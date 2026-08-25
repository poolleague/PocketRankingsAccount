using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using PocketRankingsAccount.Services;

var builder = WebApplication.CreateBuilder(args);

// Console logging works consistently for local runs and Docker deployments,
// matching PoolLeagueWeb's Program.cs.
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

// MVC powers the Controllers + Views folders, matching PoolLeagueWeb.
builder.Services.AddControllersWithViews(options =>
{
    options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
});

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    // Caddy is intended to be the only public ingress, matching
    // PoolLeagueWeb -- not yet true today, since nothing routes to this
    // service publicly yet (see PLATFORM_AGENTS.md Section 1 open item).
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

// Singleton, matching PoolLeagueWeb's builder.Services.AddSingleton<LeagueRepository>().
// AccountRepository opens its own NpgsqlConnection per method call rather
// than holding one open connection for the app's lifetime, so a singleton
// is safe here -- there's no per-request state to isolate.
builder.Services.AddSingleton<AccountRepository>();
builder.Services.AddSingleton<AccountSecurityFoundationService>();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.Cookie.Name = "PocketRankingsAccount.Session";
        options.Cookie.HttpOnly = true;
        // SameAsRequest in Development so plain-http localhost testing
        // works without HTTPS; Always in every other environment, matching
        // PoolLeagueWeb's posture of never sending the auth cookie over
        // plain HTTP once deployed.
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.ExpireTimeSpan = TimeSpan.FromHours(24);
        options.SlidingExpiration = true;
        // Re-validates against the database on every request rather than
        // trusting the encrypted cookie indefinitely, matching
        // PoolLeagueWeb's OnValidatePrincipal -> SecurityFoundationService
        // pattern. A session that's been revoked, expired, or invalidated
        // by a security-generation bump gets rejected immediately, not
        // just whenever the cookie itself would have expired.
        options.Events.OnValidatePrincipal = async context =>
        {
            var sessionHandle = context.Principal?.FindFirst(AccountSecurityFoundationService.SessionCookieClaim)?.Value;
            var security = context.HttpContext.RequestServices.GetRequiredService<AccountSecurityFoundationService>();
            if (string.IsNullOrEmpty(sessionHandle) || security.ValidateSession(sessionHandle, DateTime.UtcNow) is null)
            {
                context.RejectPrincipal();
                await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            }
        };
    });

var app = builder.Build();

// DI singletons are lazy -- nothing constructs AccountRepository (and runs
// its startup schema creation) until something requests one. No controller
// depends on it yet, so without this line the schema would silently never
// get created. Resolving it here once, eagerly, matches PoolLeagueWeb's
// documented "startup creates the schema-owned tables automatically"
// behavior (see POSTGRES_SCHEMA_LAYOUT.md) instead of only promising it.
app.Services.GetRequiredService<AccountRepository>();
var security = app.Services.GetRequiredService<AccountSecurityFoundationService>();

if (!security.IsConfigured)
{
    app.Logger.LogWarning(
        "Security:FoundationHashKey is not configured. Login/signup/session " +
        "validation will throw until a base64-encoded key of at least 32 " +
        "bytes is provided -- see .env.example.");
}
if (security.IsUsingEphemeralSigningKey)
{
    app.Logger.LogWarning(
        "No Security:TokenSigningPrivateKeyPem configured -- using an " +
        "ephemeral in-memory signing key. Identity tokens issued this run " +
        "will not verify after a restart and no other product can verify " +
        "them at all. Fine for local dev; do not run Production this way.");
}

app.UseForwardedHeaders();

// Production error handling. Development mode keeps normal detailed errors,
// matching PoolLeagueWeb.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.Use(async (context, next) =>
{
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    await next();
});

app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(name: "default", pattern: "{controller=Home}/{action=Index}/{id?}");

// Minimal health endpoint so the hosting pipeline can be verified end to
// end without depending on any controller/view work being finished.
app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "account" }));

// The "published public key" PLATFORM_AGENTS.md Section 2.1 describes --
// standard location (RFC 7517 / OpenID Connect convention) for other
// products to eventually fetch and cache this from, once they implement
// verification. Nothing consumes this yet; publishing it doesn't require
// waiting for a consumer to exist.
app.MapGet("/.well-known/jwks.json", (AccountSecurityFoundationService security) =>
    Results.Json(security.GetJsonWebKeySet()));

app.Run();

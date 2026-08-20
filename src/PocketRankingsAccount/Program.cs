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

// Still deliberately NOT wired up -- do not add without a proposal per
// PLATFORM_AGENTS.md Section 8:
//   - Cookie authentication / session validation. PoolLeagueWeb ties this
//     to SecurityFoundationService.ValidateSession; Account needs its own
//     equivalent service that calls AccountRepository to check real
//     PersonCredential/PersonSession rows. The repository now exists, but
//     the service that uses it for auth does not yet.
//   - Identity token issuance/signing -- same dependency.

var app = builder.Build();

// DI singletons are lazy -- nothing constructs AccountRepository (and runs
// its startup schema creation) until something requests one. No controller
// depends on it yet, so without this line the schema would silently never
// get created. Resolving it here once, eagerly, matches PoolLeagueWeb's
// documented "startup creates the schema-owned tables automatically"
// behavior (see POSTGRES_SCHEMA_LAYOUT.md) instead of only promising it.
app.Services.GetRequiredService<AccountRepository>();

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

// app.UseAuthentication() / app.UseAuthorization() return once real
// authentication exists -- see note above.

app.MapControllerRoute(name: "default", pattern: "{controller=Home}/{action=Index}/{id?}");

// Minimal health endpoint so the hosting pipeline can be verified end to
// end without depending on any controller/view work being finished.
app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "account" }));

app.Run();

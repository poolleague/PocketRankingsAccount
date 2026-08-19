using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;

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

// Deliberately NOT wired up yet -- do not add without a proposal per
// PLATFORM_AGENTS.md Section 8:
//   - Cookie authentication / session validation. PoolLeagueWeb ties this
//     to SecurityFoundationService.ValidateSession; Account needs its own
//     equivalent service, checking real PersonCredential/PersonSession
//     rows, which in turn needs AccountRepository (Postgres access) to
//     exist first. Wiring a cookie scheme with nothing real behind it
//     would be worse than not having one.
//   - Identity token issuance/signing -- same dependency.
// ConnectionStrings__PostgresDatabase is already defined in
// docker-compose.yml and ready to read; nothing consumes it yet.

var app = builder.Build();

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

using AniTether.Api.Data;
using AniTether.Api.Features.Anime;
using AniTether.Api.Features.Auth;
using AniTether.Api.Features.Import;
using AniTether.Api.Features.List;
using AniTether.Api.Features.Sources;
using AniTether.Api.Features.Sync;
using AniTether.Api.Features.Users;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// Production logs are one JSON object per line on stdout (read with `docker logs`).
if (!builder.Environment.IsDevelopment())
{
    builder.Logging.ClearProviders().AddJsonConsole();
}

// Global request body cap. The largest legitimate body is a MAL export upload (5 MB,
// checked again in the import endpoint).
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 6 * 1024 * 1024);

builder.Services.AddProblemDetails();

// Behind a reverse proxy / Cloudflare Tunnel the TCP peer is the proxy, so every per-IP
// rate limit would share one bucket. Trust the client-IP header only from the networks
// listed in ForwardedHeaders__KnownNetworks (loopback is trusted by default).
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ForwardedForHeaderName = builder.Configuration["ForwardedHeaders:ClientIpHeader"] ?? "X-Forwarded-For";
    foreach (var network in (builder.Configuration["ForwardedHeaders:KnownNetworks"] ?? "")
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
        options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
    }
});

// Without a persisted key ring every restart of a container invalidates all auth cookies
// (and anything else Data Protection encrypted).
if (builder.Configuration["DataProtection:KeysPath"] is { Length: > 0 } keysPath)
{
    builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(new DirectoryInfo(keysPath))
        .SetApplicationName("AniTether");
}

builder.Services.AddOpenApi();
builder.Services.ConfigureHttpJsonOptions(options =>
    // Names only: an integer would bind to any value, including ones the enum doesn't define.
    options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter(allowIntegerValues: false)));

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));
builder.Services.AddHostedService<SyncHistoryCleanupService>();

builder.Services
    .AddIdentityCore<ApplicationUser>(options =>
    {
        options.SignIn.RequireConfirmedAccount = false;
        // Also turns on email format validation (Identity only checks it when unique).
        options.User.RequireUniqueEmail = true;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<AppDbContext>()
    .AddSignInManager();

builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme)
    .AddCookie(IdentityConstants.ApplicationScheme, options =>
    {
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;
        // Rejects cookies whose security stamp was rotated (role change, lock, password
        // reset) and re-issues the principal with fresh role claims.
        options.Events.OnValidatePrincipal = SecurityStampValidator.ValidatePrincipalAsync;
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    })
    // Never issued (no 2FA), but SecurityStampValidator signs it out when it rejects a
    // cookie and throws if the scheme doesn't exist.
    .AddCookie(IdentityConstants.TwoFactorRememberMeScheme);

builder.Services.Configure<SecurityStampValidatorOptions>(options => options.ValidationInterval = TimeSpan.FromMinutes(1));

builder.Services.AddAuthorization();

builder.Services.AddRateLimiter(options =>
{
    // Partitioned per client IP (the real one, via forwarded headers above) — a single
    // shared bucket would let one caller lock every other user out.
    void PerIp(string policy, int perMinute) => options.AddPolicy(policy, context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = perMinute, Window = TimeSpan.FromMinutes(1) }));

    PerIp("auth", 10);
    PerIp("sourceLinks", 20);
    PerIp("resolveLinks", 10);
    PerIp("healthCheck", 5);
    PerIp("userAdmin", 30);
    // Endpoints that may call AniList, whose own limit (90/min) is shared by every user.
    PerIp("aniList", 60);
    PerIp("import", 5);
    PerIp("sync", 60);
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

builder.Services.AddMemoryCache();
builder.Services.AddHttpClient<AniListClient>();
// The DNS guard must resolve the origin itself; a proxy would move that lookup out of process.
builder.Services.AddHttpClient<SourceLinkResolver>(c => c.Timeout = TimeSpan.FromSeconds(5))
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false, UseProxy = false, ConnectCallback = PublicInternetConnector.ConnectAsync });
builder.Services.AddHttpClient<SourceHealthProbe>(c => c.Timeout = TimeSpan.FromSeconds(5))
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false, UseProxy = false, ConnectCallback = PublicInternetConnector.ConnectAsync });

// The health run lives on a singleton so the admin endpoint and the daily timer share one
// run slot (`TryStartInBackground`) and the report can read `Running`.
builder.Services.AddSingleton<HealthAlertSender>();
builder.Services.AddSingleton<SourceHealthService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<SourceHealthService>());

var app = builder.Build();

// A one-shot container runs the image with Database__MigrateAndExit=true
// and the schema owner's connection string, then exits. The long-running API connects as
// a role with DML only, so it never holds DDL rights or the owner's password.
if (app.Configuration.GetValue<bool>("Database:MigrateAndExit"))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    return;
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
    // ProblemDetails body, no stack trace; the exception itself is logged server-side.
    app.UseExceptionHandler();
    app.UseHsts();
}

app.Use(async (context, next) =>
{
    // OnStarting, not up front: UseExceptionHandler clears the response (headers included)
    // before writing its 500, which would strip these from error responses.
    context.Response.OnStarting(() =>
    {
        var headers = context.Response.Headers;
        headers.XContentTypeOptions = "nosniff";
        headers.XFrameOptions = "DENY";
        headers["Referrer-Policy"] = "no-referrer";
        // frame-src is https: rather than a host list: embed hosts are `sources` rows (a DB
        // edit, not a deploy), so a fixed list would silently break every newly added source.
        headers.ContentSecurityPolicy =
            "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' https: data:; " +
            "frame-src https:; connect-src 'self'; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'";
        return Task.CompletedTask;
    });
    await next();
});

// Production serves the built SPA (web/dist copied into wwwroot by the Dockerfile) from
// the same origin as the API. Dev uses Vite's server and proxy instead.
app.UseStaticFiles();

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

// Anonymous liveness for Docker/uptime checks: 200 when the DB answers, 503 otherwise.
// No detail beyond that.
app.MapGet("/api/health", async (AppDbContext db) =>
{
    bool dbOk;
    try
    {
        dbOk = await db.Database.CanConnectAsync();
    }
    catch
    {
        dbOk = false;
    }

    return dbOk
        ? Results.Ok(new { status = "ok" })
        : Results.Json(new { status = "unavailable" }, statusCode: StatusCodes.Status503ServiceUnavailable);
});

app.MapAuthEndpoints();
app.MapAnimeEndpoints();
app.MapImportEndpoints();
app.MapListEndpoints();
app.MapSyncEndpoints();
app.MapSourcesEndpoints();
app.MapSourceHealthEndpoints();
app.MapUserEndpoints();
app.MapProfileEndpoints();

// Client-side routes (/, /anime/1, /settings, ...) get index.html. The fallback's own
// `{*path:nonfile}` pattern leaves /assets/*.js to UseStaticFiles. Unknown /api paths get
// a real 404 instead of the SPA (catch-all routes rank below every concrete API route).
app.Map("/api/{**rest}", () => Results.NotFound());
app.MapFallbackToFile("index.html");

await UserAdmin.BootstrapAdminAsync(app.Services, app.Configuration);

app.Run();

public partial class Program;

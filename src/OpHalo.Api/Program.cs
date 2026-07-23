using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;
using OpHalo.Api.Accounts;
using OpHalo.Api.Auth;
using OpHalo.Api.Diagnostics;
using OpHalo.Api.Helpers;
using OpHalo.Api.Keep;
using OpHalo.Foundation.Application.Abstractions.Messaging;
using OpHalo.Foundation.Application.Abstractions.Security;
using OpHalo.Foundation.Application.Accounts.Access;
using OpHalo.Foundation.Application.Accounts.Authorization;
using OpHalo.Foundation.Application.Accounts.Entitlements;
using OpHalo.Foundation.Application.Accounts.Provisioning;
using OpHalo.Foundation.Application.Auth;
using OpHalo.Foundation.Application.Devices;
using OpHalo.Foundation.Application.Members;
using OpHalo.Foundation.Application.Push;
using OpHalo.Foundation.Core.Constants;
using OpHalo.Foundation.Infrastructure.Auth;
using OpHalo.Foundation.Infrastructure.Devices;
using OpHalo.Foundation.Infrastructure.Email;
using OpHalo.Foundation.Infrastructure.Members;
using OpHalo.Foundation.Infrastructure.Persistence;
using OpHalo.Foundation.Infrastructure.Push;
using OpHalo.Foundation.Infrastructure.Security;
using OpHalo.Foundation.Infrastructure.Services;
using OpHalo.SharedKernel.Abstractions;

var builder = WebApplication.CreateBuilder(args);

// Railway supplies the port at runtime. Bind explicitly instead of relying on shell-style
// expansion inside ASPNETCORE_URLS, which the container entrypoint does not perform.
var railwayPort = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrWhiteSpace(railwayPort))
    builder.WebHost.UseUrls($"http://0.0.0.0:{railwayPort}");

// Suppress Microsoft.AspNetCore.Hosting.Diagnostics request-path logs below Warning.
// Those logs can emit raw route paths which may include bearer tokens on public-token routes.
// appsettings.json already sets "Microsoft.AspNetCore": "Warning" but this code-level filter
// makes the intent explicit and durable against config changes (GAP-013, G8b).
builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning);

// RFC 7807 ProblemDetails support across all error responses.
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

// --- CORS ---
// Explicit origins only — no wildcard. AllowCredentials required for cookie transport.
// Origins are read lazily via a local variable so the config section is still evaluated
// at startup (not per-request), but the registration is straightforward.
var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
{
    options.AddPolicy("ophalo", policy =>
    {
        if (corsOrigins.Length > 0)
            policy.WithOrigins(corsOrigins)
                  .AllowCredentials()
                  .AllowAnyMethod()
                  .AllowAnyHeader();
    });
});

// --- Persistence ---
// Connection string is read lazily from IConfiguration inside the factory so that
// WebApplicationFactory.ConfigureAppConfiguration overrides are visible at scope-creation
// time (the fully-merged IConfiguration in DI includes test overrides; builder.Configuration
// at startup does not). Throws on first scope creation if the string is missing.
builder.Services.AddScoped<OpHaloDbContext>(sp =>
{
    var cs = sp.GetRequiredService<IConfiguration>().GetConnectionString("DefaultConnection");
    if (string.IsNullOrWhiteSpace(cs))
        throw new InvalidOperationException(
            "Connection string 'DefaultConnection' is required. " +
            "Supply it via user secrets, environment variable, or appsettings.");

    cs = NormalizePostgresConnectionString(cs);

    var clock = sp.GetRequiredService<IClock>();
    var options = new DbContextOptionsBuilder<OpHaloDbContext>()
        .UseNpgsql(cs, npgsql =>
        {
            npgsql.MigrationsHistoryTable("__OpHaloMigrationsHistory");
            npgsql.MigrationsAssembly(typeof(OpHaloDbContext).Assembly.FullName);
        })
        .UseSnakeCaseNamingConvention()
        .Options;
    return new OpHaloDbContext(
        options,
        clock,
        [typeof(OpHalo.Keep.Infrastructure.AssemblyMarker).Assembly]);
});

// --- Health checks ---
builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database");

// --- Services ---
builder.Services.AddSingleton<IClock, OpHalo.Foundation.Infrastructure.Services.SystemClock>();

builder.Services.AddKeepServices();

builder.Services.AddSingleton<IAccountAccessPolicy, AccountAccessPolicy>();
builder.Services.AddSingleton<IUserAccessPolicy, UserAccessPolicy>();
builder.Services.AddSingleton<IFeatureAccessPolicy, FeatureAccessPolicy>();

// --- Auth services ---
builder.Services.Configure<MagicLinkSettings>(builder.Configuration.GetSection("App"));
builder.Services.Configure<SignupDefaultsSettings>(builder.Configuration.GetSection("SignupDefaults"));
builder.Services.AddScoped<AccountProvisioningService>();
builder.Services.AddScoped<StartAuthService>();
builder.Services.AddScoped<SignInAuthService>();
builder.Services.AddScoped<ExchangeAuthService>();
builder.Services.AddScoped<RedeemMobileHandoffService>();
builder.Services.AddScoped<IAuthCodePersistence, EfAuthCodePersistence>();
builder.Services.AddScoped<IMobileHandoffCodePersistence, EfMobileHandoffCodePersistence>();
builder.Services.AddScoped<SendInviteService>();
builder.Services.AddScoped<AcceptInviteService>();
builder.Services.AddScoped<IInvitePersistence, EfInvitePersistence>();
builder.Services.AddScoped<MemberManagementService>();
builder.Services.AddScoped<IMemberManagementPersistence, EfMemberManagementPersistence>();
builder.Services.AddScoped<AccountUserDeviceService>();
builder.Services.AddScoped<IAccountUserDevicePersistence, EfAccountUserDevicePersistence>();
builder.Services.AddSingleton<IPushTokenFingerprintService, Sha256PushTokenFingerprintService>();
builder.Services.AddSingleton<IPushAdapter, NoOpPushAdapter>();

// --- Email ---
var resendSettings = builder.Configuration.GetSection("Resend").Get<ResendSettings>()
    ?? new ResendSettings();
builder.Services.AddSingleton(resendSettings);

// Dev-only console sender: writes magic-link URLs to stderr (not structured logs) so codes
// never appear in log pipelines. Resend is required in all other environments.
if (builder.Environment.IsDevelopment() && string.IsNullOrWhiteSpace(resendSettings.ApiKey))
{
    builder.Services.AddSingleton<IEmailSender, ConsoleEmailSender>();
}
else
{
    builder.Services.AddHttpClient<IEmailSender, ResendEmailSender>(httpClient =>
    {
        httpClient.BaseAddress = new Uri("https://api.resend.com");
        httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {resendSettings.ApiKey}");
    });
}

// --- Auth ---
builder.Services.AddHttpContextAccessor();
builder.Services.Configure<AuthCookieSettings>(builder.Configuration.GetSection("Auth"));
builder.Services.AddSingleton<AuthCookieOptionsFactory>();

builder.Services.AddScoped<ICurrentUser, CurrentUser>();
builder.Services.AddScoped<ISessionStore, SessionStore>();
builder.Services.AddScoped<IAccountSessionService, AccountSessionService>();

builder.Services.AddAuthentication(AuthConstants.SessionSchemeName)
    .AddScheme<AuthenticationSchemeOptions, SessionAuthenticationHandler>(
        AuthConstants.SessionSchemeName, _ => { });

builder.Services.AddAuthorization();

// --- Rate Limiting (ADR-060, session-log G8a) ---
// Per-IP fixed-window on all rate-limited routes. Real client IP is resolved from
// CF-Connecting-IP or X-Forwarded-For only when the remote is in Edge:TrustedProxyCidrs;
// untrusted peers cannot choose a partition key via forwarded headers.
//
// Trusted proxies are registered as a singleton read from IConfiguration at first use,
// not from builder.Configuration at startup, so WebApplicationFactory overrides are visible.
builder.Services.AddSingleton<IReadOnlyList<IPNetwork>>(sp =>
    sp.GetRequiredService<IConfiguration>()
        .GetSection("Edge:TrustedProxyCidrs")
        .GetChildren()
        .Select(c => c.Value)
        .Where(v => !string.IsNullOrWhiteSpace(v))
        .Select(v => IPNetwork.Parse(v!))
        .ToArray());

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy<string>("public-intake", context =>
    {
        var proxies = context.RequestServices.GetRequiredService<IReadOnlyList<IPNetwork>>();
        return RateLimitPartition.GetFixedWindowLimiter(
            ClientIpResolver.Resolve(context, proxies),
            _ => new FixedWindowRateLimiterOptions
            {
                Window = TimeSpan.FromMinutes(1),
                PermitLimit = 10,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0,
            });
    });

    options.AddPolicy<string>("auth", context =>
    {
        var proxies = context.RequestServices.GetRequiredService<IReadOnlyList<IPNetwork>>();
        return RateLimitPartition.GetFixedWindowLimiter(
            ClientIpResolver.Resolve(context, proxies),
            _ => new FixedWindowRateLimiterOptions
            {
                Window = TimeSpan.FromMinutes(1),
                PermitLimit = 10,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0,
            });
    });

    // Composite IP+token partition so shared networks don't penalise multiple customers (ADR-129).
    options.AddPolicy<string>("customer-write", context =>
    {
        var proxies = context.RequestServices.GetRequiredService<IReadOnlyList<IPNetwork>>();
        var pageToken = context.Request.RouteValues["pageToken"]?.ToString() ?? string.Empty;
        return RateLimitPartition.GetFixedWindowLimiter(
            ClientIpResolver.Resolve(context, proxies) + ":" + pageToken,
            _ => new FixedWindowRateLimiterOptions
            {
                Window = TimeSpan.FromMinutes(1),
                PermitLimit = 10,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0,
            });
    });
});

var app = builder.Build();

// Fail fast on missing production configuration rather than surfacing an obscure runtime error
// later (e.g. a blank magic link, or a silently rejected Resend call). Skipped for the local/test
// environments that intentionally omit Resend configuration and substitute a fake IEmailSender.
if (!app.Environment.IsDevelopment() &&
    !app.Environment.IsEnvironment("Testing") &&
    !app.Environment.IsEnvironment("RateLimitTesting"))
{
    ProductionConfigurationValidator.ValidateOrThrow(app.Configuration);
}

// Production deployments apply schema changes only when explicitly enabled. This keeps automatic
// migration opt-in while allowing a fresh managed database to be initialized by its API service.
if (app.Configuration.GetValue<bool>("Database:ApplyMigrationsOnStartup"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
    await db.Database.MigrateAsync();
}

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.UseMiddleware<CorrelationIdMiddleware>();

// Skip HTTPS redirect for "Testing" (ADR-058, build-log/014) and "RateLimitTesting"
// (production-like test host that still needs plain HTTP for the test server).
if (!app.Environment.IsEnvironment("Testing") && !app.Environment.IsEnvironment("RateLimitTesting"))
    app.UseHttpsRedirection();

// TestServer may supply null or an IPv4-mapped address; force loopback unconditionally so the
// trusted-proxy check in ClientIpResolver reliably matches 127.0.0.1/32 in rate limit tests.
if (app.Environment.IsEnvironment("RateLimitTesting"))
    app.Use(async (ctx, next) => { ctx.Connection.RemoteIpAddress = System.Net.IPAddress.Loopback; await next(ctx); });

app.UseCors("ophalo");
app.UseAuthentication();
app.UseAuthorization();

// "Testing" skips rate limiting so standard integration tests are not throttled (ADR-060).
// "RateLimitTesting" intentionally keeps rate limiting enabled for G8a proof tests.
if (!app.Environment.IsEnvironment("Testing"))
    app.UseRateLimiter();

// --- Health ---
// Minimal pass/fail signals for Railway's health checks. No dependency names, config values,
// or exception detail — the response body is deliberately opaque; see structured logs/alerts
// (GAP-039) for diagnosis.
app.MapGet("/health/live", () => Results.Ok(new { status = "healthy" }))
    .DisableRateLimiting();

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    ResponseWriter = (context, report) =>
    {
        context.Response.ContentType = "application/json";
        var status = report.Status == HealthStatus.Healthy ? "healthy" : "unhealthy";
        return context.Response.WriteAsync($"{{\"status\":\"{status}\"}}");
    }
}).DisableRateLimiting();

// --- Routes ---
app.MapKeepEndpoints();

app.MapAuthEndpoints();
app.MapAccountEndpoints();
app.MapAccountDeviceEndpoints();
app.MapBadgeEndpoints();

app.Run();

// Railway supplies PostgreSQL's DATABASE_URL in URI form. Npgsql uses keyword/value connection
// strings, so normalize that provider-standard URI at the configuration boundary while retaining
// compatibility with conventional Npgsql connection strings used locally and in tests.
static string NormalizePostgresConnectionString(string value)
{
    if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
        (uri.Scheme != "postgres" && uri.Scheme != "postgresql"))
        return value;

    var userInfo = Uri.UnescapeDataString(uri.UserInfo);
    var separator = userInfo.IndexOf(':');
    if (separator <= 0)
        throw new InvalidOperationException("PostgreSQL connection URL must include a username and password.");

    return new NpgsqlConnectionStringBuilder
    {
        Host = uri.Host,
        Port = uri.IsDefaultPort ? 5432 : uri.Port,
        Database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')),
        Username = userInfo[..separator],
        Password = userInfo[(separator + 1)..],
    }.ConnectionString;
}

// Required for WebApplicationFactory<Program> — exposes the auto-generated Program
// class to the integration test assembly (ADR-058).
public partial class Program { }

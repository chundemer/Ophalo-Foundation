using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using OpHalo.Api.Auth;
using OpHalo.Api.Helpers;
using OpHalo.Api.Keep;
using OpHalo.Foundation.Application.Abstractions.Messaging;
using OpHalo.Foundation.Application.Abstractions.Security;
using OpHalo.Foundation.Application.Accounts.Access;
using OpHalo.Foundation.Application.Accounts.Authorization;
using OpHalo.Foundation.Application.Accounts.Entitlements;
using OpHalo.Foundation.Application.Auth;
using OpHalo.Foundation.Infrastructure.Auth;
using OpHalo.Foundation.Infrastructure.Email;
using OpHalo.Foundation.Core.Constants;
using OpHalo.Foundation.Infrastructure.Persistence;
using OpHalo.Foundation.Infrastructure.Security;
using OpHalo.Foundation.Infrastructure.Services;
using OpHalo.Keep.Application.PublicIntake;
using OpHalo.Keep.Application.Requests;
using OpHalo.Keep.Application.Services;
using OpHalo.Keep.Infrastructure.Persistence;
using OpHalo.SharedKernel.Abstractions;

var builder = WebApplication.CreateBuilder(args);

// RFC 7807 ProblemDetails support across all error responses.
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

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

// --- Services ---
builder.Services.AddSingleton<IClock, OpHalo.Foundation.Infrastructure.Services.SystemClock>();

builder.Services.AddScoped<IKeepIntakePersistence, KeepIntakePersistence>();
builder.Services.AddScoped<IKeepRequestListPersistence, KeepRequestListPersistence>();
builder.Services.AddScoped<KeepTokenService>();
builder.Services.AddScoped<CreateKeepPublicIntakeService>();
builder.Services.AddScoped<GetKeepRequestListService>();

builder.Services.AddSingleton<IAccountAccessPolicy, AccountAccessPolicy>();
builder.Services.AddSingleton<IUserAccessPolicy, UserAccessPolicy>();
builder.Services.AddSingleton<IFeatureAccessPolicy, FeatureAccessPolicy>();

// --- Auth services ---
builder.Services.Configure<MagicLinkSettings>(builder.Configuration.GetSection("App"));
builder.Services.AddScoped<SignInAuthService>();
builder.Services.AddScoped<ExchangeAuthService>();
builder.Services.AddScoped<IAuthCodePersistence, EfAuthCodePersistence>();

// --- Email ---
var resendSettings = builder.Configuration.GetSection("Resend").Get<ResendSettings>()
    ?? new ResendSettings();
builder.Services.AddSingleton(resendSettings);
builder.Services.AddHttpClient<IEmailSender, ResendEmailSender>(httpClient =>
{
    httpClient.BaseAddress = new Uri("https://api.resend.com");
    httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {resendSettings.ApiKey}");
});

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

// --- Rate Limiting (ADR-060) ---
// Per-IP fixed-window on all rate-limited routes. CF-Connecting-IP cannot be
// forged when Railway ingress is Cloudflare-only — a deploy-time constraint (ADR-060).
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy<string>("public-intake", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            GetClientIp(context),
            _ => new FixedWindowRateLimiterOptions
            {
                Window = TimeSpan.FromMinutes(1),
                PermitLimit = 10,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0,
            }));

    options.AddPolicy<string>("auth", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            GetClientIp(context),
            _ => new FixedWindowRateLimiterOptions
            {
                Window = TimeSpan.FromMinutes(1),
                PermitLimit = 10,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0,
            }));
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

// WebApplicationFactory sets environment to "Testing" — skip redirect so tests
// are not chased from http to https by the test server (ADR-058, build-log/014).
if (!app.Environment.IsEnvironment("Testing"))
    app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

// WebApplicationFactory sets environment to "Testing" — skip rate limiting so
// integration tests are not throttled by per-IP limits (ADR-060).
if (!app.Environment.IsEnvironment("Testing"))
    app.UseRateLimiter();

// --- Routes ---

// Public intake — anonymous, rate limited (ADR-051, ADR-059, ADR-060)
app.MapPost("/keep/public-intake/token/{publicIntakeToken}", HandlePublicIntake)
   .RequireRateLimiting("public-intake");

// Legacy alias — same handler, same rate limit policy (ADR-051)
app.MapPost("/continuity/public-intake/token/{publicIntakeToken}", HandlePublicIntake)
   .RequireRateLimiting("public-intake");

// Operator list — requires authenticated session (Phase 5A)
app.MapGet("/keep/requests", async (GetKeepRequestListService service, CancellationToken ct) =>
{
    var result = await service.ExecuteAsync(ct);
    return result.IsSuccess ? Results.Ok(result.Value) : ErrorHttpMapper.ToHttpResult(result.Error);
}).RequireAuthorization();

app.MapAuthEndpoints();

app.Run();

// --- Handlers ---

static async Task<IResult> HandlePublicIntake(
    [FromRoute] string publicIntakeToken,
    [FromBody] PublicIntakeRequest body,
    CreateKeepPublicIntakeService service,
    CancellationToken ct)
{
    var command = new CreateKeepPublicIntakeCommand(
        publicIntakeToken,
        body.CustomerName,
        body.CustomerPhone,
        body.CustomerEmail,
        body.Description);

    var result = await service.ExecuteAsync(command, ct);

    if (!result.IsSuccess)
        return ErrorHttpMapper.ToHttpResult(result.Error);

    return Results.Created(
        (string?)null,
        new { result.Value.RequestId, result.Value.ReferenceCode, result.Value.PageToken });
}

// --- Utilities ---

// Resolves the real client IP for per-IP rate limit partitioning (ADR-060).
static string GetClientIp(HttpContext context)
{
    var cfIp = context.Request.Headers["CF-Connecting-IP"].FirstOrDefault();
    if (!string.IsNullOrWhiteSpace(cfIp))
        return cfIp;

    var forwarded = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
    if (!string.IsNullOrWhiteSpace(forwarded))
    {
        var first = forwarded.Split(',')[0].Trim();
        if (!string.IsNullOrWhiteSpace(first))
            return first;
    }

    return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}

// Required for WebApplicationFactory<Program> — exposes the auto-generated Program
// class to the integration test assembly (ADR-058).
public partial class Program { }

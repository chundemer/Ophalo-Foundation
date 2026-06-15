using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using OpHalo.Api.Keep;
using OpHalo.Foundation.Application.Abstractions.Security;
using OpHalo.Foundation.Application.Accounts.Access;
using OpHalo.Foundation.Application.Accounts.Authorization;
using OpHalo.Foundation.Application.Accounts.Entitlements;
using OpHalo.Foundation.Infrastructure.Persistence;
using OpHalo.Foundation.Infrastructure.Security;
using OpHalo.Foundation.Infrastructure.Services;
using OpHalo.Keep.Application.PublicIntake;
using OpHalo.Keep.Application.Requests;
using OpHalo.Keep.Application.Services;
using OpHalo.Keep.Infrastructure.Persistence;
using OpHalo.SharedKernel.Abstractions;
using OpHalo.SharedKernel.Results;

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
builder.Services.AddSingleton<IClock, SystemClock>();

builder.Services.AddScoped<IKeepIntakePersistence, KeepIntakePersistence>();
builder.Services.AddScoped<IKeepRequestListPersistence, KeepRequestListPersistence>();
builder.Services.AddScoped<KeepTokenService>();
builder.Services.AddScoped<CreateKeepPublicIntakeService>();
builder.Services.AddScoped<GetKeepRequestListService>();

builder.Services.AddSingleton<IAccountAccessPolicy, AccountAccessPolicy>();
builder.Services.AddSingleton<IUserAccessPolicy, UserAccessPolicy>();
builder.Services.AddSingleton<IFeatureAccessPolicy, FeatureAccessPolicy>();

// --- Identity ---
// AnonymousCurrentUser until Phase 5 auth (ADR-058).
// Tests override this via WebApplicationFactory.ConfigureTestServices.
builder.Services.AddScoped<ICurrentUser, AnonymousCurrentUser>();

// --- Rate Limiting (ADR-060) ---
// Per-IP fixed-window on all unauthenticated public routes. CF-Connecting-IP cannot be
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
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

// WebApplicationFactory sets environment to "Testing" — skip redirect so tests
// are not chased from http to https by the test server (ADR-058, build-log/014).
if (!app.Environment.IsEnvironment("Testing"))
    app.UseHttpsRedirection();

app.UseRateLimiter();

// --- Routes ---

// Public intake — anonymous, rate limited (ADR-051, ADR-059, ADR-060)
app.MapPost("/keep/public-intake/token/{publicIntakeToken}", HandlePublicIntake)
   .RequireRateLimiting("public-intake");

// Legacy alias — same handler, same rate limit policy (ADR-051)
app.MapPost("/continuity/public-intake/token/{publicIntakeToken}", HandlePublicIntake)
   .RequireRateLimiting("public-intake");

// Operator list — no RequireAuthorization() until Phase 5 auth; service fails closed (ADR-058)
app.MapGet("/keep/requests", async (GetKeepRequestListService service, CancellationToken ct) =>
{
    var result = await service.ExecuteAsync(ct);
    return result.IsSuccess ? Results.Ok(result.Value) : ToProblem(result.Error);
});

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
        return ToProblem(result.Error);

    return Results.Created(
        (string?)null,
        new { result.Value.RequestId, result.Value.ReferenceCode, result.Value.PageToken });
}

// --- Error mapping ---
// Maps domain errors to RFC 7807 ProblemDetails (build-log/014, ADR-059).
// Validation errors (KeepRequest.*Required) fall through to the default 400 —
// no enumeration of specific codes needed; all unknown codes are client errors.
static IResult ToProblem(Error error) => error.Code switch
{
    "auth.unauthorized"              => Problem(StatusCodes.Status401Unauthorized,         "Unauthorized.",         error),
    "auth.forbidden"                 => Problem(StatusCodes.Status403Forbidden,            "Forbidden.",            error),
    "keep.public_intake.unavailable" => Problem(StatusCodes.Status422UnprocessableEntity, "Unprocessable entity.", error),
    _                                => Problem(StatusCodes.Status400BadRequest,           "Bad request.",          error),
};

static IResult Problem(int statusCode, string title, Error error) =>
    Results.Problem(
        statusCode: statusCode,
        title: title,
        detail: error.Message,
        type: "about:blank",
        extensions: new Dictionary<string, object?> { ["code"] = error.Code });

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

using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using OpHalo.Api.Accounts;
using OpHalo.Api.Keep;
using OpHalo.Api.Auth;
using OpHalo.Api.Helpers;
using OpHalo.Foundation.Application.Abstractions.Messaging;
using OpHalo.Foundation.Application.Abstractions.Security;
using OpHalo.Foundation.Application.Accounts.Access;
using OpHalo.Foundation.Application.Accounts.Authorization;
using OpHalo.Foundation.Application.Accounts.Entitlements;
using OpHalo.Foundation.Application.Accounts.Provisioning;
using OpHalo.Foundation.Application.Auth;
using OpHalo.Foundation.Application.Members;
using OpHalo.Foundation.Infrastructure.Auth;
using OpHalo.Foundation.Infrastructure.Email;
using OpHalo.Foundation.Infrastructure.Members;
using OpHalo.Foundation.Core.Constants;
using OpHalo.Foundation.Infrastructure.Persistence;
using OpHalo.Foundation.Infrastructure.Security;
using OpHalo.Foundation.Infrastructure.Services;
using OpHalo.Keep.Application.IntakeSetup;
using OpHalo.Keep.Application.PublicIntake;
using OpHalo.Keep.Application.Requests;
using OpHalo.Keep.Application.Services;
using OpHalo.Keep.Core.Domain;
using OpHalo.Keep.Core.Entities.Enums;
using OpHalo.Keep.Core.Errors;
using OpHalo.Keep.Infrastructure.Cursors;
using OpHalo.Keep.Infrastructure.Persistence;
using OpHalo.SharedKernel.Abstractions;

var builder = WebApplication.CreateBuilder(args);

// Suppress Microsoft.AspNetCore.Hosting.Diagnostics request-path logs below Warning.
// Those logs can emit raw route paths which may include bearer tokens on public-token routes.
// appsettings.json already sets "Microsoft.AspNetCore": "Warning" but this code-level filter
// makes the intent explicit and durable against config changes (GAP-013, G8b).
builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning);

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
builder.Services.AddScoped<IKeepIntakeSetupPersistence, KeepIntakeSetupPersistence>();
builder.Services.AddScoped<IKeepBusinessRequestPersistence, KeepBusinessRequestPersistence>();
builder.Services.AddScoped<CreateBusinessRequestService>();
builder.Services.AddScoped<KeepIntakeSetupService>();
builder.Services.AddScoped<IKeepRequestListPersistence, KeepRequestListPersistence>();
builder.Services.AddScoped<IKeepRequestDetailPersistence, EfKeepRequestDetailPersistence>();
builder.Services.AddScoped<IKeepRequestOperatePersistence, EfKeepRequestOperatePersistence>();
builder.Services.AddScoped<KeepTokenService>();
builder.Services.AddScoped<CreateKeepPublicIntakeService>();
builder.Services.AddScoped<GetKeepRequestListService>();
builder.Services.AddScoped<GetAvailableKeepRequestsService>();
// Cursor signing key read lazily from IConfiguration so that WebApplicationFactory
// overrides in ConfigureAppConfiguration are visible at scope-creation time.
builder.Services.AddScoped<IKeepRequestListCursorProtector>(sp =>
{
    var keyBase64 = sp.GetRequiredService<IConfiguration>()["Keep:RequestListCursorSigningKey"];
    if (string.IsNullOrWhiteSpace(keyBase64))
        throw new InvalidOperationException(
            "Keep:RequestListCursorSigningKey is required. " +
            "Supply it via user secrets, environment variable, or appsettings.");
    return new HmacKeepRequestListCursorProtector(Convert.FromBase64String(keyBase64));
});
builder.Services.AddScoped<GetKeepRequestDetailService>();
builder.Services.AddScoped<GetKeepCustomerPageService>();
builder.Services.AddScoped<ChangeKeepRequestStatusService>();
builder.Services.AddScoped<AddBusinessUpdateService>();
builder.Services.AddScoped<AddInternalNoteService>();
builder.Services.AddScoped<AcknowledgeAttentionService>();
builder.Services.AddScoped<LogExternalContactService>();
builder.Services.AddScoped<ManageResponsibleService>();
builder.Services.AddScoped<ManageWatcherService>();
builder.Services.AddScoped<SelfWatchService>();
builder.Services.AddScoped<MuteService>();
builder.Services.AddScoped<MarkFeedbackReviewedService>();
builder.Services.AddScoped<GetParticipantCandidatesService>();
builder.Services.AddScoped<KeepRequestParticipationService>();
builder.Services.AddScoped<KeepPublicCustomerAccessGuard>();
builder.Services.AddScoped<AddCustomerMessageService>();
builder.Services.AddScoped<SubmitFeedbackService>();
builder.Services.AddScoped<IKeepCustomerWritePersistence, EfKeepCustomerWritePersistence>();

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
builder.Services.AddScoped<IAuthCodePersistence, EfAuthCodePersistence>();
builder.Services.AddScoped<SendInviteService>();
builder.Services.AddScoped<AcceptInviteService>();
builder.Services.AddScoped<IInvitePersistence, EfInvitePersistence>();
builder.Services.AddScoped<MemberManagementService>();
builder.Services.AddScoped<IMemberManagementPersistence, EfMemberManagementPersistence>();

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

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

// Skip HTTPS redirect for "Testing" (ADR-058, build-log/014) and "RateLimitTesting"
// (production-like test host that still needs plain HTTP for the test server).
if (!app.Environment.IsEnvironment("Testing") && !app.Environment.IsEnvironment("RateLimitTesting"))
    app.UseHttpsRedirection();

// TestServer may supply null or an IPv4-mapped address; force loopback unconditionally so the
// trusted-proxy check in ClientIpResolver reliably matches 127.0.0.1/32 in rate limit tests.
if (app.Environment.IsEnvironment("RateLimitTesting"))
    app.Use(async (ctx, next) => { ctx.Connection.RemoteIpAddress = System.Net.IPAddress.Loopback; await next(ctx); });

app.UseAuthentication();
app.UseAuthorization();

// "Testing" skips rate limiting so standard integration tests are not throttled (ADR-060).
// "RateLimitTesting" intentionally keeps rate limiting enabled for G8a proof tests.
if (!app.Environment.IsEnvironment("Testing"))
    app.UseRateLimiter();

// --- Routes ---

// Public intake — anonymous, rate limited (ADR-051, ADR-059, ADR-060)
app.MapPost("/keep/public-intake/token/{publicIntakeToken}", HandlePublicIntake)
   .RequireRateLimiting("public-intake");

// Intake setup — authenticated, Owner/Admin only (GAP-001)
app.MapGet("/keep/setup/intake", async (KeepIntakeSetupService service, CancellationToken ct) =>
{
    var result = await service.GetStatusAsync(ct);
    return result.IsSuccess ? Results.Ok(result.Value) : ErrorHttpMapper.ToHttpResult(result.Error);
}).RequireAuthorization();

app.MapPost("/keep/setup/intake/ensure", async (KeepIntakeSetupService service, CancellationToken ct) =>
{
    var result = await service.EnsureAsync(ct);
    return result.IsSuccess ? Results.Ok(result.Value) : ErrorHttpMapper.ToHttpResult(result.Error);
}).RequireAuthorization();

app.MapPost("/keep/setup/intake/replace", async (KeepIntakeSetupService service, CancellationToken ct) =>
{
    var result = await service.ReplaceAsync(ct);
    return result.IsSuccess ? Results.Ok(result.Value) : ErrorHttpMapper.ToHttpResult(result.Error);
}).RequireAuthorization();

// Operator list — requires authenticated session (Phase 5A, extended Session 4A)
app.MapGet("/keep/requests", async (
    HttpRequest request,
    GetKeepRequestListService service,
    CancellationToken ct) =>
{
    var bind = KeepRequestListQueryBinding.Bind(request.Query);
    if (!bind.IsSuccess)
        return ErrorHttpMapper.ToHttpResult(bind.Error);

    var result = await service.ExecuteAsync(bind.Value, ct);
    return result.IsSuccess ? Results.Ok(result.Value) : ErrorHttpMapper.ToHttpResult(result.Error);
}).RequireAuthorization();

// Business-created request — authenticated, Owner/Admin/Operator (G3b)
app.MapPost("/keep/requests", async (
    CreateBusinessRequestBody body,
    CreateBusinessRequestService service,
    CancellationToken ct) =>
{
    var command = new CreateBusinessRequestCommand(
        body.CustomerName, body.CustomerPhone, body.CustomerEmail, body.Description);
    var result = await service.ExecuteAsync(command, ct);
    return result.IsSuccess
        ? Results.Created($"/keep/requests/{result.Value.RequestId}", result.Value)
        : ErrorHttpMapper.ToHttpResult(result.Error);
}).RequireAuthorization();

// Available queue — Operator-only dedicated surface (G4d)
app.MapGet("/keep/requests/available", async (
    HttpRequest request,
    GetAvailableKeepRequestsService service,
    CancellationToken ct) =>
{
    var bind = KeepAvailableRequestQueryBinding.Bind(request.Query);
    if (!bind.IsSuccess)
        return ErrorHttpMapper.ToHttpResult(bind.Error);

    var result = await service.ExecuteAsync(bind.Value, ct);
    return result.IsSuccess ? Results.Ok(result.Value) : ErrorHttpMapper.ToHttpResult(result.Error);
}).RequireAuthorization();

// Operator request detail — authenticated, scoped to caller's account (Phase 8-B1-β)
app.MapGet("/keep/requests/{requestId:guid}", async (
    Guid requestId,
    GetKeepRequestDetailService service,
    CancellationToken ct) =>
{
    var result = await service.ExecuteAsync(requestId, ct);
    return result.IsSuccess ? Results.Ok(result.Value) : ErrorHttpMapper.ToHttpResult(result.Error);
}).RequireAuthorization();

// Change request status — authenticated, operator write (Phase 8-B2-alpha)
app.MapPatch("/keep/requests/{requestId:guid}/status", async (
    Guid requestId,
    HttpRequest httpRequest,
    ChangeStatusRequestBody body,
    ChangeKeepRequestStatusService service,
    CancellationToken ct) =>
{
    var versionResult = KeepRequestVersionHeader.Parse(httpRequest.Headers);
    if (!versionResult.IsSuccess)
        return ErrorHttpMapper.ToHttpResult(versionResult.Error);

    var command = new ChangeKeepRequestStatusCommand(requestId, body.Status, body.Message, versionResult.Value);
    var result = await service.ExecuteAsync(command, ct);
    return result.IsSuccess ? Results.Ok(result.Value) : ErrorHttpMapper.ToHttpResult(result.Error);
}).RequireAuthorization();

// Add business update — authenticated, operator write (Phase 8-B2-beta)
app.MapPost("/keep/requests/{requestId:guid}/business-updates", async (
    Guid requestId,
    HttpRequest httpRequest,
    BusinessUpdateRequestBody body,
    AddBusinessUpdateService service,
    CancellationToken ct) =>
{
    var versionResult = KeepRequestVersionHeader.Parse(httpRequest.Headers);
    if (!versionResult.IsSuccess)
        return ErrorHttpMapper.ToHttpResult(versionResult.Error);

    var command = new AddBusinessUpdateCommand(requestId, body.Message, body.SetStatus, versionResult.Value);
    var result = await service.ExecuteAsync(command, ct);
    return result.IsSuccess ? Results.Ok(result.Value) : ErrorHttpMapper.ToHttpResult(result.Error);
}).RequireAuthorization();

// Add internal note — authenticated, operator write (Phase 8-B2-beta)
app.MapPost("/keep/requests/{requestId:guid}/internal-notes", async (
    Guid requestId,
    HttpRequest httpRequest,
    InternalNoteRequestBody body,
    AddInternalNoteService service,
    CancellationToken ct) =>
{
    var versionResult = KeepRequestVersionHeader.Parse(httpRequest.Headers);
    if (!versionResult.IsSuccess)
        return ErrorHttpMapper.ToHttpResult(versionResult.Error);

    var command = new AddInternalNoteCommand(requestId, body.Note, versionResult.Value);
    var result = await service.ExecuteAsync(command, ct);
    return result.IsSuccess ? Results.Ok(result.Value) : ErrorHttpMapper.ToHttpResult(result.Error);
}).RequireAuthorization();

// Log external contact — authenticated, operator write (Phase 8-B5/Session 2B)
app.MapPost("/keep/requests/{requestId:guid}/external-contact", async (
    Guid requestId,
    HttpRequest httpRequest,
    ExternalContactRequestBody body,
    LogExternalContactService service,
    CancellationToken ct) =>
{
    var versionResult = KeepRequestVersionHeader.Parse(httpRequest.Headers);
    if (!versionResult.IsSuccess)
        return ErrorHttpMapper.ToHttpResult(versionResult.Error);

    var command = new LogExternalContactCommand(
        requestId, body.Direction, body.Channel, body.Outcome,
        body.RequiresBusinessFollowUp, body.Summary, versionResult.Value);
    var result = await service.ExecuteAsync(command, ct);
    return result.IsSuccess ? Results.Ok(result.Value) : ErrorHttpMapper.ToHttpResult(result.Error);
}).RequireAuthorization();

// Acknowledge attention — authenticated, operator write (Phase 8-B2-gamma)
app.MapPost("/keep/requests/{requestId:guid}/attention/acknowledge", async (
    Guid requestId,
    HttpRequest httpRequest,
    AcknowledgeAttentionRequestBody body,
    AcknowledgeAttentionService service,
    CancellationToken ct) =>
{
    var versionResult = KeepRequestVersionHeader.Parse(httpRequest.Headers);
    if (!versionResult.IsSuccess)
        return ErrorHttpMapper.ToHttpResult(versionResult.Error);

    var command = new AcknowledgeAttentionCommand(requestId, body.Reason, versionResult.Value);
    var result = await service.ExecuteAsync(command, ct);
    return result.IsSuccess ? Results.Ok(result.Value) : ErrorHttpMapper.ToHttpResult(result.Error);
}).RequireAuthorization();

// Mark feedback reviewed — authenticated, Owner/Admin write (Phase 8-B5/Session 5B, ADR-274)
app.MapPost("/keep/requests/{requestId:guid}/feedback-review", async (
    Guid requestId,
    HttpRequest httpRequest,
    FeedbackReviewRequestBody body,
    MarkFeedbackReviewedService service,
    CancellationToken ct) =>
{
    var versionResult = KeepRequestVersionHeader.Parse(httpRequest.Headers);
    if (!versionResult.IsSuccess)
        return ErrorHttpMapper.ToHttpResult(versionResult.Error);

    var command = new MarkFeedbackReviewedCommand(requestId, body.Note, versionResult.Value);
    var result = await service.ExecuteAsync(command, ct);
    return result.IsSuccess ? Results.Ok(result.Value) : ErrorHttpMapper.ToHttpResult(result.Error);
}).RequireAuthorization();

// Participant candidates — authenticated, Owner/Admin read (Phase 8-B5/Session 3B, ADR-235)
app.MapGet("/keep/requests/participant-candidates", async (
    GetParticipantCandidatesService service,
    CancellationToken ct) =>
{
    var result = await service.ExecuteAsync(ct);
    return result.IsSuccess ? Results.Ok(result.Value) : ErrorHttpMapper.ToHttpResult(result.Error);
}).RequireAuthorization();

// Assign/transfer responsible — authenticated, Owner/Admin write (Phase 8-B5/Session 3B, ADR-230)
app.MapPut("/keep/requests/{requestId:guid}/responsible", async (
    Guid requestId,
    HttpRequest httpRequest,
    SetResponsibleRequestBody body,
    ManageResponsibleService service,
    CancellationToken ct) =>
{
    var versionResult = KeepRequestVersionHeader.Parse(httpRequest.Headers);
    if (!versionResult.IsSuccess)
        return ErrorHttpMapper.ToHttpResult(versionResult.Error);

    var command = new SetResponsibleCommand(requestId, body.AccountUserId, body.Note, versionResult.Value);
    var result = await service.SetAsync(command, ct);
    return result.IsSuccess ? Results.Ok(result.Value) : ErrorHttpMapper.ToHttpResult(result.Error);
}).RequireAuthorization();

// Clear responsible — authenticated, Owner/Admin write (Phase 8-B5/Session 3B, ADR-230)
app.MapDelete("/keep/requests/{requestId:guid}/responsible", async (
    Guid requestId,
    HttpRequest httpRequest,
    [FromBody] ClearResponsibleRequestBody? body,
    ManageResponsibleService service,
    CancellationToken ct) =>
{
    var versionResult = KeepRequestVersionHeader.Parse(httpRequest.Headers);
    if (!versionResult.IsSuccess)
        return ErrorHttpMapper.ToHttpResult(versionResult.Error);

    var command = new ClearResponsibleCommand(requestId, body?.Note, versionResult.Value);
    var result = await service.ClearAsync(command, ct);
    return result.IsSuccess ? Results.Ok(result.Value) : ErrorHttpMapper.ToHttpResult(result.Error);
}).RequireAuthorization();

// Add watcher (managed) — authenticated, Owner/Admin write (Phase 8-B5/Session 3B, ADR-230)
app.MapPut("/keep/requests/{requestId:guid}/watchers/{accountUserId:guid}", async (
    Guid requestId,
    Guid accountUserId,
    HttpRequest httpRequest,
    WatcherRequestBody? body,
    ManageWatcherService service,
    CancellationToken ct) =>
{
    var versionResult = KeepRequestVersionHeader.Parse(httpRequest.Headers);
    if (!versionResult.IsSuccess)
        return ErrorHttpMapper.ToHttpResult(versionResult.Error);

    var command = new AddWatcherCommand(requestId, accountUserId, body?.Note, versionResult.Value);
    var result = await service.AddAsync(command, ct);
    return result.IsSuccess ? Results.Ok(result.Value) : ErrorHttpMapper.ToHttpResult(result.Error);
}).RequireAuthorization();

// Remove watcher (managed) — authenticated, Owner/Admin write (Phase 8-B5/Session 3B, ADR-230)
app.MapDelete("/keep/requests/{requestId:guid}/watchers/{accountUserId:guid}", async (
    Guid requestId,
    Guid accountUserId,
    HttpRequest httpRequest,
    [FromBody] WatcherRequestBody? body,
    ManageWatcherService service,
    CancellationToken ct) =>
{
    var versionResult = KeepRequestVersionHeader.Parse(httpRequest.Headers);
    if (!versionResult.IsSuccess)
        return ErrorHttpMapper.ToHttpResult(versionResult.Error);

    var command = new RemoveWatcherCommand(requestId, accountUserId, body?.Note, versionResult.Value);
    var result = await service.RemoveAsync(command, ct);
    return result.IsSuccess ? Results.Ok(result.Value) : ErrorHttpMapper.ToHttpResult(result.Error);
}).RequireAuthorization();

// Self-watch — authenticated, operator write (Phase 8-B5/Session 3B, ADR-230)
app.MapPut("/keep/requests/{requestId:guid}/watch", async (
    Guid requestId,
    HttpRequest httpRequest,
    SelfWatchService service,
    CancellationToken ct) =>
{
    var versionResult = KeepRequestVersionHeader.Parse(httpRequest.Headers);
    if (!versionResult.IsSuccess)
        return ErrorHttpMapper.ToHttpResult(versionResult.Error);

    var result = await service.WatchAsync(requestId, versionResult.Value, ct);
    return result.IsSuccess ? Results.Ok(result.Value) : ErrorHttpMapper.ToHttpResult(result.Error);
}).RequireAuthorization();

// Self-unwatch — authenticated, operator write (Phase 8-B5/Session 3B, ADR-230)
app.MapDelete("/keep/requests/{requestId:guid}/watch", async (
    Guid requestId,
    HttpRequest httpRequest,
    SelfWatchService service,
    CancellationToken ct) =>
{
    var versionResult = KeepRequestVersionHeader.Parse(httpRequest.Headers);
    if (!versionResult.IsSuccess)
        return ErrorHttpMapper.ToHttpResult(versionResult.Error);

    var result = await service.UnwatchAsync(requestId, versionResult.Value, ct);
    return result.IsSuccess ? Results.Ok(result.Value) : ErrorHttpMapper.ToHttpResult(result.Error);
}).RequireAuthorization();

// Mute — authenticated, operator write (Phase 8-B5/Session 3B, ADR-230)
app.MapPut("/keep/requests/{requestId:guid}/mute", async (
    Guid requestId,
    HttpRequest httpRequest,
    MuteService service,
    CancellationToken ct) =>
{
    var versionResult = KeepRequestVersionHeader.Parse(httpRequest.Headers);
    if (!versionResult.IsSuccess)
        return ErrorHttpMapper.ToHttpResult(versionResult.Error);

    var result = await service.MuteAsync(requestId, versionResult.Value, ct);
    return result.IsSuccess ? Results.Ok(result.Value) : ErrorHttpMapper.ToHttpResult(result.Error);
}).RequireAuthorization();

// Unmute — authenticated, operator write (Phase 8-B5/Session 3B, ADR-230)
app.MapDelete("/keep/requests/{requestId:guid}/mute", async (
    Guid requestId,
    HttpRequest httpRequest,
    MuteService service,
    CancellationToken ct) =>
{
    var versionResult = KeepRequestVersionHeader.Parse(httpRequest.Headers);
    if (!versionResult.IsSuccess)
        return ErrorHttpMapper.ToHttpResult(versionResult.Error);

    var result = await service.UnmuteAsync(requestId, versionResult.Value, ct);
    return result.IsSuccess ? Results.Ok(result.Value) : ErrorHttpMapper.ToHttpResult(result.Error);
}).RequireAuthorization();

// Customer page — anonymous, resolved by page token (Phase 8-B1-β)
// Returns 200 (active) or 410 (expired). Expired body: { businessName, referenceCode, isExpired, newRequestUrl }.
app.MapGet("/keep/r/{pageToken}", async (
    string pageToken,
    GetKeepCustomerPageService service,
    CancellationToken ct) =>
{
    var result = await service.ExecuteAsync(pageToken, ct);
    if (!result.IsSuccess)
        return ErrorHttpMapper.ToHttpResult(result.Error);

    var page = result.Value;
    return page.IsExpired
        ? Results.Json(page, statusCode: StatusCodes.Status410Gone)
        : Results.Ok(page);
});

// Customer message routes — anonymous, one route per intent, rate limited (Phase 8-B3-beta, ADR-129..131)
app.MapPost("/keep/r/{pageToken}/message",
    (string pageToken, CustomerMessageBody body, HttpRequest httpRequest, AddCustomerMessageService service, CancellationToken ct) =>
        HandleCustomerMessage(pageToken, MessageIntent.GeneralMessage, body.Message, httpRequest, service, ct))
    .RequireRateLimiting("customer-write");

app.MapPost("/keep/r/{pageToken}/question",
    (string pageToken, CustomerMessageBody body, HttpRequest httpRequest, AddCustomerMessageService service, CancellationToken ct) =>
        HandleCustomerMessage(pageToken, MessageIntent.Question, body.Message, httpRequest, service, ct))
    .RequireRateLimiting("customer-write");

app.MapPost("/keep/r/{pageToken}/update_request",
    (string pageToken, CustomerMessageBody body, HttpRequest httpRequest, AddCustomerMessageService service, CancellationToken ct) =>
        HandleCustomerMessage(pageToken, MessageIntent.UpdateRequest, body.Message, httpRequest, service, ct))
    .RequireRateLimiting("customer-write");

app.MapPost("/keep/r/{pageToken}/schedule_change_request",
    (string pageToken, CustomerMessageBody body, HttpRequest httpRequest, AddCustomerMessageService service, CancellationToken ct) =>
        HandleCustomerMessage(pageToken, MessageIntent.ScheduleChangeRequest, body.Message, httpRequest, service, ct))
    .RequireRateLimiting("customer-write");

app.MapPost("/keep/r/{pageToken}/change_or_cancel_request",
    (string pageToken, CustomerMessageBody body, HttpRequest httpRequest, AddCustomerMessageService service, CancellationToken ct) =>
        HandleCustomerMessage(pageToken, MessageIntent.ChangeOrCancelRequest, body.Message, httpRequest, service, ct))
    .RequireRateLimiting("customer-write");

app.MapPost("/keep/r/{pageToken}/issue",
    (string pageToken, CustomerMessageBody body, HttpRequest httpRequest, AddCustomerMessageService service, CancellationToken ct) =>
        HandleCustomerMessage(pageToken, MessageIntent.Complaint, body.Message, httpRequest, service, ct))
    .RequireRateLimiting("customer-write");

app.MapPost("/keep/r/{pageToken}/feedback",
    (string pageToken, FeedbackBody body, HttpRequest httpRequest, SubmitFeedbackService service, CancellationToken ct) =>
        HandleFeedback(pageToken, body, httpRequest, service, ct))
    .RequireRateLimiting("customer-write");

app.MapAuthEndpoints();
app.MapAccountEndpoints();

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

static async Task<IResult> HandleCustomerMessage(
    string pageToken,
    MessageIntent intent,
    string message,
    HttpRequest httpRequest,
    AddCustomerMessageService service,
    CancellationToken ct)
{
    var versionResult = KeepRequestVersionHeader.Parse(httpRequest.Headers);
    if (!versionResult.IsSuccess)
        return ErrorHttpMapper.ToHttpResult(versionResult.Error);

    var command = new AddCustomerMessageCommand(pageToken, intent, message, versionResult.Value);
    var result = await service.ExecuteAsync(command, ct);
    if (!result.IsSuccess)
        return ErrorHttpMapper.ToHttpResult(result.Error);

    var page = result.Value;
    return page.IsExpired
        ? Results.Json(page, statusCode: StatusCodes.Status410Gone)
        : Results.Ok(page);
}

static async Task<IResult> HandleFeedback(
    string pageToken,
    FeedbackBody body,
    HttpRequest httpRequest,
    SubmitFeedbackService service,
    CancellationToken ct)
{
    var versionResult = KeepRequestVersionHeader.Parse(httpRequest.Headers);
    if (!versionResult.IsSuccess)
        return ErrorHttpMapper.ToHttpResult(versionResult.Error);

    if (body.WasResolved is null)
        return ErrorHttpMapper.ToHttpResult(KeepRequestErrors.FeedbackResolutionRequired);

    var command = new SubmitFeedbackCommand(pageToken, body.WasResolved.Value, body.Comment, versionResult.Value);
    var result = await service.ExecuteAsync(command, ct);
    if (!result.IsSuccess)
        return ErrorHttpMapper.ToHttpResult(result.Error);

    var page = result.Value;
    return page.IsExpired
        ? Results.Json(page, statusCode: StatusCodes.Status410Gone)
        : Results.Ok(page);
}

// Required for WebApplicationFactory<Program> — exposes the auto-generated Program
// class to the integration test assembly (ADR-058).
public partial class Program { }

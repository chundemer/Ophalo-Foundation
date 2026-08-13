using OpHalo.Api.Helpers;
using OpHalo.Foundation.Application.Accounts.Entitlements;

namespace OpHalo.Api.Accounts;

/// <summary>
/// Internal-only capability-package enrollment operator path (ADR-462). Deliberately separate
/// from <c>/accounts/me/*</c> — this acts on an arbitrary target account supplied by an internal
/// operator, not the caller's own account, and is gated on <c>internal.entitlements.manage</c>
/// rather than any Business Settings permission. No account search/discovery endpoint: the caller
/// supplies a known account id.
/// </summary>
public static class InternalEntitlementsEndpoints
{
    public static void MapInternalEntitlementsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/internal/accounts/{accountId:guid}/capability-packages/{featureKey}", GetStatus)
            .RequireAuthorization();

        app.MapPost("/internal/accounts/{accountId:guid}/capability-packages/{featureKey}/enroll", Enroll)
            .RequireAuthorization();

        app.MapPost("/internal/accounts/{accountId:guid}/capability-packages/{featureKey}/disable", Disable)
            .RequireAuthorization();

        app.MapPost("/internal/accounts/{accountId:guid}/capability-packages/{featureKey}/reenable", Reenable)
            .RequireAuthorization();
    }

    private static async Task<IResult> GetStatus(
        Guid accountId,
        string featureKey,
        InternalCapabilityPackageEnrollmentApiService service,
        CancellationToken ct)
    {
        var result = await service.GetStatusAsync(accountId, featureKey, ct);
        return result.IsSuccess ? Results.Ok(result.Value) : ErrorHttpMapper.ToHttpResult(result.Error);
    }

    private static async Task<IResult> Enroll(
        Guid accountId,
        string featureKey,
        InternalCapabilityPackageEnrollmentApiService service,
        CancellationToken ct)
    {
        var result = await service.EnrollAsync(accountId, featureKey, ct);
        return result.IsSuccess ? Results.Ok(result.Value) : ErrorHttpMapper.ToHttpResult(result.Error);
    }

    private static async Task<IResult> Disable(
        Guid accountId,
        string featureKey,
        CapabilityPackageEnrollmentTransitionBody body,
        InternalCapabilityPackageEnrollmentApiService service,
        CancellationToken ct)
    {
        var result = await service.DisableAsync(accountId, featureKey, body.ConcurrencyVersion, ct);
        return result.IsSuccess ? Results.Ok(result.Value) : ErrorHttpMapper.ToHttpResult(result.Error);
    }

    private static async Task<IResult> Reenable(
        Guid accountId,
        string featureKey,
        CapabilityPackageEnrollmentTransitionBody body,
        InternalCapabilityPackageEnrollmentApiService service,
        CancellationToken ct)
    {
        var result = await service.ReenableAsync(accountId, featureKey, body.ConcurrencyVersion, ct);
        return result.IsSuccess ? Results.Ok(result.Value) : ErrorHttpMapper.ToHttpResult(result.Error);
    }
}

public sealed record CapabilityPackageEnrollmentTransitionBody(Guid ConcurrencyVersion);

using OpHalo.Keep.Core.Entities.Enums;

namespace OpHalo.Keep.Application.Requests;

/// <summary>
/// Reusable row-authorization primitive (ADR-487, build-log/129, Batch 3): true only if the current
/// user is the request's active <see cref="ParticipationType.Responsible"/> participant. Owns the
/// whole check — request-visibility load plus participant lookup, both via
/// <see cref="IKeepRequestOperatePersistence"/> on one scoped DbContext instance — so a caller never
/// needs its own <see cref="IKeepRequestOperatePersistence.GetVisibleRequestForUpdateAsync"/> call to
/// authorize an Actual Work draft mutation. An invisible/not-found request and a visible request the
/// caller does not actively lead both return <c>false</c> — intentionally indistinguishable, same
/// ADR-319 discipline as the underlying persistence call.
/// </summary>
public interface IActiveResponsibleCheck
{
    Task<bool> IsActiveResponsibleAsync(
        Guid requestId,
        Guid accountId,
        Guid accountUserId,
        KeepRequestVisibilityScope scope,
        CancellationToken ct);
}

public sealed class ActiveResponsibleCheck(IKeepRequestOperatePersistence operatePersistence) : IActiveResponsibleCheck
{
    public async Task<bool> IsActiveResponsibleAsync(
        Guid requestId, Guid accountId, Guid accountUserId, KeepRequestVisibilityScope scope, CancellationToken ct)
    {
        var request = await operatePersistence.GetVisibleRequestForUpdateAsync(
            requestId, accountId, accountUserId, scope, ct);
        if (request is null)
            return false;

        var participants = await operatePersistence.GetParticipantsForUpdateAsync(requestId, accountId, ct);
        return participants.Any(p =>
            p.IsActive && p.ParticipationType == ParticipationType.Responsible && p.AccountUserId == accountUserId);
    }
}

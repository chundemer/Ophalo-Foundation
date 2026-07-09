using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;

namespace OpHalo.Keep.Application.Setup;

public interface IKeepSetupDeferralPersistence
{
    Task<KeepBusinessSetupQueryData> GetBusinessSetupDataAsync(Guid accountId, CancellationToken ct);
    Task DeferStepAsync(KeepSetupDeferral deferral, CancellationToken ct);
    Task ClearDeferralIfPresentAsync(Guid accountId, KeepSetupStep step, DateTime clearedAtUtc, CancellationToken ct);
}

public sealed record KeepBusinessSetupQueryData(
    bool HasProfileSavedEvent,
    bool IsIntakeLinkActive,
    bool HasRequest,
    bool HasNonOwnerActiveMember,
    bool HasDeviceRegistered,
    IReadOnlyList<KeepSetupStep> DeferredSteps);

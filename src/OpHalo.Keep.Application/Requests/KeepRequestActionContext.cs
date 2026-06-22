using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Keep.Core.Entities.Enums;

namespace OpHalo.Keep.Application.Requests;

/// <summary>
/// Actor-side inputs to <see cref="KeepRequestActionPolicy.Evaluate"/>.
/// CanWrite must already incorporate the OffSeason freeze (canOperate && !isOffSeason).
/// NotificationsEnabled is meaningful only when ActiveParticipation is non-null;
/// the two fields must be consistently null or both set — inconsistency fails closed.
/// </summary>
public sealed record KeepRequestActionContext(
    AccountUserRole Role,
    bool CanWrite,
    ParticipationType? ActiveParticipation,
    bool? NotificationsEnabled);

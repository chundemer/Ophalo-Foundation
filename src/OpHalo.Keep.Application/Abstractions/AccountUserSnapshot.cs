using OpHalo.Foundation.Core.Entities.Accounts.Enums;

namespace OpHalo.Keep.Application.Abstractions;

/// <summary>
/// The AccountUser fields Keep.Application needs for UserAccessPolicy evaluation.
/// Returned by IKeepRequestListPersistence so the operator list service can gate
/// on role and membership without referencing Foundation DbSets directly.
/// </summary>
public sealed record AccountUserSnapshot(
    Guid AccountUserId,
    Guid AccountId,
    AccountUserRole Role,
    MembershipStatus MembershipStatus);

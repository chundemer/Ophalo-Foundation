using OpHalo.Foundation.Core.Entities.Accounts.Enums;

namespace OpHalo.Foundation.Infrastructure.Security;

/// <summary>
/// Immutable auth data record returned by ISessionStore to SessionAuthenticationHandler.
/// Contains only the fields the handler needs to enforce session policy and membership checks.
/// Not a domain entity — it is a narrow projection for the auth layer.
///
/// AccountUserMembershipStatus is null when the backing AccountUser row cannot be found,
/// when the AccountUser's AccountId does not match the session's AccountId (integrity check),
/// or when the store returns null for any other reason.
/// The handler authenticates only when AccountUserMembershipStatus == Active (build-log/016).
/// </summary>
public sealed record SessionData(
    Guid SessionId,
    Guid AccountId,
    Guid AccountUserId,
    DateTime ExpiresAtUtc,
    DateTime LastActivityAtUtc,
    DateTime? RevokedAtUtc,
    MembershipStatus? AccountUserMembershipStatus);

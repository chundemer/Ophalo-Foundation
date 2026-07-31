using System.Linq;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Foundation.Core.Entities.Accounts.Errors;
using OpHalo.Foundation.Core.Entities.Shared;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Foundation.Core.Entities.Accounts;

/// <summary>
/// Per-account grant of a feature key independent of <see cref="Enums.AccountPlan"/> (ADR-462).
/// A mutable state-machine row, not an event log: one row per (AccountId, FeatureKey) tracks
/// the current enrollment state and who last changed it. Read by
/// <c>AccountFeatureAccessResolver</c> alongside the plan-based <c>FeatureAccessPolicy</c> — this
/// entity never derives access on its own.
/// </summary>
public sealed class AccountCapabilityPackageEnrollment : BaseEntity
{
    public Guid AccountId { get; private set; }

    /// <summary>Must be a member of <see cref="CapabilityPackageFeatureKeys"/>.</summary>
    public string FeatureKey { get; private set; } = string.Empty;

    public CapabilityEnrollmentStatus Status { get; private set; }

    public DateTime? EnabledAt { get; private set; }

    public DateTime? DisabledAt { get; private set; }

    /// <summary>Internal OpHalo user who last changed this enrollment (actor attribution).</summary>
    public Guid ChangedByAccountUserId { get; private set; }

    /// <summary>
    /// Application-managed opaque concurrency token — same pattern as
    /// <c>KeepRequest.ConcurrencyVersion</c> (ADR-330). Rotated on every guarded transition.
    /// </summary>
    public Guid ConcurrencyVersion { get; private set; } = Guid.NewGuid();

    private AccountCapabilityPackageEnrollment()
    {
    }

    public static Result<AccountCapabilityPackageEnrollment> Enroll(
        Guid accountId, string featureKey, Guid changedByAccountUserId, DateTime nowUtc)
    {
        ValidateActorAndTime(accountId, changedByAccountUserId, nowUtc);

        if (!CapabilityPackageFeatureKeys.IsAllowed(featureKey))
            return Result<AccountCapabilityPackageEnrollment>.Failure(
                AccountCapabilityPackageEnrollmentErrors.UnknownFeatureKey);

        return Result<AccountCapabilityPackageEnrollment>.Success(new AccountCapabilityPackageEnrollment
        {
            AccountId = accountId,
            FeatureKey = featureKey,
            Status = CapabilityEnrollmentStatus.Enrolled,
            EnabledAt = nowUtc,
            DisabledAt = null,
            ChangedByAccountUserId = changedByAccountUserId,
            ConcurrencyVersion = Guid.NewGuid()
        });
    }

    /// <summary>Idempotency guard: fails rather than silently no-op'ing a duplicate disable.</summary>
    public Result Disable(Guid changedByAccountUserId, DateTime nowUtc)
    {
        ValidateActorAndTime(AccountId, changedByAccountUserId, nowUtc);

        if (Status == CapabilityEnrollmentStatus.Disabled)
            return Result.Failure(AccountCapabilityPackageEnrollmentErrors.AlreadyDisabled);

        Status = CapabilityEnrollmentStatus.Disabled;
        DisabledAt = nowUtc;
        ChangedByAccountUserId = changedByAccountUserId;
        ConcurrencyVersion = Guid.NewGuid();
        return Result.Success();
    }

    /// <summary>Idempotency guard: fails rather than silently no-op'ing a duplicate reenable.</summary>
    public Result Reenable(Guid changedByAccountUserId, DateTime nowUtc)
    {
        ValidateActorAndTime(AccountId, changedByAccountUserId, nowUtc);

        if (Status == CapabilityEnrollmentStatus.Enrolled)
            return Result.Failure(AccountCapabilityPackageEnrollmentErrors.AlreadyEnrolled);

        Status = CapabilityEnrollmentStatus.Enrolled;
        EnabledAt = nowUtc;
        DisabledAt = null;
        ChangedByAccountUserId = changedByAccountUserId;
        ConcurrencyVersion = Guid.NewGuid();
        return Result.Success();
    }

    private static void ValidateActorAndTime(Guid accountId, Guid changedByAccountUserId, DateTime nowUtc)
    {
        if (accountId == Guid.Empty)
            throw new ArgumentException("AccountId must not be empty.", nameof(accountId));
        if (changedByAccountUserId == Guid.Empty)
            throw new ArgumentException("ChangedByAccountUserId must not be empty.", nameof(changedByAccountUserId));
        if (nowUtc == default)
            throw new ArgumentException("nowUtc must not be default.", nameof(nowUtc));
        if (nowUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("nowUtc must be UTC.", nameof(nowUtc));
    }
}

/// <summary>
/// Core-owned allow-list of feature keys eligible for
/// <see cref="AccountCapabilityPackageEnrollment"/> (ADR-462). Deliberately separate from the
/// Application-layer <c>FeatureKeys</c> catalog — Core must not depend on Application, so this
/// entity validates against its own allow-list. Application's <c>FeatureKeys.Keep</c> constants
/// reference these values as their single source of truth.
/// </summary>
public static class CapabilityPackageFeatureKeys
{
    public const string PriceBookQuotesMaterials = "keep.price_book_quotes_materials";

    private static readonly IReadOnlySet<string> AllowList = new HashSet<string>(StringComparer.Ordinal)
    {
        PriceBookQuotesMaterials
    };

    /// <summary>
    /// Deterministic (ordinal-sorted) registered feature keys, for status/listing surfaces.
    /// Not the membership check itself — use <see cref="IsAllowed"/> for that.
    /// </summary>
    public static IReadOnlyList<string> All { get; } =
        AllowList.OrderBy(key => key, StringComparer.Ordinal).ToArray();

    public static bool IsAllowed(string? featureKey) =>
        !string.IsNullOrWhiteSpace(featureKey) && AllowList.Contains(featureKey);
}

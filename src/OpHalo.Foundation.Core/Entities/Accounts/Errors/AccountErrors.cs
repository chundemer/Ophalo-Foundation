using OpHalo.SharedKernel.Results;

namespace OpHalo.Foundation.Core.Entities.Accounts.Errors;

/// <summary>
/// Account domain errors — the single source of truth, referenced by both Core entity
/// methods and Application handlers. Do not duplicate these elsewhere.
/// </summary>
/// <remarks>
/// Trimmed for Phase 4a to lifecycle, access, commercial-posture, and primary-owner
/// invariants. Quota/credit/slug/intake/onboarding/pilot/session errors are out of scope
/// (their owning concerns moved to entitlements/usage and Keep public-intake).
/// </remarks>
public static class AccountErrors
{
    // --- Existence ---
    public static readonly Error NotFound =
        Error.Create("Account.NotFound", "Account not found.");

    public static readonly Error InconsistentState =
        Error.Create("Account.InconsistentState",
            "Account data is incomplete or inconsistent.");

    // --- Access ---
    public static readonly Error AccessDenied =
        Error.Create("Account.AccessDenied",
            "You do not have access to this account.");

    // --- Lifecycle ---
    public static readonly Error Inactive =
        Error.Create("Account.Inactive", "This account is not active.");

    public static readonly Error AlreadyActive =
        Error.Create("Account.AlreadyActive", "This account is already active.");

    public static readonly Error AlreadySuspended =
        Error.Create("Account.AlreadySuspended", "This account is already suspended.");

    public static readonly Error NotSuspended =
        Error.Create("Account.NotSuspended",
            "This account is not suspended and cannot be reactivated.");

    public static readonly Error AlreadyClosed =
        Error.Create("Account.AlreadyClosed", "This account is already closed.");

    public static readonly Error Suspended =
        Error.Create("Account.Suspended", "This account is suspended.");

    public static readonly Error Cancelled =
        Error.Create("Account.Cancelled", "This account has been cancelled.");

    // --- Commercial posture ---
    public static readonly Error CommercialAccessCanceled =
        Error.Create("Account.CommercialAccessCanceled",
            "Commercial access for this account has been canceled.");

    public static readonly Error TrialExpired =
        Error.Create("Account.TrialExpired",
            "Trial period has expired. Please select a plan to continue.");

    public static readonly Error PastDueBlocked =
        Error.Create("Account.PastDueBlocked",
            "Your account has an overdue balance. Please update your payment method to continue.");

    public static readonly Error Expired =
        Error.Create("Account.Expired",
            "Your subscription has expired. Please renew to continue.");

    public static readonly Error NotPastDue =
        Error.Create("Account.NotPastDue", "This account is not in a past-due state.");

    public static readonly Error CommercialAccessAlreadyCanceled =
        Error.Create("Account.CommercialAccessAlreadyCanceled",
            "Commercial access for this account is already canceled.");

    public static readonly Error OffSeasonReadOnly =
        Error.Create("Account.OffSeasonReadOnly",
            "This operation is not available during off-season mode.");

    public static readonly Error AlreadyInOffSeason =
        Error.Create("Account.AlreadyInOffSeason", "This account is already in off-season mode.");

    public static readonly Error OffSeasonEntryNotAllowed =
        Error.Create("Account.OffSeasonEntryNotAllowed",
            "Off-season mode can only be entered from an active commercial state.");

    public static readonly Error NotInOffSeason =
        Error.Create("Account.NotInOffSeason", "This account is not in off-season mode.");

    // --- Provisioning (cross-aggregate composition) ---
    public static readonly Error InternalAccountPlanMismatch =
        Error.Create("Account.InternalAccountPlanMismatch",
            "Internal accounts must use the Internal plan, and the Internal plan is reserved for internal accounts.");

    public static readonly Error InternalAccountCannotBePilot =
        Error.Create("Account.InternalAccountCannotBePilot",
            "An internal account cannot be enrolled as a pilot.");

    public static readonly Error InternalAccountAllowsNoTrialWindow =
        Error.Create("Account.InternalAccountAllowsNoTrialWindow",
            "An internal account cannot have a trial window.");

    public static readonly Error TrialWindowRequired =
        Error.Create("Account.TrialWindowRequired",
            "A trial end date is required for non-internal accounts.");

    // --- Auth ---
    public static readonly Error SessionCreationFailed =
        Error.Create("Account.SessionCreationFailed",
            "We could not finish signing you in. Please try signing in again.");

    // --- Primary owner (ADR-019) ---
    public static readonly Error PrimaryOwnerAccountMismatch =
        Error.Create("Account.PrimaryOwnerAccountMismatch",
            "The member does not belong to this account.");

    public static readonly Error PrimaryOwnerMustBeOwner =
        Error.Create("Account.PrimaryOwnerMustBeOwner",
            "The primary owner must hold the Owner role.");

    public static readonly Error PrimaryOwnerMustBeActive =
        Error.Create("Account.PrimaryOwnerMustBeActive",
            "The primary owner must be an active member.");
}

using OpHalo.SharedKernel.Results;

namespace OpHalo.Foundation.Core.Entities.Accounts.Errors;

/// <summary>
/// AccountUser domain errors — the single source of truth, referenced by both Core
/// entity methods and Application handlers. Do not duplicate these elsewhere.
/// </summary>
/// <remarks>
/// Trimmed for Phase 4a to membership transitions. Push-subscription errors are out of
/// scope (push subscriptions moved off membership per ADR-017). Unauthorized/Forbidden
/// remain an Infrastructure concern and are not modeled here.
/// </remarks>
public static class AccountUserErrors
{
    // --- Existence ---
    public static readonly Error NotFound =
        Error.Create("AccountUser.NotFound", "Account user not found.");

    public static readonly Error AlreadyExists =
        Error.Create("AccountUser.AlreadyExists", "This account user already exists.");

    // --- Status ---
    public static readonly Error Inactive =
        Error.Create("AccountUser.Inactive", "This account user is inactive.");

    public static readonly Error EmailNotVerified =
        Error.Create("AccountUser.EmailNotVerified", "Email address has not been verified.");

    public static readonly Error NotPendingInvite =
        Error.Create("AccountUser.NotPendingInvite",
            "This account user is not in a pending invite state.");

    public static readonly Error InvalidStatusTransition =
        Error.Create("AccountUser.InvalidStatusTransition",
            "This account user cannot transition from its current status.");

    // --- Registration ---
    public static readonly Error EmailAlreadyInUse =
        Error.Create("AccountUser.EmailAlreadyInUse",
            "An account user with this email already exists.");

    // --- Authentication ---
    public static readonly Error InvalidCredentials =
        Error.Create("AccountUser.InvalidCredentials", "Invalid credentials.");
}

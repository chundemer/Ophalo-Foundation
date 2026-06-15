using OpHalo.Foundation.Application.Accounts.Entitlements;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Foundation.Core.Entities.Accounts.Errors;
using OpHalo.Foundation.Core.Entities.Users;
using OpHalo.Foundation.Core.Helpers;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Foundation.Application.Accounts.Provisioning;

/// <summary>
/// Assembles the canonical object graph for a brand-new verified account — the first
/// composing caller of the Phase 4 factories. It answers <em>"given verified creation
/// inputs, what graph should exist?"</em>, not <em>"how is that graph persisted?"</em>
/// (persistence — the save contract, transaction boundary, and unique constraints — is
/// the persistence phase's job).
/// </summary>
/// <remarks>
/// Pure domain composition by design: no repositories, no unit-of-work, no DI. The value
/// is ordering and cross-aggregate consistency:
/// <list type="bullet">
/// <item>the owner membership is created before <see cref="Account.AssignPrimaryOwner"/>,
/// which validates the same-account / Owner / Active invariants in-memory — IDs exist at
/// construction via <c>Guid.CreateVersion7</c>, so no persistence round-trip is needed
/// (ADR-019/024);</item>
/// <item>seat allowance is sourced from <see cref="PlanEntitlements"/>, never caller input,
/// so an account and its entitlements cannot drift (ADR-037);</item>
/// <item>the internal-account triad — purpose <c>Internal</c> ⇒ plan <c>Internal</c>, no
/// pilot flag, no trial window — is enforced fail-closed.</item>
/// </list>
/// Per-field input validity (non-empty business name, UTC timestamps, IANA time zone) is
/// trusted to the entity factories, which throw on garbage; this service returns
/// <see cref="Result.Failure"/> only for cross-aggregate composition rules (ADR-022).
/// </remarks>
public sealed class AccountProvisioningService
{
    /// <summary>
    /// Builds the new-account graph. <paramref name="nowUtc"/> and
    /// <paramref name="trialEndsAtUtc"/> must be UTC; the entity factories enforce this.
    /// For an <see cref="AccountPurpose.Internal"/> account the plan must be
    /// <see cref="AccountPlan.Internal"/>, <paramref name="isPilot"/> must be false, and
    /// <paramref name="trialEndsAtUtc"/> must be null. For every other purpose
    /// <paramref name="trialEndsAtUtc"/> is required.
    /// </summary>
    public Result<AccountProvisioningResult> CreateVerified(
        string email,
        string? name,
        string businessName,
        AccountPurpose purpose,
        string timeZone,
        AccountPlan plan,
        bool isPilot,
        DateTime nowUtc,
        DateTime? trialEndsAtUtc)
    {
        var isInternal = purpose == AccountPurpose.Internal;

        // --- Cross-aggregate composition rules (fail-closed) ---------------------
        if (isInternal)
        {
            if (plan != AccountPlan.Internal)
                return Result<AccountProvisioningResult>.Failure(AccountErrors.InternalAccountPlanMismatch);
            if (isPilot)
                return Result<AccountProvisioningResult>.Failure(AccountErrors.InternalAccountCannotBePilot);
            if (trialEndsAtUtc is not null)
                return Result<AccountProvisioningResult>.Failure(AccountErrors.InternalAccountAllowsNoTrialWindow);
        }
        else
        {
            if (plan == AccountPlan.Internal)
                return Result<AccountProvisioningResult>.Failure(AccountErrors.InternalAccountPlanMismatch);
            if (trialEndsAtUtc is null)
                return Result<AccountProvisioningResult>.Failure(AccountErrors.TrialWindowRequired);
        }

        // Seat allowance is plan-derived, never caller-supplied (ADR-037).
        if (!PlanEntitlements.LimitDefaults.TryGetValue(plan, out var limits) ||
            !limits.TryGetValue(FeatureLimitKeys.Account.UserLimit, out var maxUserSeats))
        {
            return Result<AccountProvisioningResult>.Failure(AccountErrors.InconsistentState);
        }

        // --- Graph assembly (order matters) --------------------------------------
        var normalizedEmail = EmailNormalizer.Normalize(email);

        var user = User.CreateVerified(email, name, nowUtc);
        var account = Account.CreateVerified(businessName, purpose, timeZone);
        var owner = AccountUser.CreateOwner(account.Id, user.Id, email, normalizedEmail);

        // Self-validating (ADR-024): same account / Owner / Active. Cannot fail for the graph
        // built above, but a failure must surface rather than leave the account owner-less.
        var assign = account.AssignPrimaryOwner(owner);
        if (assign.IsFailure)
            return Result<AccountProvisioningResult>.Failure(assign.Error);

        AccountEntitlements entitlements;
        if (isInternal)
            entitlements = AccountEntitlements.CreateInternal(account.Id, maxUserSeats);
        else if (isPilot)
            entitlements = AccountEntitlements.CreatePilot(account.Id, plan, maxUserSeats, trialEndsAtUtc!.Value);
        else
            entitlements = AccountEntitlements.CreateTrial(account.Id, plan, maxUserSeats, trialEndsAtUtc!.Value);

        return Result<AccountProvisioningResult>.Success(
            new AccountProvisioningResult(user, account, owner, entitlements));
    }
}

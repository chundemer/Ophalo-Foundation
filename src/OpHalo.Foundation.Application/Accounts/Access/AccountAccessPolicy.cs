using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Foundation.Core.Entities.Accounts.Errors;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Foundation.Application.Accounts.Access;

public sealed class AccountAccessPolicy : IAccountAccessPolicy
{
    public AccountAccessDecision Evaluate(AccountAccessContext context)
    {
        // Lifecycle blocks apply to all accounts — Internal bypass does not apply here.
        if (context.LifecycleState == AccountLifecycleState.Suspended)
            return Blocked(AccountAccessReason.Suspended, AccountErrors.Suspended);

        if (context.LifecycleState == AccountLifecycleState.Closed)
            return Blocked(AccountAccessReason.Closed, AccountErrors.Cancelled);

        // Internal accounts bypass all commercial and trial checks (D-7).
        if (context.Purpose == AccountPurpose.Internal)
            return FullAccess(AccountAccessReason.Internal);

        return context.CommercialState switch
        {
            null => FullAccess(AccountAccessReason.None),

            AccountCommercialState.Active =>
                context.OperatingMode == AccountOperatingMode.OffSeason && !context.RequestImplementsAllowedInOffSeason
                    ? ReadOnly(AccountAccessReason.OffSeason, AccountErrors.OffSeasonReadOnly)
                    : FullAccess(AccountAccessReason.None),

            // null TrialEndsAtUtc is a data integrity failure — fail closed rather than
            // granting access based on a routing assumption about when StartPilot runs.
            AccountCommercialState.Trial =>
                context.TrialEndsAtUtc is null
                    ? Blocked(AccountAccessReason.TrialExpired, AccountErrors.InconsistentState)
                    : context.CurrentTimeUtc > context.TrialEndsAtUtc.Value
                        ? Blocked(AccountAccessReason.TrialExpired, AccountErrors.TrialExpired)
                        : FullAccess(AccountAccessReason.TrialActive),

            // null GracePeriodEndsUtc means no grace period was recorded — fail closed.
            AccountCommercialState.PastDue =>
                context.GracePeriodEndsUtc.HasValue && context.CurrentTimeUtc <= context.GracePeriodEndsUtc.Value
                    ? new AccountAccessDecision(AccountAccessPosture.Warning, AccountAccessReason.PastDueGrace, null)
                    : Blocked(AccountAccessReason.PastDueBlocked, AccountErrors.PastDueBlocked),

            AccountCommercialState.Expired =>
                Blocked(AccountAccessReason.Expired, AccountErrors.Expired),

            AccountCommercialState.Canceled =>
                Blocked(AccountAccessReason.Canceled, AccountErrors.CommercialAccessCanceled),

            _ => throw new ArgumentOutOfRangeException(
                nameof(context),
                context.CommercialState,
                "Unsupported account commercial state.")
        };
    }

    private static AccountAccessDecision FullAccess(AccountAccessReason reason) =>
        new(AccountAccessPosture.FullAccess, reason, null);

    private static AccountAccessDecision Blocked(AccountAccessReason reason, Error error) =>
        new(AccountAccessPosture.Blocked, reason, error);

    private static AccountAccessDecision ReadOnly(AccountAccessReason reason, Error error) =>
        new(AccountAccessPosture.ReadOnly, reason, error);
}

namespace OpHalo.Foundation.Application.Accounts.Access;

public enum AccountAccessReason
{
    None,
    TrialActive,
    TrialExpired,
    PastDueGrace,
    PastDueBlocked,
    Expired,
    Canceled,
    Suspended,
    Closed,
    OffSeason,
    Internal
}

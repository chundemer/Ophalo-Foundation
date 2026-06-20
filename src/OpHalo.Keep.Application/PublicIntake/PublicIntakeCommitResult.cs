namespace OpHalo.Keep.Application.PublicIntake;

/// <summary>
/// Result of a CommitPublicIntakeAsync attempt. Makes the retry contract
/// explicit: a naked bool would leave callers guessing what false means.
/// </summary>
public enum PublicIntakeCommitResult
{
    Committed = 1,

    /// <summary>
    /// A unique constraint on page_token or (account_id, reference_code) was
    /// violated at commit time. The caller should regenerate tokens and retry.
    /// Infrastructure detaches the failed entities before returning this value
    /// so the next attempt starts with a clean tracking state.
    /// </summary>
    UniqueTokenCollision = 2,

    /// <summary>
    /// A unique constraint on (account_id, canonical_phone) fired because a concurrent
    /// submission inserted the same customer first. The caller must re-read the winning
    /// customer, apply safe contact-update rules, then retry request/event persistence.
    /// Infrastructure detaches all failed entities before returning this value.
    /// </summary>
    CustomerCanonicalPhoneCollision = 3
}

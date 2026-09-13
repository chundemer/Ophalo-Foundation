namespace OpHalo.Foundation.Application.Feedback;

/// <summary>
/// Follow-up identity for one feedback submission's founder-channel alert (GAP-098, BL156). Domain
/// scalars only — no infrastructure/EF types — per the Application/Infrastructure boundary.
/// </summary>
public sealed record FeedbackFounderIdentity(
    string BusinessName,
    string SubmitterName,
    string Role,
    string Email);

/// <summary>
/// Resolves the current follow-up identity for a feedback submission's account/account-user pair
/// (GAP-098, BL156, ADR-500 amendment). Resolved fresh on every delivery attempt — never persisted
/// on the feedback submission row — so retries always reflect the current membership state.
/// </summary>
public interface IFeedbackIdentityReader
{
    /// <summary>
    /// Returns the current identity, or <c>null</c> when the account-user membership is missing,
    /// removed, suspended, or otherwise not active. A <c>null</c> result is not an error — the
    /// caller falls back to delivering with only the stable account/account-user IDs.
    /// </summary>
    Task<FeedbackFounderIdentity?> GetAsync(
        Guid accountId, Guid accountUserId, CancellationToken cancellationToken);
}

namespace OpHalo.Keep.Application.Requests;

/// <summary>
/// Capability-oriented visibility scope for KeepRequest row authorization (ADR-319).
/// Application services select the correct scope; Infrastructure translates it into queries.
/// </summary>
public enum KeepRequestVisibilityScope
{
    /// <summary>
    /// All requests belonging to the account. Used for Owner/Admin/Viewer detail and mutation reads.
    /// </summary>
    AccountWide = 1,

    /// <summary>
    /// Only requests where the current user has an active, eligible Responsible or Watching
    /// participation. Used for Operator detail and mutation reads.
    /// </summary>
    MyWork = 2,

    /// <summary>
    /// Combines already-participating work with active eligible Available work.
    /// Used for Operator self-assign and self-watch so existing participation remains
    /// idempotent without granting normal detail or mutation access. Implemented in G4c.
    /// </summary>
    ParticipationEntry = 3
}

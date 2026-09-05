using OpHalo.SharedKernel.Results;

namespace OpHalo.Foundation.Core.Entities.Accounts.Errors;

/// <summary>
/// Domain errors for PostAuthContinuation redemption (ADR-497). Missing, expired, consumed, and
/// invalid-selection continuations all resolve to the same generic Invalid error — the
/// /auth/continue enumeration-safety posture never distinguishes why a continuation was
/// rejected.
/// </summary>
public static class PostAuthContinuationErrors
{
    public static readonly Error Invalid =
        Error.Create("PostAuthContinuation.NotFound", "This sign-in cannot be completed. Please sign in again.");

    /// <summary>Retryable — the continuation is still valid, but no workspace was selected.</summary>
    public static readonly Error SelectionRequired =
        Error.Create("PostAuthContinuation.SelectionRequired", "Please select a workspace.");
}

namespace OpHalo.Keep.Application.PriceBook;

public enum ProposedScopeSubmissionResult
{
    Committed,
    NotFound,

    /// <summary>The scope's <c>KeepRequest</c> is <c>Closed</c>/<c>Cancelled</c>/<c>Spam</c>/
    /// <c>Test</c> — a closed or cancelled request must not gain a new submitted-scope review
    /// obligation.</summary>
    RequestTerminal,

    /// <summary>Session 4a: the scope has zero lines. <c>ProposedScope.Submit</c>'s
    /// <c>EmptySubmit</c> domain failure, surfaced distinctly from <see cref="VersionMismatch"/>
    /// rather than folded into it.</summary>
    EmptySubmit,

    /// <summary>The row changed since the caller last read it (EF concurrency-token mismatch), or
    /// — defensively, not expected to actually occur given the version check already gates entry
    /// to the domain transition — the scope was no longer <c>Draft</c> when submission ran.</summary>
    VersionMismatch,
}

/// <summary><see cref="ConcurrencyVersion"/> is set only when <see cref="Result"/> is
/// <see cref="ProposedScopeSubmissionResult.Committed"/>.</summary>
public sealed record ProposedScopeSubmissionOutcome(ProposedScopeSubmissionResult Result, Guid? ConcurrencyVersion = null);

/// <summary>
/// Owns the entire submit-a-proposed-scope transaction as one atomic boundary (Session 3.3a.2):
/// account-scoped terminal-request check, tracked scope load/version/status validation, the pure
/// <c>ProposedScope.Submit</c> domain transition, and the ADR-463
/// <c>KeepRequestWorkSignal</c> raise/reopen — all inside one database transaction, committed or
/// rolled back together. Works directly against <c>OpHaloDbContext</c> rather than composing the
/// ordinary <see cref="IProposedScopePersistence"/> and a separate work-signal persistence seam
/// (matches the <c>EfCatalogItemCreateAndActivatePersistence</c> pattern for an operation that
/// must be atomic across more than one concern). No caller ever sees a transaction object — the
/// Application-layer <c>SubmitProposedScopeService</c> only maps this enum to a <c>Result</c>.
/// </summary>
public interface IProposedScopeSubmissionPersistence
{
    Task<ProposedScopeSubmissionOutcome> SubmitAsync(
        Guid accountId, Guid proposedScopeId, Guid expectedVersion, DateTime submittedAtUtc, CancellationToken ct);
}

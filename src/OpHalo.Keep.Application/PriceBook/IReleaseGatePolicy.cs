namespace OpHalo.Keep.Application.PriceBook;

/// <summary>
/// Server-owned release gate for unreleased Price Book package workflows (ADR-496, BL142 Session 1
/// — renumbered ahead of automatic Pilot package provisioning). Independent of package entitlement
/// (<c>IAccountFeatureAccessResolver</c>): entitlement says an account may use the Price Book
/// package; this says whether a specific workflow within that package has actually shipped.
/// Configuration-derived and synchronous, matching the shape of
/// <c>OpHalo.Foundation.Application.Accounts.Entitlements.IFeatureAccessPolicy</c>. Fail-closed —
/// an absent or unrecognized configuration value blocks the workflow rather than allowing it, so a
/// newly entitled Pilot account never reaches Proposed Work/Quote mutations before a deliberate
/// release decision exists.
/// </summary>
public interface IReleaseGatePolicy
{
    /// <summary>
    /// True only when Proposed Work / Quote capture and submission are explicitly released.
    /// Fail-closed (false) when the underlying configuration is absent or unparseable.
    /// </summary>
    bool IsProposedWorkReleased();
}

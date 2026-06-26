namespace OpHalo.Foundation.Core.Entities.Accounts.Enums;

/// <summary>
/// The account's subscription tier — a cohort/label, not an access gate. Runtime code
/// authorizes against entitlements (commercial state + feature keys), never plan names
/// (ADR-009). The exact tier matrix and pricing are deliberately not locked yet
/// (build plan §4.11).
/// </summary>
/// <remarks>
/// Pilot is intentionally NOT a plan — it is an account classification
/// (<see cref="AccountClassification.Pilot"/>). A pilot customer can sit on any plan
/// while in Trial. Explicit numeric values prevent reorder drift before persistence
/// exists (ADR-026).
/// </remarks>
public enum AccountPlan
{
    /// <summary>Evaluation period — limited, time-bounded access.</summary>
    Trial = 1,

    /// <summary>Entry-level plan.</summary>
    Starter = 2,

    /// <summary>Mid-tier plan.</summary>
    Professional = 3,

    /// <summary>Higher-tier plan.</summary>
    Business = 4,

    /// <summary>Top-tier plan — tailored limits per agreement.</summary>
    Enterprise = 5,

    /// <summary>Internal OpHalo account — bypasses commercial checks via account purpose.</summary>
    Internal = 6
}

using OpHalo.Keep.Core.Entities.Enums;

namespace OpHalo.Keep.Core.Domain;

/// <summary>
/// Pilot aging thresholds and age-bucket computation for post-close feedback review (ADR-279).
/// Thresholds are centralized here so they can move to account preference settings later.
/// </summary>
public static class FeedbackReviewPolicy
{
    // ADR-279: new &lt; 24h, aging 24–72h, overdue &gt; 72h
    public const int NewThresholdHours = 24;
    public const int OverdueThresholdHours = 72;

    public static FeedbackReviewAgeBucket ComputeAgeBucket(DateTime feedbackSubmittedAtUtc, DateTime nowUtc)
    {
        var hours = (nowUtc - feedbackSubmittedAtUtc).TotalHours;
        // ADR-279: new < 24h, aging 24–72h inclusive, overdue > 72h.
        return hours < NewThresholdHours
            ? FeedbackReviewAgeBucket.New
            : hours <= OverdueThresholdHours
                ? FeedbackReviewAgeBucket.Aging
                : FeedbackReviewAgeBucket.Overdue;
    }

    public static DateTime ComputeReviewDueAtUtc(DateTime feedbackSubmittedAtUtc) =>
        feedbackSubmittedAtUtc.AddHours(OverdueThresholdHours);
}

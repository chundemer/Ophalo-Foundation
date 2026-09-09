namespace OpHalo.Foundation.Core.Entities.Feedback.Enums;

/// <summary>
/// Optional self-classification supplied with a feedback submission (GAP-038, BL149). Advisory
/// only — the founder channel is the system of record. When the submitter picks nothing the API
/// stores <see cref="Other"/>.
/// </summary>
public enum FeedbackCategory
{
    Bug = 1,
    Confusing = 2,
    MissingThing = 3,
    TooSlow = 4,
    Other = 5
}

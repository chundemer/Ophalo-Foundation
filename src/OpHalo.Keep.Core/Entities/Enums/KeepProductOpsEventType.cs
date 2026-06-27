namespace OpHalo.Keep.Core.Entities.Enums;

/// <summary>
/// Durable product-ops signal types recorded per account (INT-003 / ADR-375).
/// "First" signals are singletons — one row per account. Manual marks and recurring signals
/// may have multiple rows.
/// </summary>
public enum KeepProductOpsEventType
{
    // Account lifecycle
    AccountCreated = 1,

    // Setup signals — recorded by KeepSetupService
    ProfileAndContactSaved = 2,
    PolicySaved = 3,

    // Request signals — deferred: CreateBusinessRequestService
    FirstRequestCreated = 4,
    FirstStaffCreatedRequest = 5,
    FirstPublicIntakeRequest = 6,
    FirstRequestClosed = 7,

    // Engagement signals — deferred: share/customer-page services
    FirstTrackerLinkShared = 8,
    FirstCustomerPageView = 9,

    // Member / device signals — deferred: Foundation-boundary; derive from DB in V1
    FirstOperatorInvited = 10,
    FirstMobileDeviceRegistered = 11,

    // Recurring / scheduled signals — deferred
    WeeklyInactivity = 12,
    RepeatedNeedsShareBacklog = 13,
    NegativeFeedbackReceived = 14,

    // Manual onboarding marks — recorded via POST /keep/setup/onboarding/marks
    QuickCaptureExerciseDone = 15,
    TrackerReviewDone = 16,
    SpamClassificationExplained = 17
}

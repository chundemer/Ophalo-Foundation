using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Keep.Application.Requests;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;

namespace OpHalo.UnitTests.Keep;

public class KeepRequestActionPolicyTests
{
    static readonly Guid AccountId = Guid.NewGuid();
    static readonly Guid CustomerId = Guid.NewGuid();
    static readonly Guid ActorId = Guid.NewGuid();
    const string ActorName = "Test User";
    static readonly DateTime Now = new(2026, 6, 21, 12, 0, 0, DateTimeKind.Utc);

    // --- Fixture helpers ---

    static KeepRequest MakeReceived() =>
        KeepRequest.CreateFromCustomerIntake(AccountId, CustomerId, "Alice", "555-0001", null,
            "A description", "REF001", "tok_" + Guid.NewGuid().ToString("N"), Now.AddDays(-1), 60);

    static KeepRequest MakeClosed(bool withNegativeFeedback = false)
    {
        var r = MakeReceived();
        r.ChangeStatus(KeepRequestStatus.Resolved, null, ActorId, ActorName, Now.AddHours(-2));
        r.ChangeStatus(KeepRequestStatus.Closed, null, ActorId, ActorName, Now.AddHours(-1));
        if (withNegativeFeedback)
            r.SubmitFeedback(wasResolved: false, comment: "Not happy",
                priorityResponseTargetMinutes: 60, Now.AddMinutes(-30));
        return r;
    }

    static KeepRequest MakeCancelled()
    {
        var r = MakeReceived();
        r.ChangeStatus(KeepRequestStatus.Cancelled, "Cancelled by business", ActorId, ActorName, Now.AddHours(-1));
        return r;
    }

    static KeepRequest WithAttention(KeepRequest r, AttentionLevel level = AttentionLevel.NeedsAttention,
        AttentionReason reason = AttentionReason.CustomerMessage)
    {
        SetProp(r, nameof(KeepRequest.AttentionLevel), level);
        SetProp(r, nameof(KeepRequest.AttentionReason), reason);
        return r;
    }

    static void SetProp(KeepRequest r, string name, object? value) =>
        typeof(KeepRequest).GetProperty(name)!.SetValue(r, value);

    static KeepRequestActionContext OwnerWrite(
        ParticipationType? participation = null, bool? notifEnabled = null) =>
        new(AccountUserRole.Owner, CanWrite: true, participation, notifEnabled);

    static KeepRequestActionContext AdminWrite(
        ParticipationType? participation = null, bool? notifEnabled = null) =>
        new(AccountUserRole.Admin, CanWrite: true, participation, notifEnabled);

    static KeepRequestActionContext OperatorWrite(
        ParticipationType? participation = null, bool? notifEnabled = null) =>
        new(AccountUserRole.Operator, CanWrite: true, participation, notifEnabled);

    // -----------------------------------------------------------------------
    // Fail-closed guards
    // -----------------------------------------------------------------------

    [Fact]
    public void Viewer_returns_DenyAll()
    {
        var d = KeepRequestActionPolicy.Evaluate(MakeReceived(),
            new(AccountUserRole.Viewer, CanWrite: true, null, null));
        Assert.Same(KeepRequestActionPolicy.DenyAll, d);
    }

    [Fact]
    public void Unknown_role_returns_DenyAll()
    {
        var d = KeepRequestActionPolicy.Evaluate(MakeReceived(),
            new((AccountUserRole)999, CanWrite: true, null, null));
        Assert.Same(KeepRequestActionPolicy.DenyAll, d);
    }

    [Fact]
    public void CanWrite_false_returns_DenyAll()
    {
        var d = KeepRequestActionPolicy.Evaluate(MakeReceived(),
            new(AccountUserRole.Owner, CanWrite: false, null, null));
        Assert.Same(KeepRequestActionPolicy.DenyAll, d);
    }

    [Fact]
    public void Participation_without_NotificationsEnabled_returns_DenyAll()
    {
        var ctx = new KeepRequestActionContext(
            AccountUserRole.Owner, CanWrite: true, ParticipationType.Watching, NotificationsEnabled: null);
        Assert.Same(KeepRequestActionPolicy.DenyAll, KeepRequestActionPolicy.Evaluate(MakeReceived(), ctx));
    }

    [Fact]
    public void NotificationsEnabled_without_Participation_returns_DenyAll()
    {
        var ctx = new KeepRequestActionContext(
            AccountUserRole.Owner, CanWrite: true, ActiveParticipation: null, NotificationsEnabled: true);
        Assert.Same(KeepRequestActionPolicy.DenyAll, KeepRequestActionPolicy.Evaluate(MakeReceived(), ctx));
    }

    [Fact]
    public void Unknown_ParticipationType_value_returns_DenyAll()
    {
        var ctx = new KeepRequestActionContext(
            AccountUserRole.Owner, CanWrite: true, (ParticipationType)999, NotificationsEnabled: true);
        Assert.Same(KeepRequestActionPolicy.DenyAll, KeepRequestActionPolicy.Evaluate(MakeReceived(), ctx));
    }

    // -----------------------------------------------------------------------
    // DenyAll singleton properties
    // -----------------------------------------------------------------------

    [Fact]
    public void DenyAll_has_all_capabilities_false_and_empty_statuses()
    {
        var d = KeepRequestActionPolicy.DenyAll;
        Assert.False(d.CanChangeStatus);
        Assert.False(d.CanSendBusinessUpdate);
        Assert.False(d.CanAddInternalNote);
        Assert.False(d.CanAcknowledgeAttention);
        Assert.False(d.CanLogExternalContact);
        Assert.False(d.CanAssignResponsible);
        Assert.False(d.CanSelfAssignResponsible);
        Assert.False(d.CanClearResponsible);
        Assert.False(d.CanManageWatchers);
        Assert.False(d.CanWatch);
        Assert.False(d.CanUnwatch);
        Assert.False(d.CanMute);
        Assert.False(d.CanUnmute);
        Assert.False(d.CanMarkFeedbackReviewed);
        Assert.Empty(d.AllowedStatuses);
    }

    // -----------------------------------------------------------------------
    // Owner/Admin — non-terminal, no participation
    // -----------------------------------------------------------------------

    [Fact]
    public void Owner_nonterminal_noparticipation_has_full_write_capabilities()
    {
        var d = KeepRequestActionPolicy.Evaluate(MakeReceived(), OwnerWrite());

        Assert.True(d.CanChangeStatus);
        Assert.True(d.CanSendBusinessUpdate);
        Assert.True(d.CanAddInternalNote);
        Assert.True(d.CanLogExternalContact);
        Assert.True(d.CanAssignResponsible);
        Assert.True(d.CanClearResponsible);
        Assert.True(d.CanManageWatchers);
        Assert.False(d.CanSelfAssignResponsible);
        Assert.True(d.CanWatch);
        Assert.False(d.CanUnwatch);
        Assert.False(d.CanMute);
        Assert.False(d.CanUnmute);
    }

    [Fact]
    public void Admin_nonterminal_noparticipation_has_same_role_capabilities_as_Owner()
    {
        var d = KeepRequestActionPolicy.Evaluate(MakeReceived(), AdminWrite());

        Assert.True(d.CanAssignResponsible);
        Assert.True(d.CanClearResponsible);
        Assert.True(d.CanManageWatchers);
        Assert.False(d.CanSelfAssignResponsible);
    }

    // -----------------------------------------------------------------------
    // Operator — non-terminal, no participation
    // -----------------------------------------------------------------------

    [Fact]
    public void Operator_nonterminal_noparticipation_has_operational_capabilities()
    {
        var d = KeepRequestActionPolicy.Evaluate(MakeReceived(), OperatorWrite());

        Assert.True(d.CanChangeStatus);
        Assert.True(d.CanSendBusinessUpdate);
        Assert.True(d.CanAddInternalNote);
        Assert.True(d.CanLogExternalContact);
        Assert.True(d.CanSelfAssignResponsible);
        Assert.False(d.CanAssignResponsible);
        Assert.False(d.CanClearResponsible);
        Assert.False(d.CanManageWatchers);
        Assert.True(d.CanWatch);
        Assert.False(d.CanUnwatch);
        Assert.False(d.CanMute);
        Assert.False(d.CanUnmute);
    }

    // -----------------------------------------------------------------------
    // Terminal requests
    // -----------------------------------------------------------------------

    [Fact]
    public void Terminal_Closed_disables_status_update_contact_and_participation_write()
    {
        var d = KeepRequestActionPolicy.Evaluate(MakeClosed(), OwnerWrite());

        Assert.False(d.CanChangeStatus);
        Assert.False(d.CanSendBusinessUpdate);
        Assert.False(d.CanLogExternalContact);
        Assert.False(d.CanAssignResponsible);
        Assert.False(d.CanClearResponsible);
        Assert.False(d.CanManageWatchers);
        Assert.False(d.CanSelfAssignResponsible);
        Assert.False(d.CanWatch);
        Assert.False(d.CanUnwatch);
        Assert.False(d.CanMute);
        Assert.False(d.CanUnmute);
        Assert.Empty(d.AllowedStatuses);
    }

    [Fact]
    public void Terminal_Closed_still_allows_internal_note()
    {
        Assert.True(KeepRequestActionPolicy.Evaluate(MakeClosed(), OwnerWrite()).CanAddInternalNote);
    }

    [Fact]
    public void Terminal_UnresolvedFeedback_attention_disables_acknowledge_attention_G7a()
    {
        // G7a/ADR-300: UnresolvedFeedback must be resolved via MarkFeedbackReviewed, not generic ack.
        var r = MakeClosed(withNegativeFeedback: true);  // SubmitFeedback sets UnresolvedFeedback attention
        var d = KeepRequestActionPolicy.Evaluate(r, OwnerWrite());
        Assert.False(d.CanAcknowledgeAttention);
        Assert.True(d.CanMarkFeedbackReviewed);
    }

    [Fact]
    public void Terminal_non_UnresolvedFeedback_attention_still_allows_acknowledge_attention_ADR111()
    {
        // ADR-111: terminal attention cleanup for other reasons remains available.
        var r = MakeClosed();
        WithAttention(r, AttentionLevel.NeedsAttention, AttentionReason.CustomerMessage);
        Assert.True(KeepRequestActionPolicy.Evaluate(r, OwnerWrite()).CanAcknowledgeAttention);
    }

    [Fact]
    public void Terminal_without_attention_disables_acknowledge_attention()
    {
        Assert.False(KeepRequestActionPolicy.Evaluate(MakeClosed(), OwnerWrite()).CanAcknowledgeAttention);
    }

    [Fact]
    public void Terminal_Cancelled_also_disables_write_capabilities()
    {
        var d = KeepRequestActionPolicy.Evaluate(MakeCancelled(), OwnerWrite());
        Assert.False(d.CanChangeStatus);
        Assert.Empty(d.AllowedStatuses);
    }

    // -----------------------------------------------------------------------
    // Attention state (non-terminal)
    // -----------------------------------------------------------------------

    [Fact]
    public void Attention_present_on_nonterminal_enables_acknowledge_attention()
    {
        var r = WithAttention(MakeReceived());
        Assert.True(KeepRequestActionPolicy.Evaluate(r, OwnerWrite()).CanAcknowledgeAttention);
    }

    [Fact]
    public void No_attention_on_nonterminal_disables_acknowledge_attention()
    {
        // CreateFromCustomerIntake starts with AttentionLevel.None.
        Assert.False(KeepRequestActionPolicy.Evaluate(MakeReceived(), OwnerWrite()).CanAcknowledgeAttention);
    }

    // -----------------------------------------------------------------------
    // Watch / Unwatch / Mute / Unmute
    // -----------------------------------------------------------------------

    [Fact]
    public void Watching_notifications_enabled_can_mute_and_unwatch_but_not_watch_or_unmute()
    {
        var d = KeepRequestActionPolicy.Evaluate(
            MakeReceived(), OwnerWrite(ParticipationType.Watching, notifEnabled: true));
        Assert.True(d.CanMute);
        Assert.False(d.CanUnmute);
        Assert.True(d.CanUnwatch);
        Assert.False(d.CanWatch);
    }

    [Fact]
    public void Watching_notifications_disabled_can_unmute_and_unwatch_but_not_mute_or_watch()
    {
        var d = KeepRequestActionPolicy.Evaluate(
            MakeReceived(), OwnerWrite(ParticipationType.Watching, notifEnabled: false));
        Assert.False(d.CanMute);
        Assert.True(d.CanUnmute);
        Assert.True(d.CanUnwatch);
        Assert.False(d.CanWatch);
    }

    [Fact]
    public void Responsible_notifications_enabled_can_mute_but_not_unwatch_or_watch()
    {
        var d = KeepRequestActionPolicy.Evaluate(
            MakeReceived(), OwnerWrite(ParticipationType.Responsible, notifEnabled: true));
        Assert.True(d.CanMute);
        Assert.False(d.CanUnmute);
        Assert.False(d.CanUnwatch);
        Assert.False(d.CanWatch);
    }

    [Fact]
    public void Responsible_notifications_disabled_can_unmute_but_not_unwatch()
    {
        var d = KeepRequestActionPolicy.Evaluate(
            MakeReceived(), OwnerWrite(ParticipationType.Responsible, notifEnabled: false));
        Assert.False(d.CanMute);
        Assert.True(d.CanUnmute);
        Assert.False(d.CanUnwatch);
    }

    [Fact]
    public void Terminal_participation_disables_all_notification_and_watch_actions()
    {
        var d = KeepRequestActionPolicy.Evaluate(
            MakeClosed(), OwnerWrite(ParticipationType.Watching, notifEnabled: true));
        Assert.False(d.CanMute);
        Assert.False(d.CanUnmute);
        Assert.False(d.CanUnwatch);
        Assert.False(d.CanWatch);
    }

    [Fact]
    public void Operator_Watching_notifications_enabled_can_mute_and_unwatch()
    {
        var d = KeepRequestActionPolicy.Evaluate(
            MakeReceived(), OperatorWrite(ParticipationType.Watching, notifEnabled: true));
        Assert.True(d.CanMute);
        Assert.True(d.CanUnwatch);
        Assert.False(d.CanWatch);
    }

    // -----------------------------------------------------------------------
    // CanMarkFeedbackReviewed
    // -----------------------------------------------------------------------

    [Fact]
    public void MarkFeedbackReviewed_eligible_Owner_all_conditions_met()
    {
        Assert.True(
            KeepRequestActionPolicy.Evaluate(MakeClosed(withNegativeFeedback: true), OwnerWrite())
                .CanMarkFeedbackReviewed);
    }

    [Fact]
    public void MarkFeedbackReviewed_eligible_Admin()
    {
        Assert.True(
            KeepRequestActionPolicy.Evaluate(MakeClosed(withNegativeFeedback: true), AdminWrite())
                .CanMarkFeedbackReviewed);
    }

    [Fact]
    public void MarkFeedbackReviewed_not_eligible_for_Operator()
    {
        Assert.False(
            KeepRequestActionPolicy.Evaluate(MakeClosed(withNegativeFeedback: true), OperatorWrite())
                .CanMarkFeedbackReviewed);
    }

    [Fact]
    public void MarkFeedbackReviewed_not_eligible_when_no_feedback_submitted()
    {
        Assert.False(
            KeepRequestActionPolicy.Evaluate(MakeClosed(), OwnerWrite()).CanMarkFeedbackReviewed);
    }

    [Fact]
    public void MarkFeedbackReviewed_not_eligible_when_FeedbackWasResolved_true()
    {
        var r = MakeClosed();
        r.SubmitFeedback(wasResolved: true, comment: null, priorityResponseTargetMinutes: 60, Now.AddMinutes(-30));
        Assert.False(KeepRequestActionPolicy.Evaluate(r, OwnerWrite()).CanMarkFeedbackReviewed);
    }

    [Fact]
    public void MarkFeedbackReviewed_not_eligible_when_already_reviewed()
    {
        var r = MakeClosed(withNegativeFeedback: true);
        r.MarkFeedbackReviewed(note: null, ActorId, ActorName, Now);
        Assert.False(KeepRequestActionPolicy.Evaluate(r, OwnerWrite()).CanMarkFeedbackReviewed);
    }

    [Fact]
    public void MarkFeedbackReviewed_not_eligible_when_attention_cleared_after_feedback()
    {
        // Simulates AcknowledgeAttention having cleared UnresolvedFeedback before review (ADR-273).
        var r = MakeClosed(withNegativeFeedback: true);
        SetProp(r, nameof(KeepRequest.AttentionLevel), AttentionLevel.None);
        SetProp(r, nameof(KeepRequest.AttentionReason), null);
        Assert.False(KeepRequestActionPolicy.Evaluate(r, OwnerWrite()).CanMarkFeedbackReviewed);
    }

    [Fact]
    public void MarkFeedbackReviewed_not_eligible_on_non_Closed_status()
    {
        var r = MakeReceived();
        // Force feedback fields onto a non-Closed request to confirm policy checks status.
        SetProp(r, nameof(KeepRequest.FeedbackSubmittedAtUtc), Now.AddHours(-1));
        SetProp(r, nameof(KeepRequest.FeedbackWasResolved), false);
        SetProp(r, nameof(KeepRequest.AttentionLevel), AttentionLevel.Waiting);
        SetProp(r, nameof(KeepRequest.AttentionReason), AttentionReason.UnresolvedFeedback);
        Assert.False(KeepRequestActionPolicy.Evaluate(r, OwnerWrite()).CanMarkFeedbackReviewed);
    }

    // -----------------------------------------------------------------------
    // AllowedStatuses
    // -----------------------------------------------------------------------

    [Fact]
    public void AllowedStatuses_Received()
    {
        var d = KeepRequestActionPolicy.Evaluate(MakeReceived(), OwnerWrite());
        Assert.Equal(
            [KeepRequestStatus.Scheduled, KeepRequestStatus.InProgress,
             KeepRequestStatus.PendingCustomer, KeepRequestStatus.Resolved,
             KeepRequestStatus.Cancelled],
            d.AllowedStatuses);
    }

    [Fact]
    public void AllowedStatuses_Scheduled()
    {
        var r = MakeReceived();
        r.ChangeStatus(KeepRequestStatus.Scheduled, null, ActorId, ActorName, Now.AddHours(-1));
        var d = KeepRequestActionPolicy.Evaluate(r, OwnerWrite());
        Assert.Equal(
            [KeepRequestStatus.InProgress, KeepRequestStatus.PendingCustomer,
             KeepRequestStatus.Resolved, KeepRequestStatus.Cancelled],
            d.AllowedStatuses);
    }

    [Fact]
    public void AllowedStatuses_InProgress()
    {
        var r = MakeReceived();
        r.ChangeStatus(KeepRequestStatus.InProgress, null, ActorId, ActorName, Now.AddHours(-1));
        var d = KeepRequestActionPolicy.Evaluate(r, OwnerWrite());
        Assert.Equal(
            [KeepRequestStatus.Scheduled, KeepRequestStatus.PendingCustomer,
             KeepRequestStatus.Resolved, KeepRequestStatus.Cancelled],
            d.AllowedStatuses);
    }

    [Fact]
    public void AllowedStatuses_PendingCustomer()
    {
        var r = MakeReceived();
        r.ChangeStatus(KeepRequestStatus.PendingCustomer, "Waiting on you", ActorId, ActorName, Now.AddHours(-1));
        var d = KeepRequestActionPolicy.Evaluate(r, OwnerWrite());
        Assert.Equal(
            [KeepRequestStatus.Scheduled, KeepRequestStatus.InProgress,
             KeepRequestStatus.Resolved, KeepRequestStatus.Cancelled],
            d.AllowedStatuses);
    }

    [Fact]
    public void AllowedStatuses_Resolved()
    {
        var r = MakeReceived();
        r.ChangeStatus(KeepRequestStatus.Resolved, null, ActorId, ActorName, Now.AddHours(-1));
        var d = KeepRequestActionPolicy.Evaluate(r, OwnerWrite());
        Assert.Equal(
            [KeepRequestStatus.InProgress, KeepRequestStatus.PendingCustomer,
             KeepRequestStatus.Closed, KeepRequestStatus.Cancelled],
            d.AllowedStatuses);
    }

    [Fact]
    public void AllowedStatuses_Closed_is_empty()
    {
        Assert.Empty(KeepRequestActionPolicy.Evaluate(MakeClosed(), OwnerWrite()).AllowedStatuses);
    }

    [Fact]
    public void AllowedStatuses_Cancelled_is_empty()
    {
        Assert.Empty(KeepRequestActionPolicy.Evaluate(MakeCancelled(), OwnerWrite()).AllowedStatuses);
    }

    [Fact]
    public void AllowedStatuses_excludes_current_status()
    {
        var d = KeepRequestActionPolicy.Evaluate(MakeReceived(), OwnerWrite());
        Assert.DoesNotContain(KeepRequestStatus.Received, d.AllowedStatuses);
    }

    // -----------------------------------------------------------------------
    // G7b — CanLogExternalContact for closed unresolved-feedback review state
    // -----------------------------------------------------------------------

    [Fact]
    public void G7b_Owner_exact_active_review_can_log_external_contact()
    {
        var r = MakeClosed(withNegativeFeedback: true);
        Assert.True(KeepRequestActionPolicy.Evaluate(r, OwnerWrite()).CanLogExternalContact);
    }

    [Fact]
    public void G7b_Admin_exact_active_review_can_log_external_contact()
    {
        var r = MakeClosed(withNegativeFeedback: true);
        Assert.True(KeepRequestActionPolicy.Evaluate(r, AdminWrite()).CanLogExternalContact);
    }

    [Fact]
    public void G7b_Operator_exact_active_review_cannot_log_external_contact()
    {
        var r = MakeClosed(withNegativeFeedback: true);
        Assert.False(KeepRequestActionPolicy.Evaluate(r, OperatorWrite()).CanLogExternalContact);
    }

    [Fact]
    public void G7b_ordinary_closed_no_feedback_cannot_log_external_contact()
    {
        Assert.False(KeepRequestActionPolicy.Evaluate(MakeClosed(), OwnerWrite()).CanLogExternalContact);
    }

    [Fact]
    public void G7b_closed_positive_feedback_cannot_log_external_contact()
    {
        var r = MakeReceived();
        r.ChangeStatus(KeepRequestStatus.Resolved, null, ActorId, ActorName, Now.AddHours(-2));
        r.ChangeStatus(KeepRequestStatus.Closed, null, ActorId, ActorName, Now.AddHours(-1));
        r.SubmitFeedback(wasResolved: true, comment: "Great", priorityResponseTargetMinutes: 60, Now.AddMinutes(-30));
        Assert.False(KeepRequestActionPolicy.Evaluate(r, OwnerWrite()).CanLogExternalContact);
    }

    [Fact]
    public void G7b_closed_feedback_already_reviewed_cannot_log_external_contact()
    {
        var r = MakeClosed(withNegativeFeedback: true);
        r.MarkFeedbackReviewed(null, ActorId, ActorName, Now.AddMinutes(-10));
        Assert.False(KeepRequestActionPolicy.Evaluate(r, OwnerWrite()).CanLogExternalContact);
    }

    [Fact]
    public void G7b_cancelled_cannot_log_external_contact()
    {
        Assert.False(KeepRequestActionPolicy.Evaluate(MakeCancelled(), OwnerWrite()).CanLogExternalContact);
    }

    [Fact]
    public void G7b_OffSeason_exact_active_review_cannot_log_external_contact()
    {
        var r = MakeClosed(withNegativeFeedback: true);
        var offSeason = new KeepRequestActionContext(AccountUserRole.Owner, CanWrite: false, null, null);
        Assert.False(KeepRequestActionPolicy.Evaluate(r, offSeason).CanLogExternalContact);
    }
}

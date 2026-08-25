using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Keep.Application.Requests;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;
using OpHalo.Keep.Core.Errors;

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

    static KeepRequest MakeSpam()
    {
        var r = MakeReceived();
        SetProp(r, nameof(KeepRequest.Status), KeepRequestStatus.Spam);
        SetProp(r, nameof(KeepRequest.TerminatedAtUtc), Now.AddHours(-1));
        return r;
    }

    static KeepRequest MakeTest()
    {
        var r = MakeReceived();
        SetProp(r, nameof(KeepRequest.Status), KeepRequestStatus.Test);
        SetProp(r, nameof(KeepRequest.TerminatedAtUtc), Now.AddHours(-1));
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
        Assert.False(d.CanClose);
        Assert.False(d.CanClassify);
        Assert.False(d.CanRecordShareIntent);
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
        Assert.True(d.CanRecordShareIntent);
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
        Assert.True(d.CanRecordShareIntent);
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

    // CanClose (ADR-343)

    [Fact]
    public void CanClose_Owner_Resolved_no_attention_is_true()
    {
        var r = MakeReceived();
        r.ChangeStatus(KeepRequestStatus.Resolved, null, ActorId, ActorName, Now.AddHours(-1));
        Assert.True(KeepRequestActionPolicy.Evaluate(r, OwnerWrite()).CanClose);
    }

    [Fact]
    public void CanClose_Admin_Resolved_no_attention_is_true()
    {
        var r = MakeReceived();
        r.ChangeStatus(KeepRequestStatus.Resolved, null, ActorId, ActorName, Now.AddHours(-1));
        Assert.True(KeepRequestActionPolicy.Evaluate(r, AdminWrite()).CanClose);
    }

    [Fact]
    public void CanClose_Operator_Resolved_no_attention_is_false()
    {
        var r = MakeReceived();
        r.ChangeStatus(KeepRequestStatus.Resolved, null, ActorId, ActorName, Now.AddHours(-1));
        Assert.False(KeepRequestActionPolicy.Evaluate(r, OperatorWrite()).CanClose);
    }

    [Fact]
    public void CanClose_Owner_Resolved_with_attention_is_false()
    {
        var r = MakeReceived();
        r.ChangeStatus(KeepRequestStatus.Resolved, null, ActorId, ActorName, Now.AddHours(-1));
        WithAttention(r);
        Assert.False(KeepRequestActionPolicy.Evaluate(r, OwnerWrite()).CanClose);
    }

    [Fact]
    public void CanClose_Owner_active_request_is_false()
    {
        Assert.False(KeepRequestActionPolicy.Evaluate(MakeReceived(), OwnerWrite()).CanClose);
    }

    [Fact]
    public void CanClose_Owner_already_closed_is_false()
    {
        Assert.False(KeepRequestActionPolicy.Evaluate(MakeClosed(), OwnerWrite()).CanClose);
    }

    [Fact]
    public void AllowedStatuses_Resolved_Operator_excludes_Closed()
    {
        var r = MakeReceived();
        r.ChangeStatus(KeepRequestStatus.Resolved, null, ActorId, ActorName, Now.AddHours(-1));
        var statuses = KeepRequestActionPolicy.Evaluate(r, OperatorWrite()).AllowedStatuses;
        Assert.DoesNotContain(KeepRequestStatus.Closed, statuses);
        Assert.Equal(
            [KeepRequestStatus.InProgress, KeepRequestStatus.PendingCustomer,
             KeepRequestStatus.Cancelled],
            statuses);
    }

    [Fact]
    public void AllowedStatuses_Resolved_OwnerAdmin_with_attention_excludes_Closed()
    {
        var r = MakeReceived();
        r.ChangeStatus(KeepRequestStatus.Resolved, null, ActorId, ActorName, Now.AddHours(-1));
        WithAttention(r);
        var statuses = KeepRequestActionPolicy.Evaluate(r, OwnerWrite()).AllowedStatuses;
        Assert.DoesNotContain(KeepRequestStatus.Closed, statuses);
        Assert.Equal(
            [KeepRequestStatus.InProgress, KeepRequestStatus.PendingCustomer,
             KeepRequestStatus.Cancelled],
            statuses);
    }

    [Theory]
    [InlineData(KeepRequestStatus.Spam)]
    [InlineData(KeepRequestStatus.Test)]
    public void Terminal_Spam_and_Test_disable_write_capabilities_and_have_no_allowed_statuses(
        KeepRequestStatus status)
    {
        var r = status == KeepRequestStatus.Spam ? MakeSpam() : MakeTest();
        var d = KeepRequestActionPolicy.Evaluate(r, OwnerWrite());

        Assert.False(d.CanChangeStatus);
        Assert.False(d.CanSendBusinessUpdate);
        Assert.False(d.CanLogExternalContact);
        Assert.False(d.CanAssignResponsible);
        Assert.False(d.CanClearResponsible);
        Assert.False(d.CanManageWatchers);
        Assert.False(d.CanSelfAssignResponsible);
        Assert.False(d.CanWatch);
        Assert.False(d.CanUnwatch);
        Assert.False(d.CanSetFollowUpOn);
        Assert.False(d.CanSetPlannedFor);
        Assert.False(d.CanClose);
        Assert.False(d.CanClassify);
        Assert.Empty(d.AllowedStatuses);
    }

    [Theory]
    [InlineData(KeepRequestStatus.Spam)]
    [InlineData(KeepRequestStatus.Test)]
    public void Terminal_Spam_and_Test_still_allow_internal_note(KeepRequestStatus status)
    {
        var r = status == KeepRequestStatus.Spam ? MakeSpam() : MakeTest();
        Assert.True(KeepRequestActionPolicy.Evaluate(r, OwnerWrite()).CanAddInternalNote);
    }

    // -----------------------------------------------------------------------
    // CanClassify (ADR-349)
    // -----------------------------------------------------------------------

    [Fact]
    public void CanClassify_Owner_nonterminal_is_true()
    {
        Assert.True(KeepRequestActionPolicy.Evaluate(MakeReceived(), OwnerWrite()).CanClassify);
    }

    [Fact]
    public void CanClassify_Admin_nonterminal_is_true()
    {
        Assert.True(KeepRequestActionPolicy.Evaluate(MakeReceived(), AdminWrite()).CanClassify);
    }

    [Fact]
    public void CanClassify_Operator_nonterminal_is_false()
    {
        Assert.False(KeepRequestActionPolicy.Evaluate(MakeReceived(), OperatorWrite()).CanClassify);
    }

    [Theory]
    [InlineData(KeepRequestStatus.Spam)]
    [InlineData(KeepRequestStatus.Test)]
    [InlineData(KeepRequestStatus.Closed)]
    [InlineData(KeepRequestStatus.Cancelled)]
    public void CanClassify_Owner_terminal_is_false(KeepRequestStatus status)
    {
        var r = status switch
        {
            KeepRequestStatus.Spam => MakeSpam(),
            KeepRequestStatus.Test => MakeTest(),
            KeepRequestStatus.Closed => MakeClosed(),
            _ => MakeCancelled()
        };
        Assert.False(KeepRequestActionPolicy.Evaluate(r, OwnerWrite()).CanClassify);
    }

    // CanCreateFollowUpRequest

    [Fact]
    public void CreateFollowUpRequest_Owner_on_Closed_is_true()
        => Assert.True(KeepRequestActionPolicy.Evaluate(MakeClosed(), OwnerWrite()).CanCreateFollowUpRequest);

    [Fact]
    public void CreateFollowUpRequest_Admin_on_Closed_is_true()
        => Assert.True(KeepRequestActionPolicy.Evaluate(MakeClosed(), AdminWrite()).CanCreateFollowUpRequest);

    [Fact]
    public void CreateFollowUpRequest_Operator_on_Closed_is_false()
        => Assert.False(KeepRequestActionPolicy.Evaluate(MakeClosed(), OperatorWrite()).CanCreateFollowUpRequest);

    [Fact]
    public void CreateFollowUpRequest_Owner_on_non_Closed_active_is_false()
        => Assert.False(KeepRequestActionPolicy.Evaluate(MakeReceived(), OwnerWrite()).CanCreateFollowUpRequest);

    [Fact]
    public void CreateFollowUpRequest_Owner_on_Cancelled_is_false()
        => Assert.False(KeepRequestActionPolicy.Evaluate(MakeCancelled(), OwnerWrite()).CanCreateFollowUpRequest);

    // -----------------------------------------------------------------------
    // CanResolveFollowUp (Session 0A)
    // -----------------------------------------------------------------------

    [Fact]
    public void CanResolveFollowUp_Owner_active_with_followup_set_is_true()
    {
        var r = MakeReceived();
        r.SetFollowUpOn(DateOnly.FromDateTime(Now.AddDays(3)), null, null, ActorId, ActorName, Now);
        Assert.True(KeepRequestActionPolicy.Evaluate(r, OwnerWrite()).CanResolveFollowUp);
    }

    [Fact]
    public void CanResolveFollowUp_Operator_active_with_followup_set_is_true()
    {
        var r = MakeReceived();
        r.SetFollowUpOn(DateOnly.FromDateTime(Now.AddDays(3)), null, null, ActorId, ActorName, Now);
        Assert.True(KeepRequestActionPolicy.Evaluate(r, OperatorWrite()).CanResolveFollowUp);
    }

    [Fact]
    public void CanResolveFollowUp_Owner_active_without_followup_is_false()
        => Assert.False(KeepRequestActionPolicy.Evaluate(MakeReceived(), OwnerWrite()).CanResolveFollowUp);

    [Fact]
    public void CanResolveFollowUp_Owner_Resolved_with_followup_set_is_false()
    {
        var r = MakeReceived();
        r.SetFollowUpOn(DateOnly.FromDateTime(Now.AddDays(3)), null, null, ActorId, ActorName, Now);
        r.ChangeStatus(KeepRequestStatus.Resolved, null, ActorId, ActorName, Now.AddHours(-1));
        Assert.False(KeepRequestActionPolicy.Evaluate(r, OwnerWrite()).CanResolveFollowUp);
    }

    [Fact]
    public void CanResolveFollowUp_Owner_Closed_with_followup_set_is_false()
        => Assert.False(KeepRequestActionPolicy.Evaluate(MakeClosed(), OwnerWrite()).CanResolveFollowUp);

    // Equivalence: CanResolveFollowUp must track KeepRequest.ResolveFollowUp's own structural
    // gate exactly, so the detail contract never advertises an action the mutation rejects.
    [Theory]
    [InlineData(/* active */ true,  /* followUpSet */ true)]
    [InlineData(/* active */ true,  /* followUpSet */ false)]
    [InlineData(/* active */ false, /* followUpSet */ true)]
    [InlineData(/* active */ false, /* followUpSet */ false)]
    public void CanResolveFollowUp_matches_ResolveFollowUp_structural_gate(bool active, bool followUpSet)
    {
        var r = MakeReceived();
        if (followUpSet)
            r.SetFollowUpOn(DateOnly.FromDateTime(Now.AddDays(3)), null, null, ActorId, ActorName, Now);
        if (!active)
            r.ChangeStatus(KeepRequestStatus.Resolved, null, ActorId, ActorName, Now.AddHours(-1));

        var canResolve = KeepRequestActionPolicy.Evaluate(r, OwnerWrite()).CanResolveFollowUp;

        var mutationResult = r.ResolveFollowUp(
            FollowUpResolutionOutcome.Complete, FollowUpCompletionReason.WorkCompleted, null,
            null, null, ActorId, ActorName, Now.AddHours(1));

        var mutationRejectsStructurally = mutationResult.IsFailure
            && (mutationResult.Error == KeepRequestErrors.FollowUpOnRequiresActiveRequest
                || mutationResult.Error == KeepRequestErrors.FollowUpOnNotSet);

        Assert.Equal(canResolve, !mutationRejectsStructurally);
    }

    // -----------------------------------------------------------------------
    // SelectPrimaryAction (Session 0A)
    // -----------------------------------------------------------------------

    static readonly EffectiveAttentionResult NoAttention = new("none", null, null, null, null);

    static EffectiveAttentionResult AttentionWithGuidance(string guidanceKey) =>
        new("needs_attention", "customer_message", null, null, guidanceKey);

    // Selectors read AvailableActionsMetadata (the server's own already-mapped projection of the
    // decision), not the raw KeepRequestActionDecision — this fixture mirrors what
    // KeepRequestDetailMapper.ToDetailResult actually passes them.
    static AvailableActionsMetadata Actions(KeepRequest r, KeepRequestActionContext ctx) =>
        KeepRequestDetailMapper.ToAvailableActionsMetadata(KeepRequestActionPolicy.Evaluate(r, ctx));

    [Fact]
    public void SelectPrimaryAction_no_attention_Resolved_CanClose_returns_close_request()
    {
        var r = MakeReceived();
        r.ChangeStatus(KeepRequestStatus.Resolved, null, ActorId, ActorName, Now.AddHours(-1));
        var actions = Actions(r, OwnerWrite());

        var primary = KeepRequestActionPolicy.SelectPrimaryAction(actions, NoAttention, r.Status);

        Assert.NotNull(primary);
        Assert.Equal("close_request", primary!.Key);
        Assert.Equal("mutation", primary.Target);
        Assert.True(primary.RequiresConfirmation);
    }

    [Fact]
    public void SelectPrimaryAction_no_attention_eligible_nonresolved_returns_mark_work_done()
    {
        var r = MakeReceived();
        var actions = Actions(r, OwnerWrite());

        var primary = KeepRequestActionPolicy.SelectPrimaryAction(actions, NoAttention, r.Status);

        Assert.NotNull(primary);
        Assert.Equal("mark_work_done", primary!.Key);
        Assert.Equal("mutation", primary.Target);
        Assert.False(primary.RequiresConfirmation);
    }

    [Fact]
    public void SelectPrimaryAction_attention_with_actionable_guidance_outranks_mark_work_done()
    {
        var r = MakeReceived();
        var actions = Actions(r, OwnerWrite());
        var attention = AttentionWithGuidance("respond_to_customer");

        var primary = KeepRequestActionPolicy.SelectPrimaryAction(actions, attention, r.Status);

        Assert.NotNull(primary);
        Assert.Equal("respond_to_customer", primary!.Key);
        Assert.Equal("customer_update_composer", primary.Target);
    }

    [Fact]
    public void SelectPrimaryAction_attention_guidance_gate_unauthorized_returns_null()
    {
        // Simulate an actor for whom the guided capability is false (e.g. CanResolveFollowUp
        // false because no follow-up is actually set), and assert no fallback to work completion.
        var r = MakeReceived();
        var actions = Actions(r, OwnerWrite());
        var attention = AttentionWithGuidance("resolve_follow_up");

        var primary = KeepRequestActionPolicy.SelectPrimaryAction(actions, attention, r.Status);

        Assert.Null(primary);
    }

    [Fact]
    public void SelectPrimaryAction_attention_with_no_guidance_key_returns_null_not_work_done()
    {
        var r = MakeReceived();
        var actions = Actions(r, OwnerWrite());
        var attention = new EffectiveAttentionResult("needs_attention", "customer_message", null, null, null);

        var primary = KeepRequestActionPolicy.SelectPrimaryAction(actions, attention, r.Status);

        Assert.Null(primary);
    }

    [Fact]
    public void SelectPrimaryAction_terminal_status_returns_null()
        => Assert.Null(KeepRequestActionPolicy.SelectPrimaryAction(
            Actions(MakeClosed(), OwnerWrite()), NoAttention, KeepRequestStatus.Closed));

    [Theory]
    [MemberData(nameof(AllPrimaryActionCases))]
    public void SelectPrimaryAction_RequiresConfirmation_implies_nonempty_ConfirmationCopy(
        AvailableActionsMetadata actions, EffectiveAttentionResult attention, KeepRequestStatus status)
    {
        var primary = KeepRequestActionPolicy.SelectPrimaryAction(actions, attention, status);
        if (primary is null) return;

        if (primary.RequiresConfirmation)
            Assert.False(string.IsNullOrEmpty(primary.ConfirmationCopy));
        else
            Assert.Null(primary.ConfirmationCopy);
    }

    public static IEnumerable<object[]> AllPrimaryActionCases()
    {
        var receivedOwner = Actions(MakeReceived(), OwnerWrite());
        yield return [receivedOwner, NoAttention, KeepRequestStatus.Received];
        yield return [receivedOwner, AttentionWithGuidance("respond_to_customer"), KeepRequestStatus.Received];
        yield return [receivedOwner, AttentionWithGuidance("log_external_contact"), KeepRequestStatus.Received];
        yield return [receivedOwner, AttentionWithGuidance("acknowledge_attention"), KeepRequestStatus.Received];
        yield return [receivedOwner, AttentionWithGuidance("resolve_follow_up"), KeepRequestStatus.Received];

        var resolved = MakeReceived();
        resolved.ChangeStatus(KeepRequestStatus.Resolved, null, ActorId, ActorName, Now.AddHours(-1));
        var resolvedOwner = Actions(resolved, OwnerWrite());
        yield return [resolvedOwner, NoAttention, KeepRequestStatus.Resolved];
    }

    // -----------------------------------------------------------------------
    // SelectMarkWorkDoneSecondary (Session 0A)
    // -----------------------------------------------------------------------

    [Fact]
    public void SelectMarkWorkDoneSecondary_null_when_no_attention_because_it_is_primary_instead()
    {
        var r = MakeReceived();
        var actions = Actions(r, OwnerWrite());

        Assert.Null(KeepRequestActionPolicy.SelectMarkWorkDoneSecondary(actions, NoAttention, r.Status));
    }

    [Fact]
    public void SelectMarkWorkDoneSecondary_populated_when_eligible_and_attention_active()
    {
        var r = MakeReceived();
        var actions = Actions(r, OwnerWrite());
        var attention = AttentionWithGuidance("respond_to_customer");

        var secondary = KeepRequestActionPolicy.SelectMarkWorkDoneSecondary(actions, attention, r.Status);

        Assert.NotNull(secondary);
        Assert.Equal("mutation", secondary!.Target);
        Assert.Equal("attention_remains", secondary.Consequence);

        // Never coexists with mark_work_done as PrimaryAction.
        var primary = KeepRequestActionPolicy.SelectPrimaryAction(actions, attention, r.Status);
        Assert.NotEqual("mark_work_done", primary?.Key);
    }

    [Fact]
    public void SelectMarkWorkDoneSecondary_null_when_not_eligible_even_with_attention()
    {
        var r = MakeClosed();
        var actions = Actions(r, OwnerWrite());
        var attention = AttentionWithGuidance("respond_to_customer");

        Assert.Null(KeepRequestActionPolicy.SelectMarkWorkDoneSecondary(actions, attention, r.Status));
    }
}

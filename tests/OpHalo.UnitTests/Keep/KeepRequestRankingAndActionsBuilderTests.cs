using OpHalo.Keep.Application.Requests;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;

namespace OpHalo.UnitTests.Keep;

/// <summary>
/// Maintainability review item 4: direct coverage for the extracted ranking/severity/quick-action
/// collaborator, on top of the pre-existing indirect coverage in
/// <see cref="KeepRequestListServiceTests"/> (which still exercises this logic through
/// <c>GetKeepRequestListService.ExecuteAsync</c> unchanged).
/// </summary>
public class KeepRequestRankingAndActionsBuilderTests
{
    private static readonly Guid AccountId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 6, 15, 10, 0, 0, DateTimeKind.Utc);

    private static KeepRequest MakeRequest(
        string phone = "555-0001",
        string? email = null) =>
        KeepRequest.CreateFromCustomerIntake(
            AccountId, Guid.NewGuid(), "Bob", phone, email, "Desc",
            "REF", "tok_" + Guid.NewGuid().ToString("N"), Now, 60);

    private static void SetProp(KeepRequest r, string name, object? value) =>
        typeof(KeepRequest).GetProperty(name)!.SetValue(r, value);

    private static readonly KeepRequestActionDecision AllowAll = new(
        CanChangeStatus: true, CanSendBusinessUpdate: true, CanAddInternalNote: true,
        CanAcknowledgeAttention: true, CanLogExternalContact: true, CanAssignResponsible: true,
        CanSelfAssignResponsible: true, CanClearResponsible: true, CanManageWatchers: true,
        CanWatch: true, CanUnwatch: true, CanMute: true, CanUnmute: true,
        CanMarkFeedbackReviewed: true, CanSetFollowUpOn: true, CanSetPlannedFor: true,
        CanResolveFollowUp: true, CanClose: true, CanClassify: true,
        CanRecordShareIntent: true, CanCreateFollowUpRequest: true,
        AllowedStatuses: []);

    private static readonly KeepRequestActionDecision DenyAll = new(
        CanChangeStatus: false, CanSendBusinessUpdate: false, CanAddInternalNote: false,
        CanAcknowledgeAttention: false, CanLogExternalContact: false, CanAssignResponsible: false,
        CanSelfAssignResponsible: false, CanClearResponsible: false, CanManageWatchers: false,
        CanWatch: false, CanUnwatch: false, CanMute: false, CanUnmute: false,
        CanMarkFeedbackReviewed: false, CanSetFollowUpOn: false, CanSetPlannedFor: false,
        CanResolveFollowUp: false, CanClose: false, CanClassify: false,
        CanRecordShareIntent: false, CanCreateFollowUpRequest: false,
        AllowedStatuses: []);

    // --- ComputeRankingGroup ------------------------------------------------------

    [Fact]
    public void ComputeRankingGroup_overdue_business_waiting_wins_over_everything_else()
    {
        var r = MakeRequest();
        SetProp(r, nameof(KeepRequest.PriorityBand), PriorityBand.Priority);
        SetProp(r, nameof(KeepRequest.WaitingDirection), WaitingDirection.Business);

        var (group, order) = KeepRequestRankingAndActionsBuilder.ComputeRankingGroup(
            r, isPostClose: true, firstResponsePending: true, firstResponseOverdue: true,
            overdueBusinessWaiting: true, isDueOrOverdueFollowUpOn: true);

        Assert.Equal("overdue_business_waiting", group);
        Assert.Equal(1, order);
    }

    [Fact]
    public void ComputeRankingGroup_post_close_beats_priority_and_urgent()
    {
        var r = MakeRequest();
        SetProp(r, nameof(KeepRequest.PriorityBand), PriorityBand.Priority);
        SetProp(r, nameof(KeepRequest.WaitingDirection), WaitingDirection.Business);
        SetProp(r, nameof(KeepRequest.BusinessPriority), BusinessPriority.Urgent);

        var (group, order) = KeepRequestRankingAndActionsBuilder.ComputeRankingGroup(
            r, isPostClose: true, firstResponsePending: false, firstResponseOverdue: false,
            overdueBusinessWaiting: false, isDueOrOverdueFollowUpOn: false);

        Assert.Equal("post_close_unresolved_feedback", group);
        Assert.Equal(4, order);
    }

    [Fact]
    public void ComputeRankingGroup_priority_business_waiting()
    {
        var r = MakeRequest();
        SetProp(r, nameof(KeepRequest.PriorityBand), PriorityBand.Priority);
        SetProp(r, nameof(KeepRequest.WaitingDirection), WaitingDirection.Business);

        var (group, order) = KeepRequestRankingAndActionsBuilder.ComputeRankingGroup(
            r, isPostClose: false, firstResponsePending: false, firstResponseOverdue: false,
            overdueBusinessWaiting: false, isDueOrOverdueFollowUpOn: false);

        Assert.Equal("priority_business_waiting", group);
        Assert.Equal(2, order);
    }

    [Fact]
    public void ComputeRankingGroup_customer_urgent_active_uses_business_priority_over_intake_urgency()
    {
        var r = MakeRequest();
        SetProp(r, nameof(KeepRequest.BusinessPriority), BusinessPriority.Urgent);
        SetProp(r, nameof(KeepRequest.IntakeUrgency), IntakeUrgency.Routine);
        SetProp(r, nameof(KeepRequest.Status), KeepRequestStatus.InProgress);

        var (group, order) = KeepRequestRankingAndActionsBuilder.ComputeRankingGroup(
            r, isPostClose: false, firstResponsePending: false, firstResponseOverdue: false,
            overdueBusinessWaiting: false, isDueOrOverdueFollowUpOn: false);

        Assert.Equal("customer_urgent_active", group);
        Assert.Equal(3, order);
    }

    [Fact]
    public void ComputeRankingGroup_customer_urgent_active_excludes_pending_customer()
    {
        var r = MakeRequest();
        SetProp(r, nameof(KeepRequest.IntakeUrgency), IntakeUrgency.Urgent);
        SetProp(r, nameof(KeepRequest.Status), KeepRequestStatus.PendingCustomer);

        var (group, _) = KeepRequestRankingAndActionsBuilder.ComputeRankingGroup(
            r, isPostClose: false, firstResponsePending: false, firstResponseOverdue: false,
            overdueBusinessWaiting: false, isDueOrOverdueFollowUpOn: false);

        Assert.Equal("waiting_on_customer", group);
    }

    [Fact]
    public void ComputeRankingGroup_standard_business_waiting()
    {
        var r = MakeRequest();
        SetProp(r, nameof(KeepRequest.WaitingDirection), WaitingDirection.Business);

        var (group, order) = KeepRequestRankingAndActionsBuilder.ComputeRankingGroup(
            r, isPostClose: false, firstResponsePending: false, firstResponseOverdue: false,
            overdueBusinessWaiting: false, isDueOrOverdueFollowUpOn: false);

        Assert.Equal("standard_business_waiting", group);
        Assert.Equal(5, order);
    }

    [Fact]
    public void ComputeRankingGroup_due_follow_up_on_shares_order_five_with_standard_business_waiting()
    {
        var r = MakeRequest();

        var (group, order) = KeepRequestRankingAndActionsBuilder.ComputeRankingGroup(
            r, isPostClose: false, firstResponsePending: false, firstResponseOverdue: false,
            overdueBusinessWaiting: false, isDueOrOverdueFollowUpOn: true);

        Assert.Equal("due_follow_up_on", group);
        Assert.Equal(5, order);
    }

    [Fact]
    public void ComputeRankingGroup_first_response_pending()
    {
        var r = MakeRequest();

        var (group, order) = KeepRequestRankingAndActionsBuilder.ComputeRankingGroup(
            r, isPostClose: false, firstResponsePending: true, firstResponseOverdue: false,
            overdueBusinessWaiting: false, isDueOrOverdueFollowUpOn: false);

        Assert.Equal("first_response_pending", group);
        Assert.Equal(6, order);
    }

    [Fact]
    public void ComputeRankingGroup_waiting_on_customer()
    {
        var r = MakeRequest();
        SetProp(r, nameof(KeepRequest.Status), KeepRequestStatus.PendingCustomer);

        var (group, order) = KeepRequestRankingAndActionsBuilder.ComputeRankingGroup(
            r, isPostClose: false, firstResponsePending: false, firstResponseOverdue: false,
            overdueBusinessWaiting: false, isDueOrOverdueFollowUpOn: false);

        Assert.Equal("waiting_on_customer", group);
        Assert.Equal(7, order);
    }

    [Fact]
    public void ComputeRankingGroup_resolved_quiet()
    {
        var r = MakeRequest();
        SetProp(r, nameof(KeepRequest.Status), KeepRequestStatus.Resolved);

        var (group, order) = KeepRequestRankingAndActionsBuilder.ComputeRankingGroup(
            r, isPostClose: false, firstResponsePending: false, firstResponseOverdue: false,
            overdueBusinessWaiting: false, isDueOrOverdueFollowUpOn: false);

        Assert.Equal("resolved_quiet", group);
        Assert.Equal(8, order);
    }

    [Fact]
    public void ComputeRankingGroup_falls_back_to_active()
    {
        var r = MakeRequest();
        SetProp(r, nameof(KeepRequest.Status), KeepRequestStatus.Resolved);
        SetProp(r, nameof(KeepRequest.AttentionLevel), AttentionLevel.Waiting);

        var (group, order) = KeepRequestRankingAndActionsBuilder.ComputeRankingGroup(
            r, isPostClose: false, firstResponsePending: false, firstResponseOverdue: false,
            overdueBusinessWaiting: false, isDueOrOverdueFollowUpOn: false);

        Assert.Equal("active", group);
        Assert.Equal(9, order);
    }

    // --- ComputeSeverity -----------------------------------------------------------

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public void ComputeSeverity_returns_danger(bool isOverdue, bool isPostClose, bool isFollowUpOverdue)
    {
        var r = MakeRequest();

        var severity = KeepRequestRankingAndActionsBuilder.ComputeSeverity(
            r, isOverdue, isPostClose, firstResponsePending: false,
            isDueOrOverdueFollowUpOn: false, isFollowUpOverdue);

        Assert.Equal("danger", severity);
    }

    [Theory]
    [InlineData(AttentionReason.Complaint)]
    [InlineData(AttentionReason.ScheduleChangeRequest)]
    [InlineData(AttentionReason.ChangeOrCancelRequest)]
    public void ComputeSeverity_danger_for_specific_attention_reasons(AttentionReason reason)
    {
        var r = MakeRequest();
        SetProp(r, nameof(KeepRequest.AttentionReason), reason);

        var severity = KeepRequestRankingAndActionsBuilder.ComputeSeverity(
            r, isOverdue: false, isPostClose: false, firstResponsePending: false,
            isDueOrOverdueFollowUpOn: false, isFollowUpOverdue: false);

        Assert.Equal("danger", severity);
    }

    [Fact]
    public void ComputeSeverity_priority_business_waiting()
    {
        var r = MakeRequest();
        SetProp(r, nameof(KeepRequest.PriorityBand), PriorityBand.Priority);
        SetProp(r, nameof(KeepRequest.WaitingDirection), WaitingDirection.Business);

        var severity = KeepRequestRankingAndActionsBuilder.ComputeSeverity(
            r, isOverdue: false, isPostClose: false, firstResponsePending: false,
            isDueOrOverdueFollowUpOn: false, isFollowUpOverdue: false);

        Assert.Equal("priority", severity);
    }

    [Fact]
    public void ComputeSeverity_business_waiting_without_priority_is_attention()
    {
        var r = MakeRequest();
        SetProp(r, nameof(KeepRequest.WaitingDirection), WaitingDirection.Business);

        var severity = KeepRequestRankingAndActionsBuilder.ComputeSeverity(
            r, isOverdue: false, isPostClose: false, firstResponsePending: false,
            isDueOrOverdueFollowUpOn: false, isFollowUpOverdue: false);

        Assert.Equal("attention", severity);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void ComputeSeverity_first_response_or_follow_up_due_is_attention(bool firstResponsePending, bool isDueOrOverdueFollowUpOn)
    {
        var r = MakeRequest();

        var severity = KeepRequestRankingAndActionsBuilder.ComputeSeverity(
            r, isOverdue: false, isPostClose: false, firstResponsePending,
            isDueOrOverdueFollowUpOn, isFollowUpOverdue: false);

        Assert.Equal("attention", severity);
    }

    [Fact]
    public void ComputeSeverity_pending_customer_is_neutral()
    {
        var r = MakeRequest();
        SetProp(r, nameof(KeepRequest.Status), KeepRequestStatus.PendingCustomer);

        var severity = KeepRequestRankingAndActionsBuilder.ComputeSeverity(
            r, isOverdue: false, isPostClose: false, firstResponsePending: false,
            isDueOrOverdueFollowUpOn: false, isFollowUpOverdue: false);

        Assert.Equal("neutral", severity);
    }

    [Fact]
    public void ComputeSeverity_falls_back_to_muted()
    {
        var r = MakeRequest();
        SetProp(r, nameof(KeepRequest.Status), KeepRequestStatus.InProgress);

        var severity = KeepRequestRankingAndActionsBuilder.ComputeSeverity(
            r, isOverdue: false, isPostClose: false, firstResponsePending: false,
            isDueOrOverdueFollowUpOn: false, isFollowUpOverdue: false);

        Assert.Equal("muted", severity);
    }

    // --- BuildQuickActions -----------------------------------------------------------

    [Fact]
    public void BuildQuickActions_post_close_includes_review_feedback_and_contact_when_allowed()
    {
        var r = MakeRequest();

        var actions = KeepRequestRankingAndActionsBuilder.BuildQuickActions(
            r, canOperate: true, isPostClose: true, firstResponseOverdue: false, AllowAll);

        Assert.Collection(actions,
            a => Assert.Equal("open_detail", a.Code),
            a => Assert.Equal("review_feedback", a.Code),
            a => Assert.Equal("contact_customer", a.Code));
    }

    [Fact]
    public void BuildQuickActions_post_close_omits_review_feedback_and_contact_when_denied()
    {
        var r = MakeRequest();

        var actions = KeepRequestRankingAndActionsBuilder.BuildQuickActions(
            r, canOperate: true, isPostClose: true, firstResponseOverdue: false, DenyAll);

        Assert.Collection(actions, a => Assert.Equal("open_detail", a.Code));
    }

    [Fact]
    public void BuildQuickActions_returns_only_open_detail_when_cannot_operate()
    {
        var r = MakeRequest();

        var actions = KeepRequestRankingAndActionsBuilder.BuildQuickActions(
            r, canOperate: false, isPostClose: false, firstResponseOverdue: false, AllowAll);

        Assert.Collection(actions, a => Assert.Equal("open_detail", a.Code));
    }

    [Fact]
    public void BuildQuickActions_returns_only_open_detail_when_terminal()
    {
        var r = MakeRequest();
        SetProp(r, nameof(KeepRequest.Status), KeepRequestStatus.Closed);

        var actions = KeepRequestRankingAndActionsBuilder.BuildQuickActions(
            r, canOperate: true, isPostClose: false, firstResponseOverdue: false, AllowAll);

        Assert.Collection(actions, a => Assert.Equal("open_detail", a.Code));
    }

    [Fact]
    public void BuildQuickActions_active_request_includes_all_allowed_actions_in_fixed_order()
    {
        var r = MakeRequest();

        var actions = KeepRequestRankingAndActionsBuilder.BuildQuickActions(
            r, canOperate: true, isPostClose: false, firstResponseOverdue: false, AllowAll);

        Assert.Collection(actions,
            a => Assert.Equal("open_detail", a.Code),
            a => Assert.Equal("contact_customer", a.Code),
            a => Assert.Equal("post_customer_update", a.Code),
            a => Assert.Equal("add_internal_note", a.Code),
            a => Assert.Equal("acknowledge_attention", a.Code),
            a => Assert.Equal("close_request", a.Code));
    }

    [Fact]
    public void BuildQuickActions_suppresses_acknowledge_attention_when_first_response_overdue_with_no_response()
    {
        var r = MakeRequest();

        var actions = KeepRequestRankingAndActionsBuilder.BuildQuickActions(
            r, canOperate: true, isPostClose: false, firstResponseOverdue: true, AllowAll);

        Assert.DoesNotContain(actions, a => a.Code == "acknowledge_attention");
    }

    [Fact]
    public void BuildQuickActions_omits_contact_customer_when_no_phone_or_email()
    {
        var r = MakeRequest();
        SetProp(r, nameof(KeepRequest.CustomerPhone), "");

        var actions = KeepRequestRankingAndActionsBuilder.BuildQuickActions(
            r, canOperate: true, isPostClose: false, firstResponseOverdue: false, AllowAll);

        Assert.DoesNotContain(actions, a => a.Code == "contact_customer");
    }

    // --- BuildContactActions -----------------------------------------------------------

    [Fact]
    public void BuildContactActions_returns_empty_when_cannot_operate()
    {
        var r = MakeRequest();

        var actions = KeepRequestRankingAndActionsBuilder.BuildContactActions(
            r, canOperate: false, isPostClose: false, AllowAll);

        Assert.Empty(actions);
    }

    [Fact]
    public void BuildContactActions_returns_empty_when_log_external_contact_denied()
    {
        var r = MakeRequest();

        var actions = KeepRequestRankingAndActionsBuilder.BuildContactActions(
            r, canOperate: true, isPostClose: false, DenyAll);

        Assert.Empty(actions);
    }

    [Fact]
    public void BuildContactActions_includes_call_and_email_when_both_present()
    {
        var r = MakeRequest(phone: "555-0001", email: "bob@example.com");

        var actions = KeepRequestRankingAndActionsBuilder.BuildContactActions(
            r, canOperate: true, isPostClose: false, AllowAll);

        Assert.Collection(actions,
            a => Assert.Equal("call", a.Type),
            a => Assert.Equal("email", a.Type));
    }

    [Fact]
    public void BuildContactActions_omits_call_when_phone_is_blank()
    {
        var r = MakeRequest(email: "bob@example.com");
        SetProp(r, nameof(KeepRequest.CustomerPhone), "");

        var actions = KeepRequestRankingAndActionsBuilder.BuildContactActions(
            r, canOperate: true, isPostClose: false, AllowAll);

        Assert.Collection(actions, a => Assert.Equal("email", a.Type));
    }
}

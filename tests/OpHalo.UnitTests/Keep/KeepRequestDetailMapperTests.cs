using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Keep.Application.Requests;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;

namespace OpHalo.UnitTests.Keep;

// Session 0A: asserts PrimaryAction/MarkWorkDoneSecondary reach the final serialized
// KeepRequestDetailResult.AvailableActions exactly as KeepRequestActionPolicy selects them —
// end-to-end through ToDetailResult, not just at the pure-selector level covered by
// KeepRequestActionPolicyTests.
public class KeepRequestDetailMapperTests
{
    static readonly Guid AccountId = Guid.NewGuid();
    static readonly Guid CustomerId = Guid.NewGuid();
    static readonly Guid ActorId = Guid.NewGuid();
    const string ActorName = "Test User";
    static readonly DateTime Now = new(2026, 6, 21, 12, 0, 0, DateTimeKind.Utc);

    // CreatedAtUtc kept just minutes before Now (not days) so FirstResponseDueAtUtc (created +
    // 60-min SLA) stays in the future — otherwise case-3 first-response-overdue attention would
    // fire and mask the no-attention scenarios these tests exercise.
    static KeepRequest MakeReceived() =>
        KeepRequest.CreateFromCustomerIntake(AccountId, CustomerId, "Alice", "555-0001", null,
            "A description", "REF001", "tok_" + Guid.NewGuid().ToString("N"), Now.AddMinutes(-5), 60);

    static KeepRequestDetailResult BuildDetail(KeepRequest r, AccountUserRole role = AccountUserRole.Owner)
    {
        var context = new KeepRequestActionContext(role, CanWrite: true, null, null);
        var decision = KeepRequestActionPolicy.Evaluate(r, context);
        var availableActions = KeepRequestDetailMapper.ToAvailableActionsMetadata(decision);

        return KeepRequestDetailMapper.ToDetailResult(
            r, "Acme Services", [], [], availableActions, role, canOperate: true, ActorId, Now);
    }

    [Fact]
    public void ToDetailResult_no_attention_eligible_request_carries_mark_work_done_primary()
    {
        var detail = BuildDetail(MakeReceived());

        Assert.NotNull(detail.AvailableActions.PrimaryAction);
        Assert.Equal("mark_work_done", detail.AvailableActions.PrimaryAction!.Key);
        Assert.Equal("mutation", detail.AvailableActions.PrimaryAction.Target);
        Assert.Null(detail.AvailableActions.MarkWorkDoneSecondary);
    }

    [Fact]
    public void ToDetailResult_active_attention_carries_guidance_primary_and_demotes_work_done_to_secondary()
    {
        var r = MakeReceived();
        SetAttention(r, AttentionLevel.NeedsAttention, AttentionReason.CustomerMessage);

        var detail = BuildDetail(r);

        Assert.NotNull(detail.AvailableActions.PrimaryAction);
        Assert.Equal("respond_to_customer", detail.AvailableActions.PrimaryAction!.Key);
        Assert.Equal("customer_update_composer", detail.AvailableActions.PrimaryAction.Target);

        Assert.NotNull(detail.AvailableActions.MarkWorkDoneSecondary);
        Assert.Equal("attention_remains", detail.AvailableActions.MarkWorkDoneSecondary!.Consequence);
        Assert.NotEqual("mark_work_done", detail.AvailableActions.PrimaryAction.Key);
    }

    [Fact]
    public void ToDetailResult_Resolved_no_attention_CanClose_carries_close_request_primary_with_confirmation()
    {
        var r = MakeReceived();
        r.ChangeStatus(KeepRequestStatus.Resolved, null, ActorId, ActorName, Now.AddHours(-1));

        var detail = BuildDetail(r);

        Assert.NotNull(detail.AvailableActions.PrimaryAction);
        Assert.Equal("close_request", detail.AvailableActions.PrimaryAction!.Key);
        Assert.True(detail.AvailableActions.PrimaryAction.RequiresConfirmation);
        Assert.False(string.IsNullOrEmpty(detail.AvailableActions.PrimaryAction.ConfirmationCopy));
    }

    [Fact]
    public void ToDetailResult_terminal_status_carries_no_primary_action()
    {
        var r = MakeReceived();
        r.ChangeStatus(KeepRequestStatus.Resolved, null, ActorId, ActorName, Now.AddHours(-2));
        r.ChangeStatus(KeepRequestStatus.Closed, null, ActorId, ActorName, Now.AddHours(-1));

        var detail = BuildDetail(r);

        Assert.Null(detail.AvailableActions.PrimaryAction);
        Assert.Null(detail.AvailableActions.MarkWorkDoneSecondary);
    }

    [Fact]
    public void ToDetailResult_EffectiveAttention_field_matches_selector_input()
    {
        var r = MakeReceived();
        SetAttention(r, AttentionLevel.NeedsAttention, AttentionReason.CustomerMessage);

        var detail = BuildDetail(r);

        Assert.Equal("needs_attention", detail.EffectiveAttention.Level);
        Assert.Equal("respond_to_customer", detail.AvailableActions.PrimaryAction?.Key);
    }

    static void SetAttention(KeepRequest r, AttentionLevel level, AttentionReason reason)
    {
        typeof(KeepRequest).GetProperty(nameof(KeepRequest.AttentionLevel))!.SetValue(r, level);
        typeof(KeepRequest).GetProperty(nameof(KeepRequest.AttentionReason))!.SetValue(r, reason);
    }
}

using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Keep.Application.Notifications;
using OpHalo.Keep.Core.Entities.Enums;

namespace OpHalo.UnitTests.Keep;

public class KeepPushCandidateServiceTests
{
    static readonly Guid AccountId = Guid.NewGuid();
    static readonly Guid RequestId = Guid.NewGuid();
    static readonly Guid ActorId = Guid.NewGuid();
    static readonly Guid UserId1 = Guid.NewGuid();
    static readonly Guid UserId2 = Guid.NewGuid();
    static readonly Guid UserId3 = Guid.NewGuid();

    readonly KeepPushCandidateService _svc = new();

    static KeepPushParticipantInfo Participant(
        Guid userId,
        ParticipationType type,
        AccountUserRole role = AccountUserRole.Operator,
        bool notifications = true,
        bool active = true,
        MembershipStatus membership = MembershipStatus.Active) =>
        new(userId, type, active, notifications, role, membership);

    static KeepPushMemberInfo Member(
        Guid userId,
        AccountUserRole role = AccountUserRole.Owner,
        MembershipStatus membership = MembershipStatus.Active) =>
        new(userId, role, membership);

    KeepPushRoutingContext Ctx(
        KeepPushEventKind kind = KeepPushEventKind.CallRequested,
        bool isTerminal = false,
        bool isOffSeason = false,
        IReadOnlyList<KeepPushParticipantInfo>? participants = null,
        IReadOnlyList<KeepPushMemberInfo>? fallback = null) =>
        new(AccountId, RequestId, kind, ActorId, isTerminal, isOffSeason,
            participants ?? [], fallback ?? []);

    [Fact]
    public void OffSeason_ReturnsEmpty()
    {
        var ctx = Ctx(isOffSeason: true, participants: [Participant(UserId1, ParticipationType.Responsible)]);
        Assert.Empty(_svc.GetCandidates(ctx));
    }

    [Fact]
    public void Terminal_ReturnsEmpty()
    {
        var ctx = Ctx(isTerminal: true, participants: [Participant(UserId1, ParticipationType.Responsible)]);
        Assert.Empty(_svc.GetCandidates(ctx));
    }

    [Fact]
    public void Terminal_UnresolvedFeedback_FallbackOwnerAdmin_NotEmpty()
    {
        // Closed + UnresolvedFeedback is push-worthy even when the request is terminal (ADR-360).
        var ctx = Ctx(
            kind: KeepPushEventKind.UnresolvedFeedback,
            isTerminal: true,
            fallback: [Member(UserId1, AccountUserRole.Owner), Member(UserId2, AccountUserRole.Admin)]);
        Assert.Equal([UserId1, UserId2], _svc.GetCandidates(ctx));
    }

    [Fact]
    public void Terminal_NonFeedbackKind_ReturnsEmpty()
    {
        // Non-feedback push-worthy events are suppressed on terminal requests.
        var ctx = Ctx(
            kind: KeepPushEventKind.CallRequested,
            isTerminal: true,
            fallback: [Member(UserId1)]);
        Assert.Empty(_svc.GetCandidates(ctx));
    }

    [Fact]
    public void Responsible_Eligible_ReturnsResponsible()
    {
        var ctx = Ctx(participants: [Participant(UserId1, ParticipationType.Responsible)]);
        Assert.Equal([UserId1], _svc.GetCandidates(ctx));
    }

    [Fact]
    public void Responsible_IsMuted_FallsBackToWatchers()
    {
        var participants = new[]
        {
            Participant(UserId1, ParticipationType.Responsible, notifications: false),
            Participant(UserId2, ParticipationType.Watching)
        };
        var ctx = Ctx(participants: participants);
        Assert.Equal([UserId2], _svc.GetCandidates(ctx));
    }

    [Fact]
    public void Responsible_IsActor_FallsBackToWatchers()
    {
        var ctx = Ctx(participants:
        [
            new(ActorId, ParticipationType.Responsible, true, true, AccountUserRole.Operator, MembershipStatus.Active),
            Participant(UserId2, ParticipationType.Watching)
        ]);
        Assert.Equal([UserId2], _svc.GetCandidates(ctx));
    }

    [Fact]
    public void Viewer_Excluded()
    {
        var ctx = Ctx(participants:
        [
            Participant(UserId1, ParticipationType.Responsible, role: AccountUserRole.Viewer)
        ]);
        Assert.Empty(_svc.GetCandidates(ctx));
    }

    [Fact]
    public void InactiveMembership_Excluded()
    {
        var ctx = Ctx(participants:
        [
            Participant(UserId1, ParticipationType.Responsible, membership: MembershipStatus.Suspended)
        ]);
        Assert.Empty(_svc.GetCandidates(ctx));
    }

    [Fact]
    public void DetachedParticipant_Excluded()
    {
        var detached = new KeepPushParticipantInfo(
            UserId1, ParticipationType.Responsible, IsActive: false, true, AccountUserRole.Operator, MembershipStatus.Active);
        var ctx = Ctx(participants: [detached]);
        Assert.Empty(_svc.GetCandidates(ctx));
    }

    [Fact]
    public void MultipleWatchers_AllEligible_ReturnsAll()
    {
        var participants = new[]
        {
            Participant(UserId1, ParticipationType.Watching),
            Participant(UserId2, ParticipationType.Watching)
        };
        var ctx = Ctx(participants: participants);
        var result = _svc.GetCandidates(ctx);
        Assert.Contains(UserId1, result);
        Assert.Contains(UserId2, result);
    }

    [Fact]
    public void NoParticipants_FallsBackToOwnerAdminMembers()
    {
        var fallback = new[]
        {
            Member(UserId1, AccountUserRole.Owner),
            Member(UserId2, AccountUserRole.Admin)
        };
        var ctx = Ctx(fallback: fallback);
        var result = _svc.GetCandidates(ctx);
        Assert.Contains(UserId1, result);
        Assert.Contains(UserId2, result);
    }

    [Fact]
    public void FallbackOperator_NotIncluded()
    {
        var fallback = new[] { Member(UserId1, AccountUserRole.Operator) };
        var ctx = Ctx(fallback: fallback);
        Assert.Empty(_svc.GetCandidates(ctx));
    }

    [Fact]
    public void FallbackActor_Excluded()
    {
        var fallback = new[] { Member(ActorId, AccountUserRole.Owner) };
        var ctx = Ctx(fallback: fallback);
        Assert.Empty(_svc.GetCandidates(ctx));
    }

    [Fact]
    public void FallbackMember_AlreadyActiveParticipant_NotDuplicated()
    {
        // UserId1 is a Watcher; should not also appear as fallback
        var participants = new[] { Participant(UserId1, ParticipationType.Watching) };
        var fallback = new[] { Member(UserId1, AccountUserRole.Owner) };
        var ctx = Ctx(participants: participants, fallback: fallback);
        var result = _svc.GetCandidates(ctx);
        // Watcher step returns first; fallback not reached
        Assert.Equal([UserId1], result);
    }
}

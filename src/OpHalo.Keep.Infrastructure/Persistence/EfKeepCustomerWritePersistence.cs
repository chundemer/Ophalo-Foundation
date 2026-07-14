using Microsoft.EntityFrameworkCore;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Foundation.Infrastructure.Persistence;
using OpHalo.Keep.Application.Requests;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;

namespace OpHalo.Keep.Infrastructure.Persistence;

public sealed class EfKeepCustomerWritePersistence(OpHaloDbContext dbContext) : IKeepCustomerWritePersistence
{
    public async Task<KeepRequest?> GetRequestForUpdateAsync(Guid requestId, CancellationToken ct) =>
        await dbContext.Set<KeepRequest>()
            .FirstOrDefaultAsync(r => r.Id == requestId, ct);

    public async Task<KeepResponsePolicy?> GetResponsePolicyAsync(Guid accountId, CancellationToken ct) =>
        await dbContext.Set<KeepResponsePolicy>()
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.AccountId == accountId, ct);

    public async Task<KeepRequestCommitResult> CommitAsync(KeepRequest request, KeepRequestEvent newEvent, CancellationToken ct)
    {
        dbContext.Set<KeepRequestEvent>().Add(newEvent);
        request.RotateConcurrencyVersion();
        try
        {
            await dbContext.SaveChangesAsync(ct);
            return KeepRequestCommitResult.Committed;
        }
        catch (DbUpdateConcurrencyException)
        {
            return KeepRequestCommitResult.Conflict;
        }
    }

    public async Task<KeepRequestCommitResult> CommitFeedbackAsync(KeepRequest request, KeepRequestEvent feedbackEvent, CancellationToken ct)
    {
        dbContext.Set<KeepRequestEvent>().Add(feedbackEvent);
        request.RotateConcurrencyVersion();
        try
        {
            await dbContext.SaveChangesAsync(ct);
            return KeepRequestCommitResult.Committed;
        }
        catch (DbUpdateConcurrencyException)
        {
            return KeepRequestCommitResult.Conflict;
        }
    }

    public async Task<IReadOnlyList<KeepRequestEvent>> GetCustomerVisibleEventsAsync(
        Guid requestId, CancellationToken ct) =>
        await dbContext.Set<KeepRequestEvent>()
            .AsNoTracking()
            .Where(e => e.RequestId == requestId && e.Visibility == KeepRequestEventVisibility.All)
            .OrderBy(e => e.OccurredAtUtc)
            .ToListAsync(ct);

    public async Task CommitPageViewAsync(KeepRequest request, CancellationToken ct)
    {
        try
        {
            await dbContext.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // A concurrent operator write won the race. Page-view telemetry is best-effort;
            // losing this write is acceptable — the operator's update is more important.
        }
    }

    public async Task<IReadOnlyList<KeepParticipantProjection>> GetParticipantsAsync(
        Guid requestId, CancellationToken ct)
    {
        var participants = await dbContext.Set<KeepRequestParticipant>()
            .AsNoTracking()
            .Where(p => p.RequestId == requestId)
            .ToListAsync(ct);

        if (participants.Count == 0) return [];

        var accountUserIds = participants.Select(p => p.AccountUserId).ToHashSet();

        var accountUsers = await dbContext.AccountUsers
            .AsNoTracking()
            .Where(au => accountUserIds.Contains(au.Id))
            .Select(au => new {
                au.Id,
                au.Email,
                au.Role,
                au.MembershipStatus,
                UserName = au.UserId != null ? au.User!.Name : null
            })
            .ToListAsync(ct);

        var lookup = accountUsers.ToDictionary(au => au.Id);

        return participants.Select(p =>
        {
            if (!lookup.TryGetValue(p.AccountUserId, out var au))
                throw new InvalidOperationException(
                    $"KeepRequestParticipant {p.AccountUserId} has no corresponding AccountUser.");

            return new KeepParticipantProjection(
                p.AccountUserId,
                p.ParticipationType,
                p.NotificationsEnabled,
                p.AttachedAtUtc,
                p.DetachedAtUtc,
                DisplayName: !string.IsNullOrWhiteSpace(au.UserName) ? au.UserName : au.Email,
                Role: au.Role,
                MembershipStatus: au.MembershipStatus);
        }).ToList();
    }

    public async Task<IReadOnlyList<ParticipantCandidateRecord>> GetActiveOwnerAdminMembersAsync(
        Guid accountId, CancellationToken ct)
    {
        var rows = await dbContext.AccountUsers
            .AsNoTracking()
            .Where(au => au.AccountId == accountId
                && au.MembershipStatus == MembershipStatus.Active
                && (au.Role == AccountUserRole.Owner || au.Role == AccountUserRole.Admin))
            .Select(au => new {
                au.Id,
                au.Email,
                au.Role,
                UserName = au.UserId != null ? au.User!.Name : null
            })
            .ToListAsync(ct);

        return rows
            .Select(au => new ParticipantCandidateRecord(
                au.Id,
                !string.IsNullOrWhiteSpace(au.UserName) ? au.UserName : au.Email,
                au.Role))
            .ToList();
    }
}

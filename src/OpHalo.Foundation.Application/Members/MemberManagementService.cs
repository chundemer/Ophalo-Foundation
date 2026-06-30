using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpHalo.Foundation.Application.Abstractions.Messaging;
using OpHalo.Foundation.Application.Abstractions.Security;
using OpHalo.Foundation.Application.Accounts.Authorization;
using OpHalo.Foundation.Application.Accounts.Entitlements;
using OpHalo.Foundation.Application.Auth;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Foundation.Core.Entities.Accounts.Errors;
using OpHalo.SharedKernel.Abstractions;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Foundation.Application.Members;

/// <summary>
/// Handles member-management operations for the current authenticated account:
/// list, role change, suspend, reactivate, remove, and resend-invite.
///
/// Authorization: caller must be authenticated and hold account.members.manage (Owner or Admin).
/// Owner safety: max 2 non-Removed Owners; at least 1 Active Owner must remain;
///   only Owners can manage Owner-role members; primary owner is protected in Phase 5E.
/// Session revocation: performed after commit for suspend and remove (Active/Suspended source).
///   Role change does not revoke sessions — auth reloads role/status from persistence each request.
/// Seat limits: checked on reactivate-from-Removed and resend-from-Removed; bypassed otherwise.
/// </summary>
public sealed class MemberManagementService(
    IMemberManagementPersistence persistence,
    ICurrentUser currentUser,
    IUserAccessPolicy userAccessPolicy,
    IFeatureAccessPolicy featurePolicy,
    IAccountSessionService sessionService,
    IEmailSender emailSender,
    IOptions<MagicLinkSettings> settings,
    IClock clock,
    ILogger<MemberManagementService> logger)
{
    private static readonly Error Unauthorized =
        Error.Create("auth.unauthorized", "Authentication required.");

    // -------------------------------------------------------------------------
    // List
    // -------------------------------------------------------------------------

    public async Task<Result<ListMembersResponse>> ListMembersAsync(
        bool includeRemoved,
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated)
            return Result<ListMembersResponse>.Failure(Unauthorized);

        var context = await persistence.GetMemberListContextAsync(
            currentUser.AccountId, includeRemoved, cancellationToken);

        if (context is null)
            return Result<ListMembersResponse>.Failure(MemberErrors.NotFound);

        var members = context.Members
            .Select(m => new MemberItem(
                AccountUserId: m.AccountUserId,
                Email: m.Email,
                Role: MapRole(m.Role),
                Status: MapStatus(m.Status),
                IsCurrentUser: m.AccountUserId == currentUser.UserId,
                IsPrimaryOwner: m.AccountUserId == context.PrimaryOwnerAccountUserId,
                ActivatedAtUtc: m.ActivatedAtUtc,
                InviteExpiresAtUtc: m.InviteExpiresAtUtc))
            .ToList();

        var maxSeats = featurePolicy.ResolveLimit(context.Entitlements, FeatureLimitKeys.Account.UserLimit);
        var limitApplies = maxSeats > 0;
        var seatUsage = new SeatUsage(
            context.OccupiedSeats,
            maxSeats,
            AtLimit: limitApplies && context.OccupiedSeats >= maxSeats,
            LimitApplies: limitApplies);

        return Result<ListMembersResponse>.Success(new ListMembersResponse(members, seatUsage));
    }

    // -------------------------------------------------------------------------
    // Role change
    // -------------------------------------------------------------------------

    public async Task<Result> ChangeRoleAsync(
        Guid targetAccountUserId,
        AccountUserRole newRole,
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated)
            return Result.Failure(Unauthorized);

        if (!Enum.IsDefined(newRole))
            return Result.Failure(MemberErrors.InvalidRole);

        if (targetAccountUserId == currentUser.UserId)
            return Result.Failure(MemberErrors.CannotModifySelf);

        var context = await persistence.GetMemberManagementContextAsync(
            currentUser.UserId, currentUser.AccountId, targetAccountUserId, cancellationToken);

        if (context is null)
            return Result.Failure(MemberErrors.NotFound);

        if (!userAccessPolicy.IsPermitted(
                context.CallerRole, context.CallerMembershipStatus,
                context.AccountPurpose, PermissionKeys.Account.MembersManage))
            return Result.Failure(MemberErrors.Forbidden);

        var target = context.Target;

        if (target.Id == context.PrimaryOwnerAccountUserId)
            return Result.Failure(MemberErrors.PrimaryOwnerProtected);

        // Admin cannot manage Owner-role members in either direction.
        if (context.CallerRole != AccountUserRole.Owner)
        {
            if (target.Role == AccountUserRole.Owner)
                return Result.Failure(MemberErrors.CannotModifyOwner);
            if (newRole == AccountUserRole.Owner)
                return Result.Failure(MemberErrors.CannotModifyOwner);
        }

        // Promoting to Owner: enforce cap (Active + Invited + Suspended Owners).
        if (newRole == AccountUserRole.Owner && context.NonRemovedOwnerCount >= 2)
            return Result.Failure(MemberErrors.OwnerLimitReached);

        // Demoting an Owner: must leave at least one Active Owner.
        if (target.Role == AccountUserRole.Owner && newRole != AccountUserRole.Owner
            && context.ActiveOwnerCount <= 1)
            return Result.Failure(MemberErrors.LastOwner);

        var result = target.ChangeRole(newRole);
        if (result.IsFailure)
            return Result.Failure(MemberErrors.InvalidStatusTransition);

        await persistence.CommitAsync(target, cancellationToken);
        return Result.Success();
    }

    // -------------------------------------------------------------------------
    // Suspend
    // -------------------------------------------------------------------------

    public async Task<Result> SuspendAsync(
        Guid targetAccountUserId,
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated)
            return Result.Failure(Unauthorized);

        if (targetAccountUserId == currentUser.UserId)
            return Result.Failure(MemberErrors.CannotModifySelf);

        var context = await persistence.GetMemberManagementContextAsync(
            currentUser.UserId, currentUser.AccountId, targetAccountUserId, cancellationToken);

        if (context is null)
            return Result.Failure(MemberErrors.NotFound);

        if (!userAccessPolicy.IsPermitted(
                context.CallerRole, context.CallerMembershipStatus,
                context.AccountPurpose, PermissionKeys.Account.MembersManage))
            return Result.Failure(MemberErrors.Forbidden);

        var target = context.Target;

        if (target.Id == context.PrimaryOwnerAccountUserId)
            return Result.Failure(MemberErrors.PrimaryOwnerProtected);

        if (context.CallerRole != AccountUserRole.Owner && target.Role == AccountUserRole.Owner)
            return Result.Failure(MemberErrors.CannotModifyOwner);

        // Suspending the last Active Owner leaves no active governance.
        if (target.Role == AccountUserRole.Owner
            && target.MembershipStatus == MembershipStatus.Active
            && context.ActiveOwnerCount <= 1)
            return Result.Failure(MemberErrors.LastOwner);

        // Invited members cannot be suspended — cancel the invite instead.
        if (target.MembershipStatus == MembershipStatus.Invited)
            return Result.Failure(MemberErrors.InvalidStatusTransition);

        var domainResult = target.Suspend();
        if (domainResult.IsFailure)
            return Result.Failure(MemberErrors.InvalidStatusTransition);

        await persistence.CommitAsync(target, cancellationToken);
        await TryRevokeSessionsAsync(targetAccountUserId, cancellationToken);
        return Result.Success();
    }

    // -------------------------------------------------------------------------
    // Reactivate
    // -------------------------------------------------------------------------

    public async Task<Result> ReactivateAsync(
        Guid targetAccountUserId,
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated)
            return Result.Failure(Unauthorized);

        if (targetAccountUserId == currentUser.UserId)
            return Result.Failure(MemberErrors.CannotModifySelf);

        var context = await persistence.GetMemberManagementContextAsync(
            currentUser.UserId, currentUser.AccountId, targetAccountUserId, cancellationToken);

        if (context is null)
            return Result.Failure(MemberErrors.NotFound);

        if (!userAccessPolicy.IsPermitted(
                context.CallerRole, context.CallerMembershipStatus,
                context.AccountPurpose, PermissionKeys.Account.MembersManage))
            return Result.Failure(MemberErrors.Forbidden);

        var target = context.Target;

        // Admin cannot reactivate an Owner-role member.
        if (context.CallerRole != AccountUserRole.Owner && target.Role == AccountUserRole.Owner)
            return Result.Failure(MemberErrors.CannotModifyOwner);

        // Seat check only for Removed-with-UserId (Removed does not occupy a seat).
        // Suspended reactivation skips the check because the seat is already counted.
        if (target.MembershipStatus == MembershipStatus.Removed)
        {
            var seatLimit = featurePolicy.ResolveLimit(
                context.Entitlements, FeatureLimitKeys.Account.UserLimit);
            if (seatLimit > 0 && context.OccupiedSeats >= seatLimit)
                return Result.Failure(MemberErrors.SeatLimitReached);
        }

        var domainResult = target.Reactivate();
        if (domainResult.IsFailure)
            return Result.Failure(MemberErrors.InvalidStatusTransition);

        await persistence.CommitAsync(target, cancellationToken);
        return Result.Success();
    }

    // -------------------------------------------------------------------------
    // Remove
    // -------------------------------------------------------------------------

    public async Task<Result> RemoveAsync(
        Guid targetAccountUserId,
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated)
            return Result.Failure(Unauthorized);

        if (targetAccountUserId == currentUser.UserId)
            return Result.Failure(MemberErrors.CannotModifySelf);

        var context = await persistence.GetMemberManagementContextAsync(
            currentUser.UserId, currentUser.AccountId, targetAccountUserId, cancellationToken);

        if (context is null)
            return Result.Failure(MemberErrors.NotFound);

        if (!userAccessPolicy.IsPermitted(
                context.CallerRole, context.CallerMembershipStatus,
                context.AccountPurpose, PermissionKeys.Account.MembersManage))
            return Result.Failure(MemberErrors.Forbidden);

        var target = context.Target;

        if (target.Id == context.PrimaryOwnerAccountUserId)
            return Result.Failure(MemberErrors.PrimaryOwnerProtected);

        if (context.CallerRole != AccountUserRole.Owner && target.Role == AccountUserRole.Owner)
            return Result.Failure(MemberErrors.CannotModifyOwner);

        // Removing the last Active Owner leaves no active governance.
        if (target.Role == AccountUserRole.Owner
            && target.MembershipStatus == MembershipStatus.Active
            && context.ActiveOwnerCount <= 1)
            return Result.Failure(MemberErrors.LastOwner);

        // Capture source status before mutation; revoke sessions only if Active or Suspended.
        var hadActiveSessions = target.MembershipStatus == MembershipStatus.Active
            || target.MembershipStatus == MembershipStatus.Suspended;

        target.Remove();
        await persistence.CommitAsync(target, cancellationToken);

        if (hadActiveSessions)
            await TryRevokeSessionsAsync(targetAccountUserId, cancellationToken);

        return Result.Success();
    }

    // -------------------------------------------------------------------------
    // Resend invite
    // -------------------------------------------------------------------------

    public async Task<Result<ResendInviteResult>> ResendInviteAsync(
        Guid targetAccountUserId,
        InviteDeliveryMode deliveryMode,
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated)
            return Result<ResendInviteResult>.Failure(Unauthorized);

        var context = await persistence.GetMemberManagementContextAsync(
            currentUser.UserId, currentUser.AccountId, targetAccountUserId, cancellationToken);

        if (context is null)
            return Result<ResendInviteResult>.Failure(MemberErrors.NotFound);

        if (!userAccessPolicy.IsPermitted(
                context.CallerRole, context.CallerMembershipStatus,
                context.AccountPurpose, PermissionKeys.Account.MembersManage))
            return Result<ResendInviteResult>.Failure(MemberErrors.Forbidden);

        var target = context.Target;
        var status = target.MembershipStatus;

        // Active or Suspended: reject — wrong endpoint.
        if (status == MembershipStatus.Active || status == MembershipStatus.Suspended)
            return Result<ResendInviteResult>.Failure(MemberErrors.InvalidStatusTransition);

        // Removed with UserId: use reactivate, not resend-invite.
        if (status == MembershipStatus.Removed && target.UserId is not null)
            return Result<ResendInviteResult>.Failure(MemberErrors.InvalidStatusTransition);

        var isRestore = status == MembershipStatus.Removed;

        // Seat check only when restoring from Removed (seat not currently occupied).
        if (isRestore)
        {
            var seatLimit = featurePolicy.ResolveLimit(
                context.Entitlements, FeatureLimitKeys.Account.UserLimit);
            if (seatLimit > 0 && context.OccupiedSeats >= seatLimit)
                return Result<ResendInviteResult>.Failure(MemberErrors.SeatLimitReached);
        }

        if (string.IsNullOrWhiteSpace(settings.Value.OperatorBaseUrl))
            return Result<ResendInviteResult>.Failure(
                Error.Create("Account.InconsistentState",
                    "Invite link cannot be built — OperatorBaseUrl is not configured."));

        var rawToken = InviteTokenGenerator.GenerateRawToken();
        var tokenHash = InviteTokenGenerator.HashToken(rawToken);
        var nowUtc = clock.UtcNow;
        var expiresAtUtc = nowUtc.AddDays(7);

        var domainResult = isRestore
            ? target.RestoreInvite(tokenHash, expiresAtUtc, nowUtc)
            : target.RefreshInvite(tokenHash, expiresAtUtc, nowUtc);

        if (domainResult.IsFailure)
            return Result<ResendInviteResult>.Failure(MemberErrors.InvalidStatusTransition);

        // Token is rotated and hash persisted before the invite URL is used or returned.
        await persistence.CommitAsync(target, cancellationToken);

        var inviteLink =
            $"{settings.Value.OperatorBaseUrl}/invite/accept?token={Uri.EscapeDataString(rawToken)}";

        return deliveryMode switch
        {
            InviteDeliveryMode.Email => await SendEmailAndReturnResultAsync(
                target.Email, context.AccountBusinessName, inviteLink, cancellationToken),

            InviteDeliveryMode.ManualShare =>
                // Invite URL returned once to the authenticated caller. Not emailed, not logged.
                Result<ResendInviteResult>.Success(
                    new ResendInviteResult(InviteDeliveryMode.ManualShare, inviteLink)),

            _ => throw new ArgumentOutOfRangeException(
                nameof(deliveryMode), deliveryMode, "Unsupported invite delivery mode.")
        };
    }

    private async Task<Result<ResendInviteResult>> SendEmailAndReturnResultAsync(
        string recipientEmail,
        string accountBusinessName,
        string inviteLink,
        CancellationToken cancellationToken)
    {
        try
        {
            var emailResult = await emailSender.SendAsync(
                recipientEmail,
                InviteEmailTemplate.BuildSubject(accountBusinessName),
                InviteEmailTemplate.BuildHtmlBody(accountBusinessName, inviteLink),
                cancellationToken);

            if (emailResult.IsFailure)
            {
                logger.LogWarning(
                    "Invite email delivery returned failure after resend committed. AccountId={AccountId}",
                    currentUser.AccountId);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Invite email delivery threw after resend committed. AccountId={AccountId}",
                currentUser.AccountId);
        }

        return Result<ResendInviteResult>.Success(
            new ResendInviteResult(InviteDeliveryMode.Email, null));
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private async Task TryRevokeSessionsAsync(Guid targetAccountUserId, CancellationToken ct)
    {
        try
        {
            await sessionService.RevokeAllSessionsByAccountUserId(
                currentUser.AccountId, targetAccountUserId, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Session revocation failed for AccountUser {AccountUserId}. " +
                "Access is already blocked by live membership status check.",
                targetAccountUserId);
        }
    }

    private static string MapRole(AccountUserRole role) => role switch
    {
        AccountUserRole.Owner    => "owner",
        AccountUserRole.Admin    => "admin",
        AccountUserRole.Operator => "operator",
        AccountUserRole.Viewer   => "viewer",
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, null)
    };

    private static string MapStatus(MembershipStatus status) => status switch
    {
        MembershipStatus.Active    => "active",
        MembershipStatus.Invited   => "invited",
        MembershipStatus.Suspended => "suspended",
        MembershipStatus.Removed   => "removed",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
    };
}

// -------------------------------------------------------------------------
// Response types
// -------------------------------------------------------------------------

public sealed record ResendInviteResult(
    InviteDeliveryMode DeliveryMode,
    /// <summary>
    /// Non-null for ManualShare — the full invite URL the caller should share with the invitee.
    /// Null for Email — the URL was sent by email and must not be surfaced in the API response.
    /// The invite URL contains the raw token and must not be logged anywhere in the pipeline.
    /// </summary>
    string? InviteUrl);

public sealed record ListMembersResponse(IReadOnlyList<MemberItem> Members, SeatUsage SeatUsage);

public sealed record SeatUsage(
    int OccupiedSeats,
    int MaxSeats,
    bool AtLimit,
    bool LimitApplies);

public sealed record MemberItem(
    Guid AccountUserId,
    string Email,
    string Role,
    string Status,
    bool IsCurrentUser,
    bool IsPrimaryOwner,
    DateTime? ActivatedAtUtc,
    DateTime? InviteExpiresAtUtc);

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpHalo.Foundation.Application.Abstractions.Messaging;
using OpHalo.Foundation.Application.Abstractions.Security;
using OpHalo.Foundation.Application.Accounts.Authorization;
using OpHalo.Foundation.Application.Accounts.Entitlements;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Foundation.Core.Entities.Accounts.Errors;
using OpHalo.Foundation.Core.Helpers;
using OpHalo.SharedKernel.Abstractions;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Foundation.Application.Auth;

/// <summary>
/// Handles POST /accounts/me/invite — sends or resends an account member invite.
///
/// Authorization: caller must be authenticated and hold account.members.manage (Owner or Admin).
/// Seat limit: counts Active + Invited + Suspended, excludes Removed (D5/ADR-075).
///   Resend of an existing Invited row bypasses the seat check — the seat is already reserved.
/// Resend: rotates token in place; old token is immediately invalid.
/// Email: direct IEmailSender, best-effort after commit — delivery failure does not roll back (D7).
/// Raw token is never returned in the API response — email delivery only (D8/ADR-011).
///
/// Logging: only unexpected failures with safe IDs (no email, token, or invite URL).
/// </summary>
public sealed class SendInviteService(
    IInvitePersistence persistence,
    ICurrentUser currentUser,
    IUserAccessPolicy userAccessPolicy,
    IFeatureAccessPolicy featurePolicy,
    IEmailSender emailSender,
    IOptions<MagicLinkSettings> settings,
    IClock clock,
    ILogger<SendInviteService> logger)
{
    private static readonly Error Unauthorized =
        Error.Create("auth.unauthorized", "Authentication required.");

    private static readonly Error ConfigurationError =
        Error.Create("Account.InconsistentState",
            "Invite link cannot be built — OperatorBaseUrl is not configured.");

    public async Task<Result<SendInviteResult>> HandleAsync(
        string email,
        AccountUserRole role,
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated)
            return Result<SendInviteResult>.Failure(Unauthorized);

        // Service-level guard: Owner cannot be invited; undefined values are rejected.
        // The endpoint validates role strings, but this guard ensures CreatePendingInvite
        // never receives an invalid role regardless of call site.
        if (!Enum.IsDefined(role) || role == AccountUserRole.Owner)
            return Result<SendInviteResult>.Failure(
                Error.Create("Validation.RoleInvalid", "The specified role is not valid for an invite."));

        if (string.IsNullOrWhiteSpace(settings.Value.OperatorBaseUrl))
            return Result<SendInviteResult>.Failure(ConfigurationError);

        var normalizedEmail = EmailNormalizer.Normalize(email);
        var nowUtc = clock.UtcNow;

        var context = await persistence.GetSendInviteContextAsync(
            currentUser.UserId, currentUser.AccountId, normalizedEmail, cancellationToken);

        if (context is null)
            return Result<SendInviteResult>.Failure(InviteErrors.Forbidden);

        if (!userAccessPolicy.IsPermitted(
                context.CallerRole,
                context.CallerMembershipStatus,
                context.AccountPurpose,
                PermissionKeys.Account.MembersManage))
        {
            return Result<SendInviteResult>.Failure(InviteErrors.Forbidden);
        }

        var existing = context.ExistingMembership;

        if (existing is not null && existing.MembershipStatus != MembershipStatus.Invited)
        {
            // Removed members surface as PreviouslyRemoved so the caller can route to
            // reactivate or resend-invite. The suggestedAction field in the HTTP response
            // is added by the endpoint in 5E-C (requires knowing whether UserId is set).
            if (existing.MembershipStatus == MembershipStatus.Removed)
                return Result<SendInviteResult>.Failure(MemberErrors.PreviouslyRemoved);

            // Active or Suspended — use the member-management endpoints.
            return Result<SendInviteResult>.Failure(InviteErrors.AlreadyActive);
        }

        // Resend: the invited seat is already counted. Skip the seat-limit check so a resend
        // at the limit still succeeds and rotates the token.
        bool isResend = existing is not null;

        if (!isResend)
        {
            // Seat limit check applies only to new invites (D5/ADR-075).
            var seatLimit = featurePolicy.ResolveLimit(context.Entitlements, FeatureLimitKeys.Account.UserLimit);
            if (seatLimit > 0 && context.OccupiedSeats >= seatLimit)
                return Result<SendInviteResult>.Failure(InviteErrors.SeatLimitReached);
        }

        var rawToken = InviteTokenGenerator.GenerateRawToken();
        var tokenHash = InviteTokenGenerator.HashToken(rawToken);
        var expiresAtUtc = nowUtc.AddDays(7);

        if (isResend)
        {
            var refreshResult = existing!.RefreshInvite(tokenHash, expiresAtUtc, nowUtc);
            if (refreshResult.IsFailure)
                return Result<SendInviteResult>.Failure(refreshResult.Error);

            await persistence.CommitSendInviteAsync(existing, cancellationToken);
        }
        else
        {
            var accountUser = Core.Entities.Accounts.AccountUser.CreatePendingInvite(
                currentUser.AccountId, email, normalizedEmail, role, tokenHash, expiresAtUtc, nowUtc);

            await persistence.CommitSendInviteAsync(accountUser, cancellationToken);
        }

        // Build and send invite email — best-effort after commit. Delivery failure is logged
        // but does not roll back the invite row (D7). Wrap in try/catch because IEmailSender
        // implementations may throw from the underlying HTTP call, not just return a failed Result.
        var inviteLink = $"{settings.Value.OperatorBaseUrl}/invite/accept?token={Uri.EscapeDataString(rawToken)}";

        try
        {
            var emailResult = await emailSender.SendAsync(
                email,
                InviteEmailTemplate.BuildSubject(context.AccountBusinessName),
                InviteEmailTemplate.BuildHtmlBody(context.AccountBusinessName, inviteLink),
                cancellationToken);

            if (emailResult.IsFailure)
            {
                logger.LogWarning(
                    "Invite email delivery returned failure after invite row committed. AccountId={AccountId}",
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
                "Invite email delivery threw after invite row committed. AccountId={AccountId}",
                currentUser.AccountId);
        }

        return Result<SendInviteResult>.Success(
            isResend ? SendInviteResult.Resent : SendInviteResult.Sent);
    }
}

public enum SendInviteResult { Sent, Resent }

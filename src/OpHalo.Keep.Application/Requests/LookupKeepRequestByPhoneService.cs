using OpHalo.Foundation.Application.Abstractions.Security;
using OpHalo.Foundation.Application.Accounts.Access;
using OpHalo.Foundation.Application.Accounts.Authorization;
using OpHalo.Foundation.Application.Accounts.Entitlements;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Keep.Application.Services;
using OpHalo.Keep.Core.Domain;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;
using OpHalo.SharedKernel.Abstractions;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Keep.Application.Requests;

public sealed record PhoneLookupCustomer(string Name, string Phone, string? Email);

public sealed record PhoneLookupActiveRequest(
    Guid RequestId,
    string ReferenceCode,
    string Status,
    string Description,
    DateTime? LastActivityAtUtc);

// ADR-492: a request-phone-only match is continuity evidence, not confirmed identity. The
// candidate's KeepCustomerId is real (KeepRequest.KeepCustomerId is non-nullable) but its current
// CanonicalPhone may no longer match the entered number — number could be stale/shared/recycled.
public sealed record PhoneLookupPossibleCustomer(
    Guid CandidateCustomerId,
    string Name,
    string Phone,
    string? Email,
    IReadOnlyList<PhoneLookupActiveRequest> ActiveRequests,
    bool HasMoreActiveRequests);

public sealed record PhoneLookupResult(
    PhoneLookupCustomer? Customer,
    IReadOnlyList<PhoneLookupActiveRequest> ActiveRequests,
    bool HasMoreActiveRequests,
    PhoneLookupPossibleCustomer? PossibleCustomer);

public sealed class LookupKeepRequestByPhoneService(
    IKeepRequestOperatePersistence operatePersistence,
    IKeepBusinessRequestPersistence businessRequestPersistence,
    ICurrentUser currentUser,
    IUserAccessPolicy userAccessPolicy,
    IAccountAccessPolicy accountAccessPolicy,
    IFeatureAccessPolicy featurePolicy,
    IClock clock)
{
    private static readonly Error Unauthorized =
        Error.Create("auth.unauthorized", "Authentication required.");

    private static readonly Error Forbidden =
        Error.Create("auth.forbidden", "You do not have permission to perform this action.");

    private static readonly Error InvalidPhone =
        Error.Create("KeepRequest.InvalidPhone", "Phone must contain exactly 10 digits.");

    public async Task<Result<PhoneLookupResult>> ExecuteAsync(string? rawPhone, CancellationToken ct = default)
    {
        // --- Auth stack (mirrors CreateBusinessRequestService exactly) ---
        if (!currentUser.IsAuthenticated)
            return Result<PhoneLookupResult>.Failure(Unauthorized);

        var userSnapshot = await operatePersistence.GetAccountUserSnapshotAsync(currentUser.UserId, ct);
        if (userSnapshot is null)
            return Result<PhoneLookupResult>.Failure(Forbidden);

        if (userSnapshot.Role is AccountUserRole.Viewer)
            return Result<PhoneLookupResult>.Failure(Forbidden);

        var accountSnapshot = await operatePersistence.GetAccountAccessSnapshotAsync(currentUser.AccountId, ct);
        if (accountSnapshot is null)
            return Result<PhoneLookupResult>.Failure(Forbidden);

        if (!userAccessPolicy.IsPermitted(
                userSnapshot.Role,
                userSnapshot.MembershipStatus,
                accountSnapshot.Purpose,
                PermissionKeys.Keep.RequestsOperate))
            return Result<PhoneLookupResult>.Failure(Forbidden);

        var nowUtc = clock.UtcNow;
        var accessContext = new AccountAccessContext(
            accountSnapshot.LifecycleState,
            accountSnapshot.Purpose,
            accountSnapshot.CommercialState,
            accountSnapshot.TrialEndsAtUtc,
            accountSnapshot.PastDueGraceEndsAtUtc,
            accountSnapshot.OperatingMode,
            RequestImplementsAllowedInOffSeason: false,
            nowUtc);

        var decision = accountAccessPolicy.Evaluate(accessContext);
        if (decision.IsBlocked || decision.IsReadOnly)
            return Result<PhoneLookupResult>.Failure(Forbidden);

        if (!featurePolicy.IsEnabled(accountSnapshot.Plan, FeatureKeys.Keep.OperatorQueue))
            return Result<PhoneLookupResult>.Failure(Forbidden);

        // --- Phone normalization ---
        var canonical = PhoneNormalizer.Normalize(rawPhone ?? string.Empty);
        if (!PhoneNormalizer.IsValidLength(canonical))
            return Result<PhoneLookupResult>.Failure(InvalidPhone);

        // --- Lookup ---
        var customer = await businessRequestPersistence.FindCustomerByCanonicalPhoneAsync(
            currentUser.AccountId, canonical, ct);

        const int PageSize = 3;

        if (customer is null)
        {
            // ADR-492: no exact canonical-phone match, but a legacy/unbackfilled request may still
            // carry this phone. That request's KeepCustomerId is a real, tenant-scoped customer —
            // a candidate, not confirmed identity, since its current phone no longer matches.
            var legacyMatch = await businessRequestPersistence.FindMostRecentRequestByCustomerPhoneAsync(
                currentUser.AccountId, canonical, ct);
            if (legacyMatch is null)
                return Result<PhoneLookupResult>.Success(
                    new PhoneLookupResult(null, Array.Empty<PhoneLookupActiveRequest>(), false, null));

            var candidate = await businessRequestPersistence.FindCustomerByIdAsync(
                currentUser.AccountId, legacyMatch.KeepCustomerId, ct);
            if (candidate is null)
                return Result<PhoneLookupResult>.Success(
                    new PhoneLookupResult(null, Array.Empty<PhoneLookupActiveRequest>(), false, null));

            var candidateRows = await businessRequestPersistence.FindActiveRequestsByCustomerIdAsync(
                currentUser.AccountId, candidate.Id, take: PageSize + 1, ct);

            var candidateHasMore = candidateRows.Count > PageSize;
            var candidatePage = candidateHasMore ? candidateRows.Take(PageSize).ToList() : candidateRows;

            var possibleCustomer = new PhoneLookupPossibleCustomer(
                candidate.Id,
                candidate.Name,
                candidate.PrimaryPhone,
                candidate.Email,
                candidatePage.Select(MapActiveRequest).ToList(),
                candidateHasMore);

            return Result<PhoneLookupResult>.Success(
                new PhoneLookupResult(null, Array.Empty<PhoneLookupActiveRequest>(), false, possibleCustomer));
        }

        // Fetch one extra to detect hasMoreActiveRequests without a separate count query.
        var rows = await businessRequestPersistence.FindActiveRequestsByCustomerIdAsync(
            currentUser.AccountId, customer.Id, take: PageSize + 1, ct);

        var hasMore = rows.Count > PageSize;
        var page = hasMore ? rows.Take(PageSize).ToList() : rows;

        var lookupCustomer = new PhoneLookupCustomer(customer.Name, customer.PrimaryPhone, customer.Email);
        var activeRequests = page.Select(MapActiveRequest).ToList();

        return Result<PhoneLookupResult>.Success(
            new PhoneLookupResult(lookupCustomer, activeRequests, hasMore, null));
    }

    private static PhoneLookupActiveRequest MapActiveRequest(KeepRequest r) =>
        new(r.Id,
            r.ReferenceCode,
            MapStatus(r.Status),
            r.Description,
            r.LastBusinessActivityAt > r.LastCustomerActivityAt
                ? r.LastBusinessActivityAt
                : r.LastCustomerActivityAt ?? r.LastBusinessActivityAt ?? (DateTime?)r.CreatedAtUtc);

    private static string MapStatus(KeepRequestStatus status) => status switch
    {
        KeepRequestStatus.Received        => "received",
        KeepRequestStatus.Scheduled       => "scheduled",
        KeepRequestStatus.InProgress      => "in_progress",
        KeepRequestStatus.PendingCustomer => "pending_customer",
        KeepRequestStatus.Resolved        => "resolved",
        KeepRequestStatus.Closed          => "closed",
        KeepRequestStatus.Cancelled       => "cancelled",
        KeepRequestStatus.Spam            => "spam",
        KeepRequestStatus.Test            => "test",
        _ => throw new InvalidOperationException($"Unknown KeepRequestStatus: {status}")
    };
}

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

public sealed record PhoneLookupPrefill(string Name, string? Email);

public sealed record PhoneLookupActiveRequest(
    Guid RequestId,
    string ReferenceCode,
    string Status,
    string Description,
    DateTime? LastActivityAtUtc);

public sealed record PhoneLookupResult(
    PhoneLookupCustomer? Customer,
    PhoneLookupPrefill? Prefill,
    IReadOnlyList<PhoneLookupActiveRequest> ActiveRequests,
    bool HasMoreActiveRequests);

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

        if (customer is null)
        {
            // GAP-025: no KeepCustomer row yet, but legacy/unbackfilled requests may still carry
            // this phone. Fall back to a read-only prefill so Quick Capture doesn't open blank —
            // never creates or links a KeepCustomer here.
            var legacyMatch = await businessRequestPersistence.FindMostRecentRequestByCustomerPhoneAsync(
                currentUser.AccountId, canonical, ct);

            var prefill = legacyMatch is null
                ? null
                : new PhoneLookupPrefill(legacyMatch.CustomerName, legacyMatch.CustomerEmail);

            return Result<PhoneLookupResult>.Success(
                new PhoneLookupResult(null, prefill, Array.Empty<PhoneLookupActiveRequest>(), false));
        }

        // Fetch one extra to detect hasMoreActiveRequests without a separate count query.
        const int PageSize = 3;
        var rows = await businessRequestPersistence.FindActiveRequestsByCustomerIdAsync(
            currentUser.AccountId, customer.Id, take: PageSize + 1, ct);

        var hasMore = rows.Count > PageSize;
        var page = hasMore ? rows.Take(PageSize).ToList() : rows;

        var lookupCustomer = new PhoneLookupCustomer(customer.Name, customer.PrimaryPhone, customer.Email);
        var activeRequests = page.Select(MapActiveRequest).ToList();

        return Result<PhoneLookupResult>.Success(
            new PhoneLookupResult(lookupCustomer, null, activeRequests, hasMore));
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

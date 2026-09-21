using System.Globalization;
using OpHalo.Foundation.Application.Abstractions.Security;
using OpHalo.Foundation.Application.Accounts.Access;
using OpHalo.Foundation.Application.Accounts.Authorization;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;
using OpHalo.Keep.Core.Errors;
using OpHalo.SharedKernel.Abstractions;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Keep.Application.Setup;

public sealed class KeepSetupService(
    IKeepSetupPersistence persistence,
    ICurrentUser currentUser,
    IUserAccessPolicy userAccessPolicy,
    IAccountAccessPolicy accountAccessPolicy,
    IClock clock,
    KeepResponsePolicyService responsePolicyService)
{
    private static readonly Error Unauthorized =
        Error.Create("auth.unauthorized", "Authentication required.");

    private static readonly Error Forbidden =
        Error.Create("auth.forbidden", "You do not have permission to perform this action.");

    private static readonly Error CalendarValidation = Error.Create(
        "KeepSetup.CalendarValidation",
        "Calendar must list weekly intervals as weekday names with HH:mm times and closures as YYYY-MM-DD dates.");

    private const int MaxClosureReasonLength = 60;

    private static readonly Error ClosureReasonValidation = Error.Create(
        "KeepSetup.ClosureReasonValidation",
        "A closure reason must be a single line of at most 60 characters.");

    private static readonly Error PolicyValidation = Error.Create(
        "KeepSetup.PolicyValidation",
        "Policy durations must be positive and timing bases must be Continuous or StaffedHours.");

    // Canonical unsaved-policy defaults (ADR-505) — returned when no policy row exists yet.
    private const int DefaultFirstResponseTargetMinutes = KeepResponsePolicyDefaults.FirstResponseTargetMinutes;
    private const int DefaultStandardResponseTargetMinutes = KeepResponsePolicyDefaults.StandardResponseTargetMinutes;
    private const int DefaultPriorityResponseTargetMinutes = KeepResponsePolicyDefaults.PriorityResponseTargetMinutes;
    private const int DefaultStatusCheckThresholdDays = 5;

    public async Task<Result<KeepSetupResult>> GetSetupAsync(CancellationToken ct = default)
    {
        var auth = await AuthorizeAsync(ct);
        if (auth.IsFailure) return Result<KeepSetupResult>.Failure(auth.Error);

        var (account, profile) = await persistence.GetProfileDataAsync(currentUser.AccountId, ct);
        var policy = await persistence.GetPolicyAsync(currentUser.AccountId, ct);

        return Result<KeepSetupResult>.Success(await BuildResultAsync(account, profile, policy, ct));
    }

    public async Task<Result<KeepSetupResult>> UpdateProfileAsync(
        string businessName,
        string timeZone,
        string? customerFacingPhone,
        string? customerFacingEmail,
        string? logoUrl,
        string? websiteUrl,
        string? expectedSettingsVersion,
        CancellationToken ct = default)
    {
        var auth = await AuthorizeAsync(ct);
        if (auth.IsFailure) return Result<KeepSetupResult>.Failure(auth.Error);

        var (account, existingProfile) = await persistence.GetProfileDataAsync(currentUser.AccountId, ct);

        // Validate and stage every profile field EXCEPT timezone first: UpdateProfile is
        // re-supplied the account's current, unchanged timezone so it validates/applies only
        // BusinessName here. The requested timezone value is applied later, inside
        // SaveProfileWithTimeZoneAsync's single transaction, only once its own governance
        // (IANA validation, staffed-hours reachability re-preflight) has approved it — so a
        // rejected timezone change can never leave BusinessName/profile saved without it, or
        // vice versa (ADR-505 batch 4c).
        var updateResult = account.UpdateProfile(businessName, account.TimeZone);
        if (updateResult.IsFailure) return Result<KeepSetupResult>.Failure(updateResult.Error);

        var profile = existingProfile ?? KeepBusinessProfile.Create(currentUser.AccountId);
        profile.UpdateContact(customerFacingPhone, customerFacingEmail);

        var identityResult = profile.UpdatePublicIdentity(logoUrl, websiteUrl);
        if (identityResult.IsFailure) return Result<KeepSetupResult>.Failure(identityResult.Error);

        var actorDisplayName = await persistence.GetActorDisplayNameAsync(currentUser.UserId, ct);
        if (actorDisplayName is null) return Result<KeepSetupResult>.Failure(Forbidden);

        var profileEvent = KeepProductOpsEvent.Record(
            currentUser.AccountId, KeepProductOpsEventType.ProfileAndContactSaved, clock.UtcNow);
        var saveResult = await persistence.SaveProfileWithTimeZoneAsync(
            account, profile, profileEvent, currentUser.UserId, actorDisplayName, timeZone, expectedSettingsVersion, clock.UtcNow, ct);
        if (saveResult.IsFailure) return Result<KeepSetupResult>.Failure(saveResult.Error);

        var policy = await persistence.GetPolicyAsync(currentUser.AccountId, ct);

        return Result<KeepSetupResult>.Success(await BuildResultAsync(account, profile, policy, ct));
    }

    public async Task<Result<KeepSetupResult>> UpdatePolicyAsync(
        int firstResponseTargetMinutes,
        int standardResponseTargetMinutes,
        int priorityResponseTargetMinutes,
        int statusCheckThresholdDays,
        string? firstResponseTimingBasis,
        string? standardResponseTimingBasis,
        string? priorityResponseTimingBasis,
        string? expectedSettingsVersion,
        CancellationToken ct = default)
    {
        var auth = await AuthorizeAsync(ct);
        if (auth.IsFailure) return Result<KeepSetupResult>.Failure(auth.Error);

        // Advisory boundary pre-check: the domain stays authoritative, but its ArgumentException
        // would otherwise escape the governed path as an HTTP 500.
        if (firstResponseTargetMinutes <= 0
            || standardResponseTargetMinutes <= 0
            || priorityResponseTargetMinutes <= 0
            || statusCheckThresholdDays <= 0)
            return Result<KeepSetupResult>.Failure(PolicyValidation);

        if (!TryParseTimingBasis(firstResponseTimingBasis, out var first)
            || !TryParseTimingBasis(standardResponseTimingBasis, out var standard)
            || !TryParseTimingBasis(priorityResponseTimingBasis, out var priority))
            return Result<KeepSetupResult>.Failure(PolicyValidation);

        var write = await responsePolicyService.UpdatePolicyTargetsAsync(
            firstResponseTargetMinutes,
            standardResponseTargetMinutes,
            priorityResponseTargetMinutes,
            statusCheckThresholdDays,
            first,
            standard,
            priority,
            expectedSettingsVersion,
            ct);
        if (write.IsFailure) return Result<KeepSetupResult>.Failure(write.Error);

        return await GetSetupAsync(ct);
    }

    // Omitted (null) preserves the persisted basis (ADR-506 rollout); anything else must be an
    // explicit known value — an unknown string is a request-shape error, never a silent default.
    private static bool TryParseTimingBasis(string? value, out ResponseTimingBasis? basis)
    {
        basis = null;
        if (value is null) return true;
        if (string.Equals(value, nameof(ResponseTimingBasis.Continuous), StringComparison.OrdinalIgnoreCase))
            basis = ResponseTimingBasis.Continuous;
        else if (string.Equals(value, nameof(ResponseTimingBasis.StaffedHours), StringComparison.OrdinalIgnoreCase))
            basis = ResponseTimingBasis.StaffedHours;
        return basis is not null;
    }

    /// <summary>
    /// Full-snapshot calendar save (ADR-506): parses the desired weekly intervals and closure
    /// dates, diffs closures against the stored calendar, and delegates to the governed policy
    /// service, which owns authorization, the version check, and validation. Missing or stale
    /// <paramref name="expectedSettingsVersion"/> is passed through unchanged.
    /// </summary>
    public async Task<Result<KeepSetupResult>> UpdateCalendarAsync(
        IReadOnlyList<KeepSetupWeeklyIntervalInput>? weeklyIntervals,
        IReadOnlyList<KeepSetupClosureInput>? closures,
        string? expectedSettingsVersion,
        CancellationToken ct = default)
    {
        var parsed = ParseCalendar(weeklyIntervals, closures);
        if (parsed.IsFailure) return Result<KeepSetupResult>.Failure(parsed.Error);
        var (intervals, desiredClosures) = parsed.Value;

        // Authorize before reading stored state so an unauthorized caller learns nothing.
        var auth = await AuthorizeAsync(ct);
        if (auth.IsFailure) return Result<KeepSetupResult>.Failure(auth.Error);

        if (desiredClosures.Select(c => c.Date).Distinct().Count() != desiredClosures.Count)
            return Result<KeepSetupResult>.Failure(KeepResponsePolicyErrors.DuplicateClosureDate);

        var current = await persistence.GetCalendarAsync(currentUser.AccountId, ct);
        var currentByDate = current.Closures.ToDictionary(c => c.Date);
        var desiredDates = desiredClosures.Select(c => c.Date).ToHashSet();
        // Only new dates and changed reasons are sent, so a no-op stays silent (ADR-507).
        var toSet = desiredClosures
            .Where(c => !currentByDate.TryGetValue(c.Date, out var stored)
                        || !string.Equals(stored.Reason, c.Reason, StringComparison.Ordinal))
            .ToList();
        var toRemove = current.Closures.Select(c => c.Date).Where(d => !desiredDates.Contains(d)).ToList();

        var write = await responsePolicyService.UpdateCalendarAsync(
            intervals, toSet, toRemove, expectedSettingsVersion, ct);
        if (write.IsFailure) return Result<KeepSetupResult>.Failure(write.Error);

        return await GetSetupAsync(ct);
    }

    private static Result<(List<(DayOfWeek Weekday, TimeOnly OpensAt, TimeOnly ClosesAt)> Intervals, List<KeepCalendarClosureSnapshot> Closures)>
        ParseCalendar(IReadOnlyList<KeepSetupWeeklyIntervalInput>? weeklyIntervals, IReadOnlyList<KeepSetupClosureInput>? closureInputs)
    {
        var invalid = Result<(List<(DayOfWeek, TimeOnly, TimeOnly)>, List<KeepCalendarClosureSnapshot>)>.Failure(CalendarValidation);
        if (weeklyIntervals is null || closureInputs is null) return invalid;

        var intervals = new List<(DayOfWeek, TimeOnly, TimeOnly)>();
        foreach (var i in weeklyIntervals)
        {
            if (i is null
                || string.IsNullOrWhiteSpace(i.Weekday)
                || char.IsDigit(i.Weekday.Trim()[0])
                || !Enum.TryParse<DayOfWeek>(i.Weekday.Trim(), ignoreCase: true, out var weekday)
                || !TimeOnly.TryParseExact(i.OpensAt, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var opens)
                || !TimeOnly.TryParseExact(i.ClosesAt, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var closes))
                return invalid;
            intervals.Add((weekday, opens, closes));
        }

        var closures = new List<KeepCalendarClosureSnapshot>();
        foreach (var c in closureInputs)
        {
            if (c is null
                || !DateOnly.TryParseExact(c.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                return invalid;

            // ADR-507: trim, empty -> null, else single line, no control characters, UTF-16 length <= 60.
            var reason = c.Reason?.Trim();
            if (string.IsNullOrEmpty(reason)) reason = null;
            else if (reason.Length > MaxClosureReasonLength || reason.Any(char.IsControl))
                return Result<(List<(DayOfWeek, TimeOnly, TimeOnly)>, List<KeepCalendarClosureSnapshot>)>.Failure(ClosureReasonValidation);

            closures.Add(new KeepCalendarClosureSnapshot(date, reason));
        }

        return Result<(List<(DayOfWeek, TimeOnly, TimeOnly)>, List<KeepCalendarClosureSnapshot>)>.Success((intervals, closures));
    }

    private async Task<KeepSetupResult> BuildResultAsync(
        Account account, KeepBusinessProfile? profile, KeepResponsePolicy? policy, CancellationToken ct)
    {
        var calendar = await persistence.GetCalendarAsync(currentUser.AccountId, ct);

        return new KeepSetupResult(
            BusinessName: account.BusinessName,
            TimeZone: account.TimeZone,
            CustomerFacingPhone: profile?.CustomerFacingPhone,
            CustomerFacingEmail: profile?.CustomerFacingEmail,
            LogoUrl: profile?.LogoUrl,
            WebsiteUrl: profile?.WebsiteUrl,
            ResponsePolicy: ToPolicy(policy),
            Calendar: ToCalendar(calendar),
            SettingsVersion: KeepSettingsVersion.Compute(account.TimeZone, policy, calendar));
    }

    private static KeepSetupPolicyResult ToPolicy(KeepResponsePolicy? policy) =>
        policy is null
            ? new KeepSetupPolicyResult(
                DefaultFirstResponseTargetMinutes,
                DefaultStandardResponseTargetMinutes,
                DefaultPriorityResponseTargetMinutes,
                DefaultStatusCheckThresholdDays,
                nameof(ResponseTimingBasis.Continuous),
                nameof(ResponseTimingBasis.Continuous),
                nameof(ResponseTimingBasis.Continuous))
            : new KeepSetupPolicyResult(
                policy.FirstResponseTargetMinutes,
                policy.StandardResponseTargetMinutes,
                policy.PriorityResponseTargetMinutes,
                policy.StatusCheckThresholdDays,
                policy.FirstResponseTimingBasis.ToString(),
                policy.StandardResponseTimingBasis.ToString(),
                policy.PriorityResponseTimingBasis.ToString());

    private static KeepSetupCalendarResult ToCalendar(KeepCalendarSnapshot calendar) =>
        new(
            calendar.WeeklyIntervals
                .OrderBy(i => i.Weekday)
                .Select(i => new KeepSetupWeeklyIntervalResult(
                    i.Weekday.ToString(),
                    i.OpensAt.ToString("HH:mm", CultureInfo.InvariantCulture),
                    i.ClosesAt.ToString("HH:mm", CultureInfo.InvariantCulture)))
                .ToList(),
            calendar.Closures
                .OrderBy(c => c.Date)
                .Select(c => new KeepSetupClosureResult(
                    c.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), c.Reason))
                .ToList());

    private async Task<Result> AuthorizeAsync(CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result.Failure(Unauthorized);

        var userSnapshot = await persistence.GetAccountUserSnapshotAsync(currentUser.UserId, ct);
        if (userSnapshot is null)
            return Result.Failure(Forbidden);

        var accountSnapshot = await persistence.GetAccountAccessSnapshotAsync(currentUser.AccountId, ct);
        if (accountSnapshot is null)
            return Result.Failure(Forbidden);

        if (!userAccessPolicy.IsPermitted(
                userSnapshot.Role,
                userSnapshot.MembershipStatus,
                accountSnapshot.Purpose,
                PermissionKeys.Keep.SettingsManage))
            return Result.Failure(Forbidden);

        var nowUtc = clock.UtcNow;
        var accessContext = new AccountAccessContext(
            accountSnapshot.LifecycleState,
            accountSnapshot.Purpose,
            accountSnapshot.CommercialState,
            accountSnapshot.TrialEndsAtUtc,
            accountSnapshot.PastDueGraceEndsAtUtc,
            accountSnapshot.OperatingMode,
            RequestImplementsAllowedInOffSeason: true,
            nowUtc);

        var decision = accountAccessPolicy.Evaluate(accessContext);
        if (decision.IsBlocked)
            return Result.Failure(Forbidden);

        return Result.Success();
    }
}

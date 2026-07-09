using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using OpHalo.Foundation.Application.Abstractions.Security;
using OpHalo.Foundation.Application.Accounts.Access;
using OpHalo.Foundation.Application.Accounts.Authorization;
using OpHalo.Foundation.Application.Accounts.Entitlements;
using OpHalo.Keep.Application.Services;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Errors;
using OpHalo.SharedKernel.Abstractions;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Keep.Application.IntakeSetup;

public sealed class KeepIntakeSetupService(
    IKeepIntakeSetupPersistence persistence,
    KeepTokenService tokenService,
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

    private static readonly Error SlugExhausted =
        Error.Create("KeepPublicIntakeLink.SlugCollision",
            "Unable to generate a unique intake link; please retry.");

    private static readonly Error NoActiveLink = KeepPublicIntakeLinkErrors.NoActiveLink;

    public async Task<Result<KeepIntakeSetupStatusResult>> GetStatusAsync(CancellationToken ct = default)
    {
        var auth = await AuthorizeAsync(ct);
        if (auth.IsFailure)
            return Result<KeepIntakeSetupStatusResult>.Failure(auth.Error);

        var link = await persistence.FindActiveLinkByAccountAsync(currentUser.AccountId, ct);

        return Result<KeepIntakeSetupStatusResult>.Success(new KeepIntakeSetupStatusResult(
            HasActiveLink: link is not null,
            PublicSlug: link?.PublicSlug,
            CreatedAtUtc: link?.CreatedAtUtc));
    }

    public async Task<Result<KeepIntakeSetupEnsureResult>> EnsureAsync(CancellationToken ct = default)
    {
        var auth = await AuthorizeAsync(ct);
        if (auth.IsFailure)
            return Result<KeepIntakeSetupEnsureResult>.Failure(auth.Error);

        // Fast path: active link already exists — return without issuing a new token.
        var existing = await persistence.FindActiveLinkByAccountAsync(currentUser.AccountId, ct);
        if (existing is not null)
            return Result<KeepIntakeSetupEnsureResult>.Success(
                new KeepIntakeSetupEnsureResult(Created: false, RawToken: null, PublicSlug: existing.PublicSlug));

        var businessName = await persistence.GetAccountBusinessNameAsync(currentUser.AccountId, ct);
        var slugBase = Slugify(businessName ?? "business");

        const int maxAttempts = 5;
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            var slug = await GenerateUniqueSlugAsync(slugBase, ct);
            var rawToken = tokenService.GeneratePublicIntakeToken();
            var tokenHash = tokenService.HashPublicIntakeToken(rawToken);
            var link = KeepPublicIntakeLink.Create(currentUser.AccountId, slug, tokenHash, currentUser.UserId);

            var commitResult = await persistence.CommitEnsureAsync(link, ct);

            switch (commitResult)
            {
                case EnsureIntakeLinkCommitResult.Created:
                    return Result<KeepIntakeSetupEnsureResult>.Success(
                        new KeepIntakeSetupEnsureResult(Created: true, RawToken: rawToken, PublicSlug: slug));

                case EnsureIntakeLinkCommitResult.AlreadyExists:
                    // Concurrent caller won the race — re-read their link, do not issue a token.
                    var winner = await persistence.FindActiveLinkByAccountAsync(currentUser.AccountId, ct);
                    return Result<KeepIntakeSetupEnsureResult>.Success(
                        new KeepIntakeSetupEnsureResult(Created: false, RawToken: null,
                            PublicSlug: winner?.PublicSlug));

                case EnsureIntakeLinkCommitResult.SlugCollision:
                    // Another account claimed this slug; retry with a new suffix.
                    continue;
            }
        }

        return Result<KeepIntakeSetupEnsureResult>.Failure(SlugExhausted);
    }

    public async Task<Result<KeepIntakeSetupReplaceResult>> ReplaceAsync(CancellationToken ct = default)
    {
        var auth = await AuthorizeAsync(ct);
        if (auth.IsFailure)
            return Result<KeepIntakeSetupReplaceResult>.Failure(auth.Error);

        var existing = await persistence.FindActiveLinkByAccountAsync(currentUser.AccountId, ct);
        if (existing is null)
            return Result<KeepIntakeSetupReplaceResult>.Failure(KeepPublicIntakeLinkErrors.NoActiveLink);

        var nowUtc = clock.UtcNow;
        var revokeResult = existing.Revoke(nowUtc, currentUser.UserId);
        if (revokeResult.IsFailure)
            return Result<KeepIntakeSetupReplaceResult>.Failure(revokeResult.Error);

        var businessName = await persistence.GetAccountBusinessNameAsync(currentUser.AccountId, ct);
        var slugBase = Slugify(businessName ?? "business");
        var slug = await GenerateUniqueSlugAsync(slugBase, ct);

        var rawToken = tokenService.GeneratePublicIntakeToken();
        var tokenHash = tokenService.HashPublicIntakeToken(rawToken);
        var newLink = KeepPublicIntakeLink.Create(currentUser.AccountId, slug, tokenHash, currentUser.UserId);

        await persistence.CommitReplaceAsync(existing, newLink, ct);

        return Result<KeepIntakeSetupReplaceResult>.Success(
            new KeepIntakeSetupReplaceResult(RawToken: rawToken, PublicSlug: slug, StaleLinksWarning: true));
    }

    public async Task<Result<KeepIntakeSetupRenameResult>> RenameAsync(string desiredName, CancellationToken ct = default)
    {
        var auth = await AuthorizeAsync(ct);
        if (auth.IsFailure)
            return Result<KeepIntakeSetupRenameResult>.Failure(auth.Error);

        var link = await persistence.FindActiveLinkByAccountAsync(currentUser.AccountId, ct);
        if (link is null)
            return Result<KeepIntakeSetupRenameResult>.Failure(NoActiveLink);

        var newSlug = Slugify(desiredName);

        // No-op: desired name normalizes to the current slug — no rename, no alias needed.
        if (string.Equals(link.PublicSlug, newSlug, StringComparison.OrdinalIgnoreCase))
            return Result<KeepIntakeSetupRenameResult>.Success(new KeepIntakeSetupRenameResult(link.PublicSlug));

        // For a rename the user is expressing intent for a specific name; surface conflicts rather
        // than silently appending a suffix (unlike auto-provision flows in EnsureAsync/ReplaceAsync).
        if (await persistence.SlugExistsAsync(newSlug, ct))
            return Result<KeepIntakeSetupRenameResult>.Failure(KeepPublicIntakeLinkErrors.SlugTaken);

        var oldSlug = link.PublicSlug;
        link.RenameSlug(newSlug);
        var alias = KeepPublicIntakeSlugAlias.Create(currentUser.AccountId, link.Id, oldSlug);

        var commitResult = await persistence.CommitRenameAsync(link, alias, ct);
        return commitResult switch
        {
            RenameIntakeLinkCommitResult.Renamed =>
                Result<KeepIntakeSetupRenameResult>.Success(new KeepIntakeSetupRenameResult(newSlug)),
            RenameIntakeLinkCommitResult.SlugCollision =>
                Result<KeepIntakeSetupRenameResult>.Failure(KeepPublicIntakeLinkErrors.SlugTaken),
            _ => throw new InvalidOperationException($"Unexpected RenameIntakeLinkCommitResult: {commitResult}")
        };
    }

    // --- Auth ---

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

        if (!featurePolicy.IsEnabled(accountSnapshot.Plan, FeatureKeys.Keep.PublicIntake))
            return Result.Failure(Forbidden);

        return Result.Success();
    }

    // --- Slug helpers ---

    private static string Slugify(string input)
    {
        // Strip diacritics: NFD decomposition removes combining marks (é → e + ́ → e).
        var decomposed = input.Normalize(NormalizationForm.FormD);
        var stripped = new string(decomposed
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            .ToArray());
        var lower = stripped.ToLowerInvariant();
        // Collapse any run of non-alphanumeric characters into a single hyphen.
        var replaced = Regex.Replace(lower, @"[^a-z0-9]+", "-");
        var trimmed = replaced.Trim('-');
        var result = string.IsNullOrEmpty(trimmed) ? "business" : trimmed;
        return result.Length > 60 ? result[..60].TrimEnd('-') : result;
    }

    private async Task<string> GenerateUniqueSlugAsync(string slugBase, CancellationToken ct)
    {
        if (!await persistence.SlugExistsAsync(slugBase, ct))
            return slugBase;

        for (var i = 2; ; i++)
        {
            var candidate = $"{slugBase}-{i}";
            if (!await persistence.SlugExistsAsync(candidate, ct))
                return candidate;
        }
    }
}

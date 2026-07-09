using OpHalo.Foundation.Core.Entities.Shared;

namespace OpHalo.Keep.Core.Entities;

/// <summary>
/// Records a public-slug that was once active for an intake link (ADR-429).
/// When a link's PublicSlug changes, the old slug is preserved here so
/// previously-shared URLs continue to resolve. A slug is active when
/// RetiredAtUtc is null; only one active alias may exist per slug (enforced
/// by partial unique index ix_keep_public_intake_slug_aliases_active_slug).
/// </summary>
public sealed class KeepPublicIntakeSlugAlias : BaseEntity
{
    public Guid AccountId { get; private set; }
    public Guid IntakeLinkId { get; private set; }
    public string Slug { get; private set; } = string.Empty;
    public DateTime? RetiredAtUtc { get; private set; }

    public bool IsActive => !RetiredAtUtc.HasValue && !IsDeleted;

    public void Retire(DateTime nowUtc) => RetiredAtUtc = nowUtc;

    public static KeepPublicIntakeSlugAlias Create(
        Guid accountId,
        Guid intakeLinkId,
        string slug)
    {
        if (accountId == Guid.Empty)
            throw new ArgumentException("Account ID is required.", nameof(accountId));
        if (intakeLinkId == Guid.Empty)
            throw new ArgumentException("Intake link ID is required.", nameof(intakeLinkId));
        if (string.IsNullOrWhiteSpace(slug))
            throw new ArgumentException("Slug is required.", nameof(slug));

        return new KeepPublicIntakeSlugAlias
        {
            AccountId = accountId,
            IntakeLinkId = intakeLinkId,
            Slug = slug.ToLowerInvariant().Trim()
        };
    }
}

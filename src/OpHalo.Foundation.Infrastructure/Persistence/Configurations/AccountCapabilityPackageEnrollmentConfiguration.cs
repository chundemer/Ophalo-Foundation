using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpHalo.Foundation.Core.Entities.Accounts;

namespace OpHalo.Foundation.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for <see cref="AccountCapabilityPackageEnrollment"/> (ADR-462).
/// </summary>
internal sealed class AccountCapabilityPackageEnrollmentConfiguration
    : BaseEntityConfiguration<AccountCapabilityPackageEnrollment>
{
    protected override void ConfigureEntity(EntityTypeBuilder<AccountCapabilityPackageEnrollment> builder)
    {
        builder.ToTable("account_capability_package_enrollments");

        builder.Property(x => x.AccountId)
            .IsRequired();

        builder.Property(x => x.FeatureKey)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.EnabledAt);

        builder.Property(x => x.DisabledAt);

        builder.Property(x => x.ChangedByAccountUserId)
            .IsRequired();

        // Application-managed opaque uuid token: never database-generated, no default, no
        // trigger, no index. EF includes it in the UPDATE predicate so a stale write maps to
        // DbUpdateConcurrencyException. Same pattern as KeepRequest.ConcurrencyVersion (ADR-330).
        builder.Property(x => x.ConcurrencyVersion)
            .HasColumnType("uuid")
            .IsRequired()
            .IsConcurrencyToken()
            .ValueGeneratedNever();

        // ADR-462: unique on (AccountId, FeatureKey) — one row grants one feature key to one
        // account; prevents a second, conflicting row that would make access resolution ambiguous.
        builder.HasIndex(x => new { x.AccountId, x.FeatureKey })
            .IsUnique();

        // One Account may have many enrollment rows (one per feature key) — not 1:1.
        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(x => x.AccountId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

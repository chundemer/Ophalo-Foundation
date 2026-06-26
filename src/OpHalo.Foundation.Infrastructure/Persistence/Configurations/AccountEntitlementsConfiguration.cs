using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpHalo.Foundation.Core.Entities.Accounts;

namespace OpHalo.Foundation.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for <see cref="AccountEntitlements"/> — the account's commercial
/// and operating posture (Phase 4b). 1:1 with <see cref="Account"/>. The legacy halo
/// booleans, provisioning key, pilot dates, and all Stripe/billing/delinquency columns are
/// dropped (not ported). This entity is exempt from the soft-delete query filter (ADR-025) —
/// see <c>OpHaloDbContext.IsFoundationalSatellite</c>.
/// </summary>
internal sealed class AccountEntitlementsConfiguration : BaseEntityConfiguration<AccountEntitlements>
{
    protected override void ConfigureEntity(EntityTypeBuilder<AccountEntitlements> builder)
    {
        builder.ToTable("account_entitlements");

        builder.Property(x => x.AccountId)
            .IsRequired();

        builder.Property(x => x.Plan)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.CommercialState)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.OperatingMode)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.TrialEndsAtUtc);

        builder.Property(x => x.PastDueGraceEndsAtUtc);

        builder.Property(x => x.MaxUserSeats)
            .IsRequired();

        builder.Property(x => x.Classification)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        // 1:1 with Account. No navigation on this side — the satellite holds only AccountId.
        builder.HasIndex(x => x.AccountId)
            .IsUnique();

        builder.HasOne<Account>()
            .WithOne()
            .HasForeignKey<AccountEntitlements>(x => x.AccountId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

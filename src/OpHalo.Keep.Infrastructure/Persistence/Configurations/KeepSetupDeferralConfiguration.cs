using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Infrastructure.Persistence.Configurations;
using OpHalo.Keep.Core.Entities;

namespace OpHalo.Keep.Infrastructure.Persistence.Configurations;

internal sealed class KeepSetupDeferralConfiguration : BaseEntityConfiguration<KeepSetupDeferral>
{
    protected override void ConfigureEntity(EntityTypeBuilder<KeepSetupDeferral> builder)
    {
        builder.ToTable("keep_setup_deferrals");

        builder.Property(x => x.AccountId).IsRequired();
        builder.Property(x => x.Step).IsRequired();
        builder.Property(x => x.DeferredAtUtc).IsRequired();
        builder.Property(x => x.ClearedAtUtc);
        builder.Property(x => x.DeferredByAccountUserId).IsRequired();

        // One active or cleared deferral row per account/step. Redefer reuses the row.
        builder.HasIndex(x => new { x.AccountId, x.Step })
            .IsUnique()
            .HasDatabaseName("ix_keep_setup_deferrals_account_step");

        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(x => x.AccountId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

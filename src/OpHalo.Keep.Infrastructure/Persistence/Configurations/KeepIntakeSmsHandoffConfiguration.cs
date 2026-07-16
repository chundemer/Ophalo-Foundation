using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Infrastructure.Persistence.Configurations;
using OpHalo.Keep.Core.Entities;

namespace OpHalo.Keep.Infrastructure.Persistence.Configurations;

internal sealed class KeepIntakeSmsHandoffConfiguration : BaseEntityConfiguration<KeepIntakeSmsHandoff>
{
    protected override void ConfigureEntity(EntityTypeBuilder<KeepIntakeSmsHandoff> builder)
    {
        builder.ToTable("keep_intake_sms_handoffs");

        builder.Property(x => x.AccountId)
            .IsRequired();

        builder.Property(x => x.HandoffTokenHash)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(x => x.MessageBody)
            .HasMaxLength(2000)
            .IsRequired();

        builder.Property(x => x.ExpiresAtUtc)
            .IsRequired();

        // Unique index on hash — globally unique; used for point lookups.
        builder.HasIndex(x => x.HandoffTokenHash)
            .IsUnique()
            .HasDatabaseName("ix_keep_intake_sms_handoffs_token_hash");

        // Non-unique index on expiry — supports future cleanup of expired records.
        builder.HasIndex(x => x.ExpiresAtUtc)
            .HasDatabaseName("ix_keep_intake_sms_handoffs_expires_at");

        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(x => x.AccountId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

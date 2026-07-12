using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpHalo.Foundation.Infrastructure.Persistence.Configurations;
using OpHalo.Keep.Core.Entities;

namespace OpHalo.Keep.Infrastructure.Persistence.Configurations;

internal sealed class KeepSmsHandoffConfiguration : BaseEntityConfiguration<KeepSmsHandoff>
{
    protected override void ConfigureEntity(EntityTypeBuilder<KeepSmsHandoff> builder)
    {
        builder.ToTable("keep_sms_handoffs");

        builder.Property(x => x.HandoffTokenHash)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(x => x.RequestId)
            .IsRequired();

        builder.Property(x => x.AccountId)
            .IsRequired();

        builder.Property(x => x.CustomerPhone)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.MessageBody)
            .HasMaxLength(2000)
            .IsRequired();

        builder.Property(x => x.CreatedBy)
            .IsRequired();

        builder.Property(x => x.ExpiresAtUtc)
            .IsRequired();

        // Unique index on hash — token must be globally unique; used for point lookups.
        builder.HasIndex(x => x.HandoffTokenHash)
            .IsUnique();

        // Non-unique index on expiry — supports future cleanup of expired records.
        builder.HasIndex(x => x.ExpiresAtUtc);
    }
}

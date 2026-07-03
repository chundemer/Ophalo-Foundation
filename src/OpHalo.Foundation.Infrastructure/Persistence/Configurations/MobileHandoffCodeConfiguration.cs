using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpHalo.Foundation.Core.Entities.Accounts;

namespace OpHalo.Foundation.Infrastructure.Persistence.Configurations;

internal sealed class MobileHandoffCodeConfiguration : IEntityTypeConfiguration<MobileHandoffCode>
{
    public void Configure(EntityTypeBuilder<MobileHandoffCode> builder)
    {
        builder.ToTable("mobile_handoff_codes");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.CodeHash)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(x => x.AccountId).IsRequired();
        builder.Property(x => x.AccountUserId).IsRequired();

        builder.Property(x => x.ClientType)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.IssuedAtUtc).IsRequired();
        builder.Property(x => x.ExpiresAtUtc).IsRequired();
        builder.Property(x => x.ConsumedAtUtc);

        builder.Ignore(x => x.IsConsumed);

        builder.HasIndex(x => x.CodeHash)
            .IsUnique()
            .HasDatabaseName("ix_mobile_handoff_codes_code_hash");

        builder.HasIndex(x => x.ExpiresAtUtc)
            .HasDatabaseName("ix_mobile_handoff_codes_expires_at_utc");

        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(x => x.AccountId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

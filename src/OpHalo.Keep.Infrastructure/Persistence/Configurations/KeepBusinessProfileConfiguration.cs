using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Infrastructure.Persistence.Configurations;
using OpHalo.Keep.Core.Entities;

namespace OpHalo.Keep.Infrastructure.Persistence.Configurations;

internal sealed class KeepBusinessProfileConfiguration : BaseEntityConfiguration<KeepBusinessProfile>
{
    protected override void ConfigureEntity(EntityTypeBuilder<KeepBusinessProfile> builder)
    {
        builder.ToTable("keep_business_profiles");

        builder.Property(x => x.AccountId)
            .IsRequired();

        builder.Property(x => x.CustomerFacingPhone)
            .HasMaxLength(50);

        builder.Property(x => x.CustomerFacingEmail)
            .HasMaxLength(256);

        builder.Property(x => x.LogoUrl)
            .HasMaxLength(2048);

        builder.Property(x => x.WebsiteUrl)
            .HasMaxLength(2048);

        builder.HasIndex(x => x.AccountId)
            .IsUnique()
            .HasDatabaseName("ix_keep_business_profiles_account_id");

        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(x => x.AccountId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

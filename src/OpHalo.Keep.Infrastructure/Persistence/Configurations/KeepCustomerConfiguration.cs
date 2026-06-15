using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpHalo.Foundation.Infrastructure.Persistence.Configurations;
using OpHalo.Keep.Core.Entities;

namespace OpHalo.Keep.Infrastructure.Persistence.Configurations;

internal sealed class KeepCustomerConfiguration : BaseEntityConfiguration<KeepCustomer>
{
    protected override void ConfigureEntity(EntityTypeBuilder<KeepCustomer> builder)
    {
        builder.ToTable("keep_customers");

        builder.Property(x => x.AccountId)
            .IsRequired();

        builder.Property(x => x.Name)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.PrimaryPhone)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.Email)
            .HasMaxLength(320);

        // Identity constraint: same customer calling the same account on the same number
        // always resolves to the same row.
        builder.HasIndex(x => new { x.AccountId, x.PrimaryPhone })
            .IsUnique()
            .HasDatabaseName("ix_keep_customers_account_phone");
    }
}

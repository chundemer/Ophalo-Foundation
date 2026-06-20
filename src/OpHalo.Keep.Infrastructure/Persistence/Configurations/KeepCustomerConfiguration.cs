using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpHalo.Foundation.Core.Entities.Accounts;
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

        // Digit-only canonical identity form — used for account-scoped uniqueness (GAP-006).
        // Max 15 digits matches the E.164 maximum and the domain validation bound.
        builder.Property(x => x.CanonicalPhone)
            .HasMaxLength(15)
            .IsRequired();

        builder.Property(x => x.Email)
            .HasMaxLength(320);

        // Identity constraint: canonical phone is the account-scoped unique identity.
        builder.HasIndex(x => new { x.AccountId, x.CanonicalPhone })
            .IsUnique()
            .HasDatabaseName("ix_keep_customers_account_canonical_phone");

        // Alternate key — allows KeepRequest to hold a composite (AccountId, KeepCustomerId) FK
        // that prevents cross-account customer references at the database level.
        builder.HasAlternateKey(x => new { x.AccountId, x.Id })
            .HasName("ak_keep_customers_account_id");

        // FK — restricts deletion of an account that has customers.
        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(x => x.AccountId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

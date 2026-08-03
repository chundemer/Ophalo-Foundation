using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Infrastructure.Persistence.Configurations;
using OpHalo.Keep.Core.Entities;

namespace OpHalo.Keep.Infrastructure.Persistence.Configurations;

internal sealed class PriceBookAccountStateConfiguration : BaseEntityConfiguration<PriceBookAccountState>
{
    protected override void ConfigureEntity(EntityTypeBuilder<PriceBookAccountState> builder)
    {
        builder.ToTable("keep_pricebook_account_states");

        builder.Property(x => x.AccountId)
            .IsRequired();

        // Application-managed opaque uuid token: never database-generated, no default, no
        // trigger. EF includes it in the UPDATE predicate so a stale write maps to
        // DbUpdateConcurrencyException — the account-scoped publish lock (ADR-470).
        builder.Property(x => x.PublishLockVersion)
            .HasColumnType("uuid")
            .IsRequired()
            .IsConcurrencyToken()
            .ValueGeneratedNever();

        // ADR-470: at most one lock row per account.
        builder.HasIndex(x => x.AccountId)
            .IsUnique()
            .HasDatabaseName("ix_keep_pricebook_account_states_account_id");

        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(x => x.AccountId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

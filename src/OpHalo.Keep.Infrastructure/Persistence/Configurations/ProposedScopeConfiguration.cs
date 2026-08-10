using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Infrastructure.Persistence.Configurations;
using OpHalo.Keep.Core.Entities;

namespace OpHalo.Keep.Infrastructure.Persistence.Configurations;

internal sealed class ProposedScopeConfiguration : BaseEntityConfiguration<ProposedScope>
{
    protected override void ConfigureEntity(EntityTypeBuilder<ProposedScope> builder)
    {
        builder.ToTable("keep_pricebook_proposed_scopes");

        builder.Property(x => x.AccountId)
            .IsRequired();

        builder.Property(x => x.RequestId)
            .IsRequired();

        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        // Application-managed opaque uuid token: never database-generated, no default, no
        // trigger. EF includes it in the UPDATE predicate so a stale write maps to
        // DbUpdateConcurrencyException. Same pattern as OfferingAssembly.ConcurrencyVersion (ADR-330).
        builder.Property(x => x.ConcurrencyVersion)
            .HasColumnType("uuid")
            .IsRequired()
            .IsConcurrencyToken()
            .ValueGeneratedNever();

        // build-log/108: at most one open Draft scope per request at a time — a later field visit
        // after OfficeReviewed creates a new row (ADR-464) rather than reopening this one.
        builder.HasIndex(x => x.RequestId)
            .IsUnique()
            .HasFilter("status = 'Draft'")
            .HasDatabaseName("ux_keep_pricebook_proposed_scopes_open_draft");

        builder.HasIndex(x => new { x.AccountId, x.RequestId });

        // Alternate key — supports the composite FK from ProposedScopeLine.ProposedScopeId, which
        // must reference (AccountId, Id) to prevent cross-account line assignment (matches the
        // pattern established for OfferingAssembly/OfferingAssemblyItem).
        builder.HasAlternateKey(x => new { x.AccountId, x.Id })
            .HasName("ak_keep_pricebook_proposed_scopes_account_id");

        // Lines is exposed as a read-only collection backed by a private field with no public
        // Add — EF must mutate through the field rather than a settable property/collection API.
        builder.Navigation(x => x.Lines)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(x => x.AccountId)
            .OnDelete(DeleteBehavior.Restrict);

        // Composite FK to KeepRequest(AccountId, Id) — prevents a scope referencing a request from
        // a different account; account-safe at the database level, no post-load tenant check.
        // Restrict: a request referenced by a scope is never silently orphaned by deletion (Keep
        // requests are not hard-deleted in the first place).
        builder.HasOne<KeepRequest>()
            .WithMany()
            .HasForeignKey(x => new { x.AccountId, x.RequestId })
            .HasPrincipalKey(r => new { r.AccountId, r.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

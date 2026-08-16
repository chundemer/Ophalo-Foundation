using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpHalo.Foundation.Infrastructure.Persistence.Configurations;
using OpHalo.Keep.Core.Entities;

namespace OpHalo.Keep.Infrastructure.Persistence.Configurations;

internal sealed class ScopeNudgeSuggestionConfiguration : BaseEntityConfiguration<ScopeNudgeSuggestion>
{
    protected override void ConfigureEntity(EntityTypeBuilder<ScopeNudgeSuggestion> builder)
    {
        builder.ToTable("keep_pricebook_scope_nudge_suggestions", t => t.HasCheckConstraint(
            "ck_keep_pricebook_scope_nudge_suggestions_exclusive_target",
            "(suggested_catalog_item_id IS NOT NULL AND suggested_offering_assembly_id IS NULL) OR " +
            "(suggested_catalog_item_id IS NULL AND suggested_offering_assembly_id IS NOT NULL)"));

        builder.Property(x => x.AccountId)
            .IsRequired();

        builder.Property(x => x.ScopeNudgeRuleId)
            .IsRequired();

        builder.Property(x => x.Order)
            .IsRequired();

        builder.Property(x => x.SuggestedCatalogItemId);

        builder.Property(x => x.SuggestedOfferingAssemblyId);

        builder.HasIndex(x => new { x.ScopeNudgeRuleId, x.Order })
            .IsUnique()
            .HasDatabaseName("ux_keep_pricebook_scope_nudge_suggestions_rule_order");

        // Composite FK to ScopeNudgeRule(AccountId, Id) rather than ScopeNudgeRuleId alone —
        // otherwise a malformed direct write could pair a rule from Account A with a suggestion
        // carrying AccountId from Account B, and the target composite FKs below would then validate
        // against B's target, breaking the aggregate's tenant boundary (locked correction,
        // build-log/122/123). Cascade: suggestions are component rows owned by the rule, matching
        // OfferingAssemblyItem's FK to its owning OfferingAssembly. True database-level Cascade (not
        // ClientCascade) — EfScopeNudgeRulePersistence.DeleteAsync issues a bulk ExecuteDeleteAsync
        // with no children loaded into the change tracker, so the cascade must happen in the
        // database, not in EF's in-memory graph.
        builder.HasOne<ScopeNudgeRule>()
            .WithMany(x => x.Suggestions)
            .HasForeignKey(x => new { x.AccountId, x.ScopeNudgeRuleId })
            .HasPrincipalKey(r => new { r.AccountId, r.Id })
            .OnDelete(DeleteBehavior.Cascade);

        // Composite FK to CatalogItem(AccountId, Id) — prevents a suggestion targeting a catalog
        // item in a different account. Stays nullable: null for an assembly suggestion. Restrict
        // (build-log/122/123): neither CatalogItem nor OfferingAssembly exposes a hard-delete path.
        builder.HasOne<CatalogItem>()
            .WithMany()
            .HasForeignKey(x => new { x.AccountId, x.SuggestedCatalogItemId })
            .HasPrincipalKey(c => new { c.AccountId, c.Id })
            .OnDelete(DeleteBehavior.Restrict);

        // Composite FK to OfferingAssembly(AccountId, Id) — same cross-account guard for an
        // assembly suggestion. Stays nullable: null for a catalog-item suggestion.
        builder.HasOne<OfferingAssembly>()
            .WithMany()
            .HasForeignKey(x => new { x.AccountId, x.SuggestedOfferingAssemblyId })
            .HasPrincipalKey(a => new { a.AccountId, a.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

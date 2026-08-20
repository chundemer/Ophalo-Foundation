using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Infrastructure.Persistence.Configurations;
using OpHalo.Keep.Core.Entities;

namespace OpHalo.Keep.Infrastructure.Persistence.Configurations;

internal sealed class ActualWorkNudgeRuleConfiguration : BaseEntityConfiguration<ActualWorkNudgeRule>
{
    protected override void ConfigureEntity(EntityTypeBuilder<ActualWorkNudgeRule> builder)
    {
        builder.ToTable("keep_pricebook_actual_work_nudge_rules", t => t.HasCheckConstraint(
            "ck_keep_pricebook_actual_work_nudge_rules_exclusive_trigger",
            "(trigger_catalog_item_id IS NOT NULL AND trigger_offering_assembly_id IS NULL) OR " +
            "(trigger_catalog_item_id IS NULL AND trigger_offering_assembly_id IS NOT NULL)"));

        builder.Property(x => x.AccountId)
            .IsRequired();

        builder.Property(x => x.TriggerCatalogItemId);

        builder.Property(x => x.TriggerOfferingAssemblyId);

        // Alternate key — supports the composite FK from ActualWorkNudgeSuggestion.ActualWorkNudgeRuleId,
        // which must reference (AccountId, Id) so a malformed direct write can never pair a rule
        // from one account with a suggestion carrying a different account — same guard as
        // ScopeNudgeRule/ScopeNudgeSuggestion (build-log/122/123).
        builder.HasAlternateKey(x => new { x.AccountId, x.Id })
            .HasName("ak_keep_pricebook_actual_work_nudge_rules_account_id");

        // Suggestions is exposed as a read-only collection backed by a private field with no
        // public Add — EF must mutate through the field rather than a settable property/collection
        // API, matching ScopeNudgeRule.Suggestions.
        builder.Navigation(x => x.Suggestions)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        // 5d-ii preflight (build-log/129): at most one rule per trigger type per account — no
        // account-wide rule cap, same as ScopeNudgeRule. Names kept under Postgres's 63-char
        // identifier limit so they match the exact-name catch in EfActualWorkNudgeRulePersistence.
        builder.HasIndex(x => new { x.AccountId, x.TriggerCatalogItemId })
            .IsUnique()
            .HasFilter("trigger_catalog_item_id IS NOT NULL")
            .HasDatabaseName("ux_keep_aw_nudge_rules_trigger_catalog_item");

        builder.HasIndex(x => new { x.AccountId, x.TriggerOfferingAssemblyId })
            .IsUnique()
            .HasFilter("trigger_offering_assembly_id IS NOT NULL")
            .HasDatabaseName("ux_keep_aw_nudge_rules_trigger_offering_assembly");

        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(x => x.AccountId)
            .OnDelete(DeleteBehavior.Restrict);

        // Composite FK to CatalogItem(AccountId, Id) — prevents a rule triggering from a catalog
        // item in a different account. Stays nullable: null for an assembly-trigger rule. Restrict:
        // neither CatalogItem nor OfferingAssembly exposes a hard-delete path, matching every other
        // reference to those entities.
        builder.HasOne<CatalogItem>()
            .WithMany()
            .HasForeignKey(x => new { x.AccountId, x.TriggerCatalogItemId })
            .HasPrincipalKey(c => new { c.AccountId, c.Id })
            .OnDelete(DeleteBehavior.Restrict);

        // Composite FK to OfferingAssembly(AccountId, Id) — same cross-account guard for an
        // assembly-trigger rule. Stays nullable: null for a catalog-item-trigger rule.
        builder.HasOne<OfferingAssembly>()
            .WithMany()
            .HasForeignKey(x => new { x.AccountId, x.TriggerOfferingAssemblyId })
            .HasPrincipalKey(a => new { a.AccountId, a.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

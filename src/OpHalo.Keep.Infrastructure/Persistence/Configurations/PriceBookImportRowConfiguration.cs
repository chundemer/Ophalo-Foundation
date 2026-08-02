using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Infrastructure.Persistence.Configurations;
using OpHalo.Keep.Core.Entities;

namespace OpHalo.Keep.Infrastructure.Persistence.Configurations;

internal sealed class PriceBookImportRowConfiguration : BaseEntityConfiguration<PriceBookImportRow>
{
    protected override void ConfigureEntity(EntityTypeBuilder<PriceBookImportRow> builder)
    {
        builder.ToTable("keep_pricebook_import_rows");

        builder.Property(x => x.AccountId)
            .IsRequired();

        builder.Property(x => x.PriceBookImportId)
            .IsRequired();

        builder.Property(x => x.RowNumber)
            .IsRequired();

        builder.Property(x => x.SourceTab)
            .HasMaxLength(100);

        builder.Property(x => x.MappedCatalogItemId);

        builder.Property(x => x.ProposedType)
            .HasMaxLength(50);

        builder.Property(x => x.ProposedDisplayName)
            .HasMaxLength(200);

        builder.Property(x => x.ProposedExternalKey)
            .HasMaxLength(200);

        builder.Property(x => x.ProposedCategoryLabel)
            .HasMaxLength(200);

        builder.Property(x => x.ProposedUnitOfMeasure)
            .HasMaxLength(50);

        builder.Property(x => x.ProposedCost)
            .HasPrecision(18, 4);

        builder.Property(x => x.ProposedSellPrice)
            .HasPrecision(18, 4);

        builder.Property(x => x.ProposedCurrency)
            .HasMaxLength(3);

        builder.Property(x => x.ProposedSourceLaborHours)
            .HasPrecision(18, 4);

        builder.Property(x => x.ProposedSourceConsumablesAllowance)
            .HasPrecision(18, 4);

        builder.Property(x => x.ProposedSourceTaxAmount)
            .HasPrecision(18, 4);

        builder.Property(x => x.ValidationStatus)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        // Strongly typed string collection persisted as one jsonb column. The ValueComparer is
        // required so EF detects in-place mutation of the same List<string> instance across
        // MarkWarning/MarkError calls rather than only reference changes (there is no repository
        // precedent for a jsonb collection column in this codebase yet).
        var validationMessagesConverter = new ValueConverter<IReadOnlyCollection<string>, string>(
            messages => JsonSerializer.Serialize(messages, JsonSerializerOptions.Default),
            json => JsonSerializer.Deserialize<List<string>>(json, JsonSerializerOptions.Default) ?? new List<string>());

        var validationMessagesComparer = new ValueComparer<IReadOnlyCollection<string>>(
            (a, b) => (a ?? Array.Empty<string>()).SequenceEqual(b ?? Array.Empty<string>()),
            messages => messages.Aggregate(0, (hash, m) => HashCode.Combine(hash, m.GetHashCode())),
            messages => messages.ToList());

        builder.Property(x => x.ValidationMessages)
            .HasConversion(validationMessagesConverter)
            .HasColumnType("jsonb")
            .IsRequired()
            .Metadata.SetValueComparer(validationMessagesComparer);

        builder.Property(x => x.ExceptionResolution)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.HasIndex(x => new { x.PriceBookImportId, x.RowNumber })
            .IsUnique();

        builder.HasIndex(x => x.PriceBookImportId);

        builder.HasIndex(x => new { x.PriceBookImportId, x.ValidationStatus });

        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(x => x.AccountId)
            .OnDelete(DeleteBehavior.Restrict);

        // Composite FK to PriceBookImport(AccountId, Id) — prevents a row referencing an import
        // from a different account (matches the pattern established for CatalogItem.CategoryId).
        builder.HasOne<PriceBookImport>()
            .WithMany(x => x.Rows)
            .HasForeignKey(x => new { x.AccountId, x.PriceBookImportId })
            .HasPrincipalKey(i => new { i.AccountId, i.Id })
            .OnDelete(DeleteBehavior.Cascade);

        // Composite FK to CatalogItem(AccountId, Id) — prevents a row mapping to a catalog item
        // from a different account (matches the pattern established for CatalogItem.CategoryId).
        // MappedCatalogItemId stays nullable: null means "new item."
        builder.HasOne<CatalogItem>()
            .WithMany()
            .HasForeignKey(x => new { x.AccountId, x.MappedCatalogItemId })
            .HasPrincipalKey(c => new { c.AccountId, c.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

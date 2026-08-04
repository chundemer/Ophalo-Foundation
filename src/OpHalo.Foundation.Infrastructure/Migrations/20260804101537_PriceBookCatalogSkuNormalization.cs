using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpHalo.Foundation.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PriceBookCatalogSkuNormalization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_keep_pricebook_catalog_items_account_id_external_key",
                table: "keep_pricebook_catalog_items");

            migrationBuilder.AddColumn<string>(
                name: "normalized_external_key",
                table: "keep_pricebook_catalog_items",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            // Backfill before the unique index below, so any pre-existing collision under the
            // canonical form (e.g. "COP-34" vs "cop34") fails this migration atomically here
            // instead of a later application-level insert. Matches SkuNormalizer.Normalize
            // exactly: ASCII [A-Za-z0-9] only, lowercased, so runtime and backfill can never
            // diverge on Unicode input.
            migrationBuilder.Sql(
                """
                UPDATE keep_pricebook_catalog_items
                SET normalized_external_key = lower(regexp_replace(external_key, '[^A-Za-z0-9]', '', 'g'))
                WHERE external_key IS NOT NULL;
                """);

            migrationBuilder.CreateIndex(
                name: "ix_keep_pricebook_catalog_items_account_id_normalized_external",
                table: "keep_pricebook_catalog_items",
                columns: new[] { "account_id", "normalized_external_key" },
                unique: true,
                filter: "normalized_external_key IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_keep_pricebook_catalog_items_account_id_normalized_external",
                table: "keep_pricebook_catalog_items");

            migrationBuilder.DropColumn(
                name: "normalized_external_key",
                table: "keep_pricebook_catalog_items");

            migrationBuilder.CreateIndex(
                name: "ix_keep_pricebook_catalog_items_account_id_external_key",
                table: "keep_pricebook_catalog_items",
                columns: new[] { "account_id", "external_key" },
                unique: true,
                filter: "external_key IS NOT NULL");
        }
    }
}

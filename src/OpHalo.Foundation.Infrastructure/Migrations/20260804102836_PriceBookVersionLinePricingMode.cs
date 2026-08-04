using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpHalo.Foundation.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PriceBookVersionLinePricingMode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "pricing_mode",
                table: "keep_pricebook_version_lines",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            // Legacy rows predate PriceBookLinePricingMode (build-log/112): a non-null Sell Price
            // means StandalonePrice, its absence means NoStandalonePrice — same derivation
            // EfPriceBookPublishPersistence now applies to every new publish.
            migrationBuilder.Sql(
                """
                UPDATE keep_pricebook_version_lines
                SET pricing_mode = CASE
                    WHEN sell_price_snapshot IS NOT NULL THEN 'StandalonePrice'
                    ELSE 'NoStandalonePrice'
                END;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "pricing_mode",
                table: "keep_pricebook_version_lines",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "pricing_mode",
                table: "keep_pricebook_version_lines");
        }
    }
}

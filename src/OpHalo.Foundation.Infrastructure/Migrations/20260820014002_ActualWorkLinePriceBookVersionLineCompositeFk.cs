using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpHalo.Foundation.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ActualWorkLinePriceBookVersionLineCompositeFk : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_keep_actual_work_lines_price_book_version_line_account_id_p",
                table: "keep_actual_work_lines");

            migrationBuilder.DropIndex(
                name: "ix_keep_actual_work_lines_account_id_catalog_item_id",
                table: "keep_actual_work_lines");

            migrationBuilder.DropIndex(
                name: "ix_keep_actual_work_lines_account_id_price_book_version_line_id",
                table: "keep_actual_work_lines");

            migrationBuilder.AddUniqueConstraint(
                name: "ak_keep_pricebook_version_lines_account_id_catalog_item_id",
                table: "keep_pricebook_version_lines",
                columns: new[] { "account_id", "catalog_item_id", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_keep_actual_work_lines_account_id_catalog_item_id_price_boo",
                table: "keep_actual_work_lines",
                columns: new[] { "account_id", "catalog_item_id", "price_book_version_line_id" });

            migrationBuilder.AddForeignKey(
                name: "fk_keep_actual_work_lines_price_book_version_line_account_id_c",
                table: "keep_actual_work_lines",
                columns: new[] { "account_id", "catalog_item_id", "price_book_version_line_id" },
                principalTable: "keep_pricebook_version_lines",
                principalColumns: new[] { "account_id", "catalog_item_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_keep_actual_work_lines_price_book_version_line_account_id_c",
                table: "keep_actual_work_lines");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_keep_pricebook_version_lines_account_id_catalog_item_id",
                table: "keep_pricebook_version_lines");

            migrationBuilder.DropIndex(
                name: "ix_keep_actual_work_lines_account_id_catalog_item_id_price_boo",
                table: "keep_actual_work_lines");

            migrationBuilder.CreateIndex(
                name: "ix_keep_actual_work_lines_account_id_catalog_item_id",
                table: "keep_actual_work_lines",
                columns: new[] { "account_id", "catalog_item_id" });

            migrationBuilder.CreateIndex(
                name: "ix_keep_actual_work_lines_account_id_price_book_version_line_id",
                table: "keep_actual_work_lines",
                columns: new[] { "account_id", "price_book_version_line_id" });

            migrationBuilder.AddForeignKey(
                name: "fk_keep_actual_work_lines_price_book_version_line_account_id_p",
                table: "keep_actual_work_lines",
                columns: new[] { "account_id", "price_book_version_line_id" },
                principalTable: "keep_pricebook_version_lines",
                principalColumns: new[] { "account_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }
    }
}

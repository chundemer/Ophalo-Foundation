using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpHalo.Foundation.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PriceBookImport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "keep_pricebook_imports",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_file_object_key = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    uploaded_by_account_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    uploaded_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    published_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    published_by_account_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    published_price_book_version_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    deleted_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_keep_pricebook_imports", x => x.id);
                    table.UniqueConstraint("ak_keep_pricebook_imports_account_id", x => new { x.account_id, x.id });
                    table.ForeignKey(
                        name: "fk_keep_pricebook_imports_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "keep_pricebook_import_rows",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    price_book_import_id = table.Column<Guid>(type: "uuid", nullable: false),
                    row_number = table.Column<int>(type: "integer", nullable: false),
                    source_tab = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    mapped_catalog_item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    proposed_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    proposed_display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    proposed_external_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    proposed_category_label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    proposed_unit_of_measure = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    proposed_cost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    proposed_sell_price = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    proposed_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    proposed_source_labor_hours = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    proposed_source_consumables_allowance = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    proposed_source_tax_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    validation_status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    validation_messages = table.Column<string>(type: "jsonb", nullable: false),
                    exception_resolution = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    deleted_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_keep_pricebook_import_rows", x => x.id);
                    table.ForeignKey(
                        name: "fk_keep_pricebook_import_rows_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_keep_pricebook_import_rows_keep_pricebook_catalog_items_acc",
                        columns: x => new { x.account_id, x.mapped_catalog_item_id },
                        principalTable: "keep_pricebook_catalog_items",
                        principalColumns: new[] { "account_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_keep_pricebook_import_rows_keep_pricebook_imports_account_i",
                        columns: x => new { x.account_id, x.price_book_import_id },
                        principalTable: "keep_pricebook_imports",
                        principalColumns: new[] { "account_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_keep_pricebook_import_rows_account_id_mapped_catalog_item_id",
                table: "keep_pricebook_import_rows",
                columns: new[] { "account_id", "mapped_catalog_item_id" });

            migrationBuilder.CreateIndex(
                name: "ix_keep_pricebook_import_rows_account_id_price_book_import_id",
                table: "keep_pricebook_import_rows",
                columns: new[] { "account_id", "price_book_import_id" });

            migrationBuilder.CreateIndex(
                name: "ix_keep_pricebook_import_rows_price_book_import_id",
                table: "keep_pricebook_import_rows",
                column: "price_book_import_id");

            migrationBuilder.CreateIndex(
                name: "ix_keep_pricebook_import_rows_price_book_import_id_row_number",
                table: "keep_pricebook_import_rows",
                columns: new[] { "price_book_import_id", "row_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_keep_pricebook_import_rows_price_book_import_id_validation_",
                table: "keep_pricebook_import_rows",
                columns: new[] { "price_book_import_id", "validation_status" });

            migrationBuilder.CreateIndex(
                name: "ix_keep_pricebook_imports_account_id_status",
                table: "keep_pricebook_imports",
                columns: new[] { "account_id", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "keep_pricebook_import_rows");

            migrationBuilder.DropTable(
                name: "keep_pricebook_imports");
        }
    }
}

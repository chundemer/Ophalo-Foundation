using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpHalo.Foundation.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CatalogItem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "keep_pricebook_catalog_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    external_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    category_id = table.Column<Guid>(type: "uuid", nullable: true),
                    unit_of_measure = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    is_common_item = table.Column<bool>(type: "boolean", nullable: false),
                    active_state = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    current_price_book_version_line_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source_actual_work_line_id = table.Column<Guid>(type: "uuid", nullable: true),
                    concurrency_version = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    deleted_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_keep_pricebook_catalog_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_keep_pricebook_catalog_items_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_keep_pricebook_catalog_items_account_id",
                table: "keep_pricebook_catalog_items",
                column: "account_id",
                filter: "is_common_item = true");

            migrationBuilder.CreateIndex(
                name: "ix_keep_pricebook_catalog_items_account_id_active_state_catego",
                table: "keep_pricebook_catalog_items",
                columns: new[] { "account_id", "active_state", "category_id" });

            migrationBuilder.CreateIndex(
                name: "ix_keep_pricebook_catalog_items_account_id_external_key",
                table: "keep_pricebook_catalog_items",
                columns: new[] { "account_id", "external_key" },
                unique: true,
                filter: "external_key IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "keep_pricebook_catalog_items");
        }
    }
}

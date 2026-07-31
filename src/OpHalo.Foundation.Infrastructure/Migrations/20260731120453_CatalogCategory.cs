using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpHalo.Foundation.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CatalogCategory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "keep_pricebook_catalog_categories",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    normalized_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    active_state = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
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
                    table.PrimaryKey("pk_keep_pricebook_catalog_categories", x => x.id);
                    table.UniqueConstraint("ak_keep_pricebook_catalog_categories_account_id", x => new { x.account_id, x.id });
                    table.ForeignKey(
                        name: "fk_keep_pricebook_catalog_categories_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_keep_pricebook_catalog_items_account_id_category_id",
                table: "keep_pricebook_catalog_items",
                columns: new[] { "account_id", "category_id" });

            migrationBuilder.CreateIndex(
                name: "ix_keep_pricebook_catalog_categories_account_id_active_state",
                table: "keep_pricebook_catalog_categories",
                columns: new[] { "account_id", "active_state" });

            migrationBuilder.CreateIndex(
                name: "ix_keep_pricebook_catalog_categories_account_name",
                table: "keep_pricebook_catalog_categories",
                columns: new[] { "account_id", "normalized_name" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_keep_pricebook_catalog_items_keep_pricebook_catalog_categor",
                table: "keep_pricebook_catalog_items",
                columns: new[] { "account_id", "category_id" },
                principalTable: "keep_pricebook_catalog_categories",
                principalColumns: new[] { "account_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_keep_pricebook_catalog_items_keep_pricebook_catalog_categor",
                table: "keep_pricebook_catalog_items");

            migrationBuilder.DropTable(
                name: "keep_pricebook_catalog_categories");

            migrationBuilder.DropIndex(
                name: "ix_keep_pricebook_catalog_items_account_id_category_id",
                table: "keep_pricebook_catalog_items");
        }
    }
}

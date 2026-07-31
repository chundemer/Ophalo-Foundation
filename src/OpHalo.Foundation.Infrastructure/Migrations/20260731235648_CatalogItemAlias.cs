using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpHalo.Foundation.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CatalogItemAlias : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "ak_keep_pricebook_catalog_items_account_id",
                table: "keep_pricebook_catalog_items",
                columns: new[] { "account_id", "id" });

            migrationBuilder.CreateTable(
                name: "keep_pricebook_catalog_item_aliases",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    catalog_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    alias_text = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    normalized_alias_text = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    active_state = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    deleted_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_keep_pricebook_catalog_item_aliases", x => x.id);
                    table.ForeignKey(
                        name: "fk_keep_pricebook_catalog_item_aliases_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_keep_pricebook_catalog_item_aliases_catalog_item_account_id",
                        columns: x => new { x.account_id, x.catalog_item_id },
                        principalTable: "keep_pricebook_catalog_items",
                        principalColumns: new[] { "account_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_keep_pricebook_catalog_item_aliases_account_item_text",
                table: "keep_pricebook_catalog_item_aliases",
                columns: new[] { "account_id", "catalog_item_id", "normalized_alias_text" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "keep_pricebook_catalog_item_aliases");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_keep_pricebook_catalog_items_account_id",
                table: "keep_pricebook_catalog_items");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpHalo.Foundation.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class QuickScopeAction : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "keep_pricebook_quick_scope_actions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order = table.Column<int>(type: "integer", nullable: false),
                    catalog_item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    offering_assembly_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    deleted_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_keep_pricebook_quick_scope_actions", x => x.id);
                    table.CheckConstraint("ck_keep_pricebook_quick_scope_actions_exclusive_target", "(catalog_item_id IS NOT NULL AND offering_assembly_id IS NULL) OR (catalog_item_id IS NULL AND offering_assembly_id IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_keep_pricebook_quick_scope_actions_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_keep_pricebook_quick_scope_actions_keep_pricebook_catalog_i",
                        columns: x => new { x.account_id, x.catalog_item_id },
                        principalTable: "keep_pricebook_catalog_items",
                        principalColumns: new[] { "account_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_keep_pricebook_quick_scope_actions_keep_pricebook_offering_",
                        columns: x => new { x.account_id, x.offering_assembly_id },
                        principalTable: "keep_pricebook_offering_assemblies",
                        principalColumns: new[] { "account_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_keep_pricebook_quick_scope_actions_account_catalog_item",
                table: "keep_pricebook_quick_scope_actions",
                columns: new[] { "account_id", "catalog_item_id" },
                unique: true,
                filter: "catalog_item_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_keep_pricebook_quick_scope_actions_account_offering_assembly",
                table: "keep_pricebook_quick_scope_actions",
                columns: new[] { "account_id", "offering_assembly_id" },
                unique: true,
                filter: "offering_assembly_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_keep_pricebook_quick_scope_actions_account_order",
                table: "keep_pricebook_quick_scope_actions",
                columns: new[] { "account_id", "order" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "keep_pricebook_quick_scope_actions");
        }
    }
}

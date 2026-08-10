using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpHalo.Foundation.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class OfferingAssembly : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "keep_pricebook_offering_assemblies",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    primary_catalog_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    price_treatment = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
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
                    table.PrimaryKey("pk_keep_pricebook_offering_assemblies", x => x.id);
                    table.UniqueConstraint("ak_keep_pricebook_offering_assemblies_account_id", x => new { x.account_id, x.id });
                    table.ForeignKey(
                        name: "fk_keep_pricebook_offering_assemblies_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_keep_pricebook_offering_assemblies_keep_pricebook_catalog_i",
                        columns: x => new { x.account_id, x.primary_catalog_item_id },
                        principalTable: "keep_pricebook_catalog_items",
                        principalColumns: new[] { "account_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "keep_pricebook_offering_assembly_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    offering_assembly_id = table.Column<Guid>(type: "uuid", nullable: false),
                    catalog_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    default_quantity = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    is_optional = table.Column<bool>(type: "boolean", nullable: false),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    deleted_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_keep_pricebook_offering_assembly_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_keep_pricebook_offering_assembly_items_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_keep_pricebook_offering_assembly_items_keep_pricebook_catal",
                        columns: x => new { x.account_id, x.catalog_item_id },
                        principalTable: "keep_pricebook_catalog_items",
                        principalColumns: new[] { "account_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_keep_pricebook_offering_assembly_items_keep_pricebook_offer",
                        columns: x => new { x.account_id, x.offering_assembly_id },
                        principalTable: "keep_pricebook_offering_assemblies",
                        principalColumns: new[] { "account_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_keep_pricebook_offering_assemblies_account_id_active_state",
                table: "keep_pricebook_offering_assemblies",
                columns: new[] { "account_id", "active_state" });

            migrationBuilder.CreateIndex(
                name: "ux_keep_pricebook_offering_assemblies_active_primary_item",
                table: "keep_pricebook_offering_assemblies",
                columns: new[] { "account_id", "primary_catalog_item_id" },
                unique: true,
                filter: "active_state = 'Active'");

            migrationBuilder.CreateIndex(
                name: "ix_keep_pricebook_offering_assembly_items_account_id_catalog_i",
                table: "keep_pricebook_offering_assembly_items",
                columns: new[] { "account_id", "catalog_item_id" });

            migrationBuilder.CreateIndex(
                name: "ix_keep_pricebook_offering_assembly_items_account_id_offering_",
                table: "keep_pricebook_offering_assembly_items",
                columns: new[] { "account_id", "offering_assembly_id" });

            migrationBuilder.CreateIndex(
                name: "ux_keep_pricebook_offering_assembly_items_assembly_item",
                table: "keep_pricebook_offering_assembly_items",
                columns: new[] { "offering_assembly_id", "catalog_item_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "keep_pricebook_offering_assembly_items");

            migrationBuilder.DropTable(
                name: "keep_pricebook_offering_assemblies");
        }
    }
}

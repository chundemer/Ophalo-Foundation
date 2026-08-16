using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpHalo.Foundation.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemovedProposedScopeLineSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "keep_pricebook_removed_scope_line_snapshots",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    proposed_scope_id = table.Column<Guid>(type: "uuid", nullable: false),
                    line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    line_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    catalog_item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    offering_assembly_id = table.Column<Guid>(type: "uuid", nullable: true),
                    quantity = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    is_exception = table.Column<bool>(type: "boolean", nullable: false),
                    off_catalog_description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    off_catalog_quantity = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    display_name_snapshot = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    unit_of_measure_snapshot = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    offering_assembly_name_snapshot = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    default_quantity_snapshot = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    removed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    deleted_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_keep_pricebook_removed_scope_line_snapshots", x => x.id);
                    table.ForeignKey(
                        name: "fk_keep_pricebook_removed_scope_line_snapshots_accounts_accoun",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_keep_pricebook_removed_scope_line_snapshots_keep_pricebook_",
                        columns: x => new { x.account_id, x.proposed_scope_id },
                        principalTable: "keep_pricebook_proposed_scopes",
                        principalColumns: new[] { "account_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_keep_pricebook_removed_scope_line_snapshots_account_id_prop",
                table: "keep_pricebook_removed_scope_line_snapshots",
                columns: new[] { "account_id", "proposed_scope_id" });

            migrationBuilder.CreateIndex(
                name: "ux_keep_pricebook_removed_scope_line_snapshots_scope_line",
                table: "keep_pricebook_removed_scope_line_snapshots",
                columns: new[] { "proposed_scope_id", "line_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "keep_pricebook_removed_scope_line_snapshots");
        }
    }
}

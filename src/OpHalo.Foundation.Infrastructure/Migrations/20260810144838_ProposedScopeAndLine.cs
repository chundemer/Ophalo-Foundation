using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpHalo.Foundation.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ProposedScopeAndLine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "keep_pricebook_proposed_scopes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    submitted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    reviewed_by_account_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reviewed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
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
                    table.PrimaryKey("pk_keep_pricebook_proposed_scopes", x => x.id);
                    table.UniqueConstraint("ak_keep_pricebook_proposed_scopes_account_id", x => new { x.account_id, x.id });
                    table.ForeignKey(
                        name: "fk_keep_pricebook_proposed_scopes_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_keep_pricebook_proposed_scopes_keep_requests_account_id_req",
                        columns: x => new { x.account_id, x.request_id },
                        principalTable: "keep_requests",
                        principalColumns: new[] { "account_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "keep_pricebook_proposed_scope_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    proposed_scope_id = table.Column<Guid>(type: "uuid", nullable: false),
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
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    deleted_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_keep_pricebook_proposed_scope_lines", x => x.id);
                    table.ForeignKey(
                        name: "fk_keep_pricebook_proposed_scope_lines_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_keep_pricebook_proposed_scope_lines_keep_pricebook_catalog_",
                        columns: x => new { x.account_id, x.catalog_item_id },
                        principalTable: "keep_pricebook_catalog_items",
                        principalColumns: new[] { "account_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_keep_pricebook_proposed_scope_lines_keep_pricebook_offering",
                        columns: x => new { x.account_id, x.offering_assembly_id },
                        principalTable: "keep_pricebook_offering_assemblies",
                        principalColumns: new[] { "account_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_keep_pricebook_proposed_scope_lines_keep_pricebook_proposed",
                        columns: x => new { x.account_id, x.proposed_scope_id },
                        principalTable: "keep_pricebook_proposed_scopes",
                        principalColumns: new[] { "account_id", "id" });
                });

            migrationBuilder.CreateIndex(
                name: "ix_keep_pricebook_proposed_scope_lines_account_id_catalog_item",
                table: "keep_pricebook_proposed_scope_lines",
                columns: new[] { "account_id", "catalog_item_id" });

            migrationBuilder.CreateIndex(
                name: "ix_keep_pricebook_proposed_scope_lines_account_id_offering_ass",
                table: "keep_pricebook_proposed_scope_lines",
                columns: new[] { "account_id", "offering_assembly_id" });

            migrationBuilder.CreateIndex(
                name: "ix_keep_pricebook_proposed_scope_lines_account_id_proposed_sco",
                table: "keep_pricebook_proposed_scope_lines",
                columns: new[] { "account_id", "proposed_scope_id" });

            migrationBuilder.CreateIndex(
                name: "ix_keep_pricebook_proposed_scope_lines_proposed_scope_id_displ",
                table: "keep_pricebook_proposed_scope_lines",
                columns: new[] { "proposed_scope_id", "display_order" });

            migrationBuilder.CreateIndex(
                name: "ix_keep_pricebook_proposed_scopes_account_id_request_id",
                table: "keep_pricebook_proposed_scopes",
                columns: new[] { "account_id", "request_id" });

            migrationBuilder.CreateIndex(
                name: "ux_keep_pricebook_proposed_scopes_open_draft",
                table: "keep_pricebook_proposed_scopes",
                column: "request_id",
                unique: true,
                filter: "status = 'Draft'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "keep_pricebook_proposed_scope_lines");

            migrationBuilder.DropTable(
                name: "keep_pricebook_proposed_scopes");
        }
    }
}

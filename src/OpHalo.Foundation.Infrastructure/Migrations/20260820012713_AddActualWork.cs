using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpHalo.Foundation.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddActualWork : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "keep_actual_works",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    outcome = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    completion_note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    submitted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
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
                    table.PrimaryKey("pk_keep_actual_works", x => x.id);
                    table.UniqueConstraint("ak_keep_actual_works_account_id", x => new { x.account_id, x.id });
                    table.ForeignKey(
                        name: "fk_keep_actual_works_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_keep_actual_works_keep_request_account_id_request_id",
                        columns: x => new { x.account_id, x.request_id },
                        principalTable: "keep_requests",
                        principalColumns: new[] { "account_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "keep_actual_work_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actual_work_id = table.Column<Guid>(type: "uuid", nullable: false),
                    catalog_item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    price_book_version_line_id = table.Column<Guid>(type: "uuid", nullable: true),
                    display_name_snapshot = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    unit_of_measure_snapshot = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    actual_quantity = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    sell_price_snapshot = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    standard_expected_direct_cost_snapshot = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    commercial_baseline_source_line_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    deleted_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_keep_actual_work_lines", x => x.id);
                    table.CheckConstraint("ck_keep_actual_work_lines_three_state_linkage", "(price_book_version_line_id IS NULL OR catalog_item_id IS NOT NULL) AND ((sell_price_snapshot IS NULL AND standard_expected_direct_cost_snapshot IS NULL) OR price_book_version_line_id IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_keep_actual_work_lines_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_keep_actual_work_lines_catalog_item_account_id_catalog_item",
                        columns: x => new { x.account_id, x.catalog_item_id },
                        principalTable: "keep_pricebook_catalog_items",
                        principalColumns: new[] { "account_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_keep_actual_work_lines_keep_actual_works_account_id_actual_",
                        columns: x => new { x.account_id, x.actual_work_id },
                        principalTable: "keep_actual_works",
                        principalColumns: new[] { "account_id", "id" });
                    table.ForeignKey(
                        name: "fk_keep_actual_work_lines_price_book_version_line_account_id_p",
                        columns: x => new { x.account_id, x.price_book_version_line_id },
                        principalTable: "keep_pricebook_version_lines",
                        principalColumns: new[] { "account_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_keep_actual_work_lines_account_id_actual_work_id",
                table: "keep_actual_work_lines",
                columns: new[] { "account_id", "actual_work_id" });

            migrationBuilder.CreateIndex(
                name: "ix_keep_actual_work_lines_account_id_catalog_item_id",
                table: "keep_actual_work_lines",
                columns: new[] { "account_id", "catalog_item_id" });

            migrationBuilder.CreateIndex(
                name: "ix_keep_actual_work_lines_account_id_price_book_version_line_id",
                table: "keep_actual_work_lines",
                columns: new[] { "account_id", "price_book_version_line_id" });

            migrationBuilder.CreateIndex(
                name: "ix_keep_actual_works_account_id_request_id",
                table: "keep_actual_works",
                columns: new[] { "account_id", "request_id" });

            migrationBuilder.CreateIndex(
                name: "ux_keep_actual_works_open_draft",
                table: "keep_actual_works",
                column: "request_id",
                unique: true,
                filter: "status = 'Draft'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "keep_actual_work_lines");

            migrationBuilder.DropTable(
                name: "keep_actual_works");
        }
    }
}

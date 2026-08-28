using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpHalo.Foundation.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddActualWorkFinancialResolution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "ak_keep_actual_work_lines_account_visit_id",
                table: "keep_actual_work_lines",
                columns: new[] { "account_id", "actual_work_id", "id" });

            migrationBuilder.CreateTable(
                name: "keep_actual_work_line_financial_resolutions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actual_work_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actual_work_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    resolved_unit_sell_price = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    resolved_unit_standard_expected_direct_cost = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    basis = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    resolved_by_account_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    resolved_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    deleted_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_keep_actual_work_line_financial_resolutions", x => x.id);
                    table.CheckConstraint("ck_keep_actual_work_line_financial_resolutions_non_negative", "(resolved_unit_sell_price IS NULL OR resolved_unit_sell_price >= 0) AND (resolved_unit_standard_expected_direct_cost IS NULL OR resolved_unit_standard_expected_direct_cost >= 0)");
                    table.CheckConstraint("ck_keep_actual_work_line_financial_resolutions_reason_present", "length(btrim(reason)) > 0");
                    table.CheckConstraint("ck_keep_actual_work_line_financial_resolutions_value_present", "resolved_unit_sell_price IS NOT NULL OR resolved_unit_standard_expected_direct_cost IS NOT NULL");
                    table.ForeignKey(
                        name: "fk_keep_actual_work_line_financial_resolutions_accounts_accoun",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_keep_actual_work_line_financial_resolutions_keep_actual_wor",
                        columns: x => new { x.account_id, x.actual_work_id },
                        principalTable: "keep_actual_works",
                        principalColumns: new[] { "account_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_keep_actual_work_line_financial_resolutions_keep_actual_wor1",
                        columns: x => new { x.account_id, x.actual_work_id, x.actual_work_line_id },
                        principalTable: "keep_actual_work_lines",
                        principalColumns: new[] { "account_id", "actual_work_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "keep_actual_work_office_financial_dispositions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actual_work_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    disposed_by_account_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    disposed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    deleted_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_keep_actual_work_office_financial_dispositions", x => x.id);
                    table.CheckConstraint("ck_keep_actual_work_office_fin_disposition_reason_present", "length(btrim(reason)) > 0");
                    table.ForeignKey(
                        name: "fk_keep_actual_work_office_financial_dispositions_accounts_acc",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_keep_actual_work_office_financial_dispositions_keep_actual_",
                        columns: x => new { x.account_id, x.actual_work_id },
                        principalTable: "keep_actual_works",
                        principalColumns: new[] { "account_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_keep_actual_work_line_financial_resolutions_account_id_actu",
                table: "keep_actual_work_line_financial_resolutions",
                columns: new[] { "account_id", "actual_work_id", "actual_work_line_id" });

            migrationBuilder.CreateIndex(
                name: "ix_keep_actual_work_line_financial_resolutions_effective",
                table: "keep_actual_work_line_financial_resolutions",
                columns: new[] { "account_id", "actual_work_line_id", "resolved_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_keep_actual_work_office_financial_dispositions_effective",
                table: "keep_actual_work_office_financial_dispositions",
                columns: new[] { "account_id", "actual_work_id", "disposed_at_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "keep_actual_work_line_financial_resolutions");

            migrationBuilder.DropTable(
                name: "keep_actual_work_office_financial_dispositions");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_keep_actual_work_lines_account_visit_id",
                table: "keep_actual_work_lines");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpHalo.Foundation.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Gap100PolicyCalendarSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "business_hours_only",
                table: "keep_response_policies");

            migrationBuilder.AddColumn<string>(
                name: "first_response_timing_basis",
                table: "keep_response_policies",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Continuous");

            migrationBuilder.AddColumn<string>(
                name: "priority_response_timing_basis",
                table: "keep_response_policies",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Continuous");

            migrationBuilder.AddColumn<string>(
                name: "standard_response_timing_basis",
                table: "keep_response_policies",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Continuous");

            migrationBuilder.CreateTable(
                name: "keep_calendar_closures",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    closure_date = table.Column<DateOnly>(type: "date", nullable: false),
                    label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    deleted_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_keep_calendar_closures", x => x.id);
                    table.ForeignKey(
                        name: "fk_keep_calendar_closures_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "keep_calendar_weekly_intervals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    weekday = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    opens_at = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    closes_at = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    deleted_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_keep_calendar_weekly_intervals", x => x.id);
                    table.ForeignKey(
                        name: "fk_keep_calendar_weekly_intervals_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_keep_calendar_closures_account_id_closure_date",
                table: "keep_calendar_closures",
                columns: new[] { "account_id", "closure_date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_keep_calendar_weekly_intervals_account_id_weekday",
                table: "keep_calendar_weekly_intervals",
                columns: new[] { "account_id", "weekday" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "keep_calendar_closures");

            migrationBuilder.DropTable(
                name: "keep_calendar_weekly_intervals");

            migrationBuilder.DropColumn(
                name: "first_response_timing_basis",
                table: "keep_response_policies");

            migrationBuilder.DropColumn(
                name: "priority_response_timing_basis",
                table: "keep_response_policies");

            migrationBuilder.DropColumn(
                name: "standard_response_timing_basis",
                table: "keep_response_policies");

            migrationBuilder.AddColumn<bool>(
                name: "business_hours_only",
                table: "keep_response_policies",
                type: "boolean",
                nullable: true);
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpHalo.Foundation.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddActualWorkPerformer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "default_performed_by_account_user_id",
                table: "keep_actual_works",
                type: "uuid",
                nullable: true);

            // ADR-494 D1: strict non-null, NO backfill. Production and any correctly-reset local DB
            // hold zero Actual Work line rows, so ADD COLUMN ... NOT NULL succeeds with no default.
            // A DB that still holds line rows fails loudly here on purpose — never a manufactured
            // Guid.Empty fill (EF's auto-generated defaultValue removed).
            migrationBuilder.AddColumn<Guid>(
                name: "performed_by_account_user_id",
                table: "keep_actual_work_lines",
                type: "uuid",
                nullable: false);

            migrationBuilder.CreateIndex(
                name: "ix_keep_actual_works_account_id_default_performed_by_account_u",
                table: "keep_actual_works",
                columns: new[] { "account_id", "default_performed_by_account_user_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_keep_actual_works_account_id_default_performed_by_account_u",
                table: "keep_actual_works");

            migrationBuilder.DropColumn(
                name: "default_performed_by_account_user_id",
                table: "keep_actual_works");

            migrationBuilder.DropColumn(
                name: "performed_by_account_user_id",
                table: "keep_actual_work_lines");
        }
    }
}

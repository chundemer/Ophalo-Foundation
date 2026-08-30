using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpHalo.Foundation.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddActualWorkSupersession : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "superseded_at_utc",
                table: "keep_actual_works",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "superseded_by_account_user_id",
                table: "keep_actual_works",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "superseded_by_actual_work_id",
                table: "keep_actual_works",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "supersession_reason",
                table: "keep_actual_works",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_keep_actual_works_account_id_superseded_by_actual_work_id",
                table: "keep_actual_works",
                columns: new[] { "account_id", "superseded_by_actual_work_id" });

            migrationBuilder.CreateIndex(
                name: "ux_keep_actual_works_superseded_by",
                table: "keep_actual_works",
                column: "superseded_by_actual_work_id",
                unique: true,
                filter: "superseded_by_actual_work_id IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "fk_keep_actual_works_keep_actual_works_account_id_superseded_b",
                table: "keep_actual_works",
                columns: new[] { "account_id", "superseded_by_actual_work_id" },
                principalTable: "keep_actual_works",
                principalColumns: new[] { "account_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_keep_actual_works_keep_actual_works_account_id_superseded_b",
                table: "keep_actual_works");

            migrationBuilder.DropIndex(
                name: "ix_keep_actual_works_account_id_superseded_by_actual_work_id",
                table: "keep_actual_works");

            migrationBuilder.DropIndex(
                name: "ux_keep_actual_works_superseded_by",
                table: "keep_actual_works");

            migrationBuilder.DropColumn(
                name: "superseded_at_utc",
                table: "keep_actual_works");

            migrationBuilder.DropColumn(
                name: "superseded_by_account_user_id",
                table: "keep_actual_works");

            migrationBuilder.DropColumn(
                name: "superseded_by_actual_work_id",
                table: "keep_actual_works");

            migrationBuilder.DropColumn(
                name: "supersession_reason",
                table: "keep_actual_works");
        }
    }
}

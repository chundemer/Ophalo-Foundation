using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpHalo.Foundation.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddActualWorkRecorderAccountUserId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Nullable first so existing rows can be backfilled from their immutable
            // created_by_user_id authorship (GAP-055: RecorderAccountUserId starts equal to the
            // creator at Create time) before the NOT NULL constraint is applied — a fixed empty-guid
            // default would strand every pre-existing row outside the new recorder-ownership gate.
            migrationBuilder.AddColumn<Guid>(
                name: "recorder_account_user_id",
                table: "keep_actual_works",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql(
                "UPDATE keep_actual_works SET recorder_account_user_id = created_by_user_id " +
                "WHERE recorder_account_user_id IS NULL;");

            migrationBuilder.AlterColumn<Guid>(
                name: "recorder_account_user_id",
                table: "keep_actual_works",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "recorder_account_user_id",
                table: "keep_actual_works");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpHalo.Foundation.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddActualWorkDraftRecorderTransfer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "keep_actual_work_draft_recorder_transfers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actual_work_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_account_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    prior_recorder_account_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    new_recorder_account_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    transferred_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    deleted_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_keep_actual_work_draft_recorder_transfers", x => x.id);
                    table.ForeignKey(
                        name: "fk_keep_actual_work_draft_recorder_transfers_keep_actual_works",
                        columns: x => new { x.account_id, x.actual_work_id },
                        principalTable: "keep_actual_works",
                        principalColumns: new[] { "account_id", "id" });
                });

            migrationBuilder.CreateIndex(
                name: "ix_keep_actual_work_draft_recorder_transfers_account_id_actual",
                table: "keep_actual_work_draft_recorder_transfers",
                columns: new[] { "account_id", "actual_work_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "keep_actual_work_draft_recorder_transfers");
        }
    }
}

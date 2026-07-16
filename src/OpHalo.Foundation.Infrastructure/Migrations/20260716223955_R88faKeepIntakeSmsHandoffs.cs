using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpHalo.Foundation.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class R88faKeepIntakeSmsHandoffs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "keep_intake_sms_handoffs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    handoff_token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    message_body = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    expires_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    deleted_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_keep_intake_sms_handoffs", x => x.id);
                    table.ForeignKey(
                        name: "fk_keep_intake_sms_handoffs_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_keep_intake_sms_handoffs_account_id",
                table: "keep_intake_sms_handoffs",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_keep_intake_sms_handoffs_expires_at",
                table: "keep_intake_sms_handoffs",
                column: "expires_at_utc");

            migrationBuilder.CreateIndex(
                name: "ix_keep_intake_sms_handoffs_token_hash",
                table: "keep_intake_sms_handoffs",
                column: "handoff_token_hash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "keep_intake_sms_handoffs");
        }
    }
}

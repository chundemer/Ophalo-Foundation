using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpHalo.Foundation.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AccountSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "account_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    client_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    device_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    expires_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_activity_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_seen_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    revoked_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_account_sessions", x => x.id);
                    table.ForeignKey(
                        name: "fk_account_sessions_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_account_sessions_account_users_account_user_id",
                        column: x => x.account_user_id,
                        principalTable: "account_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_account_sessions_account_id",
                table: "account_sessions",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_account_sessions_account_user_id",
                table: "account_sessions",
                column: "account_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_account_sessions_expires_at_utc",
                table: "account_sessions",
                column: "expires_at_utc");

            migrationBuilder.CreateIndex(
                name: "ix_account_sessions_token_hash",
                table: "account_sessions",
                column: "session_token_hash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "account_sessions");
        }
    }
}

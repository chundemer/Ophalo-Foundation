using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpHalo.Foundation.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AccountAuthCodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "account_auth_codes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    code_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    issued_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    expires_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    consumed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    invalidated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    delivery_email_snapshot = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    target_account_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    entry_context = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_account_auth_codes", x => x.id);
                    table.ForeignKey(
                        name: "fk_account_auth_codes_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_account_auth_codes_account_id",
                table: "account_auth_codes",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_account_auth_codes_code_hash",
                table: "account_auth_codes",
                column: "code_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_account_auth_codes_expires_at_utc",
                table: "account_auth_codes",
                column: "expires_at_utc");

            migrationBuilder.CreateIndex(
                name: "ix_account_auth_codes_target_account_user_id",
                table: "account_auth_codes",
                column: "target_account_user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "account_auth_codes");
        }
    }
}

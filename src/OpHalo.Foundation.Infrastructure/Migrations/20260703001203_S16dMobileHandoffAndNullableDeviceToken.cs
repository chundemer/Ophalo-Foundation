using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpHalo.Foundation.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class S16dMobileHandoffAndNullableDeviceToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_account_user_devices_fingerprint_active",
                table: "account_user_devices");

            migrationBuilder.AlterColumn<string>(
                name: "token_last_four",
                table: "account_user_devices",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(10)",
                oldMaxLength: 10);

            migrationBuilder.AlterColumn<string>(
                name: "push_token_fingerprint",
                table: "account_user_devices",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);

            migrationBuilder.AlterColumn<string>(
                name: "push_token",
                table: "account_user_devices",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(1024)",
                oldMaxLength: 1024);

            migrationBuilder.CreateTable(
                name: "mobile_handoff_codes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    issued_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    expires_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    consumed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mobile_handoff_codes", x => x.id);
                    table.ForeignKey(
                        name: "fk_mobile_handoff_codes_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_account_user_devices_fingerprint_active",
                table: "account_user_devices",
                column: "push_token_fingerprint",
                filter: "status = 'Active' AND push_token_fingerprint IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_mobile_handoff_codes_account_id",
                table: "mobile_handoff_codes",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_mobile_handoff_codes_code_hash",
                table: "mobile_handoff_codes",
                column: "code_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_mobile_handoff_codes_expires_at_utc",
                table: "mobile_handoff_codes",
                column: "expires_at_utc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mobile_handoff_codes");

            migrationBuilder.DropIndex(
                name: "ix_account_user_devices_fingerprint_active",
                table: "account_user_devices");

            migrationBuilder.AlterColumn<string>(
                name: "token_last_four",
                table: "account_user_devices",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(10)",
                oldMaxLength: 10,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "push_token_fingerprint",
                table: "account_user_devices",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "push_token",
                table: "account_user_devices",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(1024)",
                oldMaxLength: 1024,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_account_user_devices_fingerprint_active",
                table: "account_user_devices",
                column: "push_token_fingerprint",
                filter: "status = 'Active'");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpHalo.Foundation.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountUserDevices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "account_user_devices",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    app_installation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    platform = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    push_token = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    push_token_fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    token_last_four = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    app_version = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    device_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_seen_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    revoked_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_delivery_failure_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_delivery_failure_reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_account_user_devices", x => x.id);
                    table.ForeignKey(
                        name: "fk_account_user_devices_account_users_account_id_account_user_",
                        columns: x => new { x.account_id, x.account_user_id },
                        principalTable: "account_users",
                        principalColumns: new[] { "account_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_account_user_devices_account_user",
                table: "account_user_devices",
                columns: new[] { "account_id", "account_user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_account_user_devices_fingerprint_active",
                table: "account_user_devices",
                column: "push_token_fingerprint",
                filter: "status = 'Active'");

            migrationBuilder.CreateIndex(
                name: "ix_account_user_devices_user_install",
                table: "account_user_devices",
                columns: new[] { "account_user_id", "app_installation_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "account_user_devices");
        }
    }
}

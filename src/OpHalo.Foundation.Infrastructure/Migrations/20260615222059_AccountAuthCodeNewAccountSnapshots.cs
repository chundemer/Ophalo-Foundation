using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpHalo.Foundation.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AccountAuthCodeNewAccountSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "business_name_snapshot",
                table: "account_auth_codes",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "name_snapshot",
                table: "account_auth_codes",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "time_zone_snapshot",
                table: "account_auth_codes",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_account_auth_codes_delivery_email_entry_context",
                table: "account_auth_codes",
                columns: new[] { "delivery_email_snapshot", "entry_context" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_account_auth_codes_delivery_email_entry_context",
                table: "account_auth_codes");

            migrationBuilder.DropColumn(
                name: "business_name_snapshot",
                table: "account_auth_codes");

            migrationBuilder.DropColumn(
                name: "name_snapshot",
                table: "account_auth_codes");

            migrationBuilder.DropColumn(
                name: "time_zone_snapshot",
                table: "account_auth_codes");
        }
    }
}

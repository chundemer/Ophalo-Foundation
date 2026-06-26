using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpHalo.Foundation.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AccountClassification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "classification",
                table: "account_entitlements",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE account_entitlements
                SET classification = CASE
                    WHEN plan = 'Internal' THEN 'InternalTest'
                    WHEN is_pilot THEN 'Pilot'
                    ELSE 'Production'
                END;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "classification",
                table: "account_entitlements",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldNullable: true);

            migrationBuilder.DropColumn(
                name: "is_pilot",
                table: "account_entitlements");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_pilot",
                table: "account_entitlements",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql("""
                UPDATE account_entitlements
                SET is_pilot = classification = 'Pilot';
                """);

            migrationBuilder.DropColumn(
                name: "classification",
                table: "account_entitlements");
        }
    }
}

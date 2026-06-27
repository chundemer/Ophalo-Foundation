using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpHalo.Foundation.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class KeepSetupBusinessProfileAndPolicyThreshold : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "status_check_threshold_days",
                table: "keep_response_policies",
                type: "integer",
                nullable: false,
                defaultValue: 5);

            migrationBuilder.CreateTable(
                name: "keep_business_profiles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_facing_phone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    customer_facing_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    deleted_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_keep_business_profiles", x => x.id);
                    table.ForeignKey(
                        name: "fk_keep_business_profiles_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_keep_business_profiles_account_id",
                table: "keep_business_profiles",
                column: "account_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "keep_business_profiles");

            migrationBuilder.DropColumn(
                name: "status_check_threshold_days",
                table: "keep_response_policies");
        }
    }
}

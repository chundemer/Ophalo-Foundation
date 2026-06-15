using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpHalo.Foundation.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialFoundationSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    phone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    is_email_verified = table.Column<bool>(type: "boolean", nullable: false),
                    email_verified_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_login_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    deleted_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "account_entitlements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    commercial_state = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    operating_mode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    trial_ends_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    past_due_grace_ends_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    max_user_seats = table.Column<int>(type: "integer", nullable: false),
                    is_pilot = table.Column<bool>(type: "boolean", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    deleted_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_account_entitlements", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "account_users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    membership_status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    normalized_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    role = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    invite_token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    invite_expires_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    activated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    deleted_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_account_users", x => x.id);
                    table.ForeignKey(
                        name: "fk_account_users_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "accounts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    business_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    time_zone = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    purpose = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    lifecycle_state = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    primary_owner_account_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    last_login_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    deleted_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_accounts", x => x.id);
                    table.ForeignKey(
                        name: "fk_accounts_account_users_primary_owner_account_user_id",
                        column: x => x.primary_owner_account_user_id,
                        principalTable: "account_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_account_entitlements_account_id",
                table: "account_entitlements",
                column: "account_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_account_users_account_email",
                table: "account_users",
                columns: new[] { "account_id", "normalized_email" },
                unique: true,
                filter: "deleted_at_utc IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_account_users_account_id",
                table: "account_users",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_account_users_account_user",
                table: "account_users",
                columns: new[] { "account_id", "user_id" },
                unique: true,
                filter: "user_id IS NOT NULL AND deleted_at_utc IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_account_users_invite_token_hash",
                table: "account_users",
                column: "invite_token_hash",
                unique: true,
                filter: "invite_token_hash IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_account_users_user_id",
                table: "account_users",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_accounts_primary_owner_account_user_id",
                table: "accounts",
                column: "primary_owner_account_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_users_email",
                table: "users",
                column: "email",
                unique: true,
                filter: "deleted_at_utc IS NULL");

            migrationBuilder.AddForeignKey(
                name: "fk_account_entitlements_accounts_account_id",
                table: "account_entitlements",
                column: "account_id",
                principalTable: "accounts",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_account_users_accounts_account_id",
                table: "account_users",
                column: "account_id",
                principalTable: "accounts",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_account_users_accounts_account_id",
                table: "account_users");

            migrationBuilder.DropTable(
                name: "account_entitlements");

            migrationBuilder.DropTable(
                name: "accounts");

            migrationBuilder.DropTable(
                name: "account_users");

            migrationBuilder.DropTable(
                name: "users");
        }
    }
}

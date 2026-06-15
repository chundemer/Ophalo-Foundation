using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpHalo.Foundation.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class KeepDomain : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "keep_customers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    primary_phone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    deleted_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_keep_customers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "keep_public_intake_links",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    public_slug = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    revoked_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    deleted_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_keep_public_intake_links", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "keep_request_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    content = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    actor_account_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    visibility = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    occurred_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    deleted_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_keep_request_events", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "keep_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    keep_customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    customer_phone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    customer_email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    current_status_text = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    reference_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    page_token = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    expires_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    closed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_business_activity_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_customer_activity_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    deleted_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_keep_requests", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_keep_customers_account_phone",
                table: "keep_customers",
                columns: new[] { "account_id", "primary_phone" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_keep_public_intake_links_active_slug",
                table: "keep_public_intake_links",
                column: "public_slug",
                unique: true,
                filter: "revoked_at_utc IS NULL AND deleted_at_utc IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_keep_public_intake_links_active_token_hash",
                table: "keep_public_intake_links",
                column: "token_hash",
                unique: true,
                filter: "revoked_at_utc IS NULL AND deleted_at_utc IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_keep_request_events_account_id",
                table: "keep_request_events",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_keep_request_events_request_id",
                table: "keep_request_events",
                column: "request_id");

            migrationBuilder.CreateIndex(
                name: "ix_keep_requests_account_id",
                table: "keep_requests",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_keep_requests_account_reference_code",
                table: "keep_requests",
                columns: new[] { "account_id", "reference_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_keep_requests_page_token",
                table: "keep_requests",
                column: "page_token",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "keep_customers");

            migrationBuilder.DropTable(
                name: "keep_public_intake_links");

            migrationBuilder.DropTable(
                name: "keep_request_events");

            migrationBuilder.DropTable(
                name: "keep_requests");
        }
    }
}

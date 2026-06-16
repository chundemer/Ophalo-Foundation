using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpHalo.Foundation.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase8KeepDataModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "closed_at_utc",
                table: "keep_requests",
                newName: "terminated_at_utc");

            migrationBuilder.AddColumn<string>(
                name: "attention_clear_reason",
                table: "keep_requests",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "attention_cleared_at_utc",
                table: "keep_requests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "attention_cleared_by_account_user_id",
                table: "keep_requests",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "attention_level",
                table: "keep_requests",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "None");

            migrationBuilder.AddColumn<string>(
                name: "attention_reason",
                table: "keep_requests",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "attention_since_utc",
                table: "keep_requests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "feedback_comment",
                table: "keep_requests",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "feedback_submitted_at_utc",
                table: "keep_requests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "feedback_was_resolved",
                table: "keep_requests",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "first_responded_at_utc",
                table: "keep_requests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "first_responder_account_user_id",
                table: "keep_requests",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "first_response_due_at_utc",
                table: "keep_requests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "first_response_event_id",
                table: "keep_requests",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "next_attention_at_utc",
                table: "keep_requests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "origin",
                table: "keep_requests",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "Customer");

            migrationBuilder.AddColumn<string>(
                name: "priority_band",
                table: "keep_requests",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "Standard");

            migrationBuilder.AddColumn<string>(
                name: "waiting_direction",
                table: "keep_requests",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "None");

            migrationBuilder.AddColumn<string>(
                name: "actor_display_name",
                table: "keep_request_events",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "actor_type",
                table: "keep_request_events",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "System");

            migrationBuilder.AddColumn<string>(
                name: "communication_channel",
                table: "keep_request_events",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "message_intent",
                table: "keep_request_events",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "keep_request_participants",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    participation_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    notifications_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    attached_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    detached_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    deleted_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_keep_request_participants", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "keep_response_policies",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    first_response_target_minutes = table.Column<int>(type: "integer", nullable: false),
                    standard_response_target_minutes = table.Column<int>(type: "integer", nullable: false),
                    priority_response_target_minutes = table.Column<int>(type: "integer", nullable: false),
                    business_hours_only = table.Column<bool>(type: "boolean", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    deleted_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_keep_response_policies", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_keep_requests_account_attention",
                table: "keep_requests",
                columns: new[] { "account_id", "attention_level", "attention_since_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_keep_request_participants_account_user",
                table: "keep_request_participants",
                columns: new[] { "account_id", "account_user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_keep_request_participants_request_id",
                table: "keep_request_participants",
                column: "request_id");

            migrationBuilder.CreateIndex(
                name: "ix_keep_request_participants_request_user",
                table: "keep_request_participants",
                columns: new[] { "request_id", "account_user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_keep_response_policies_account_id",
                table: "keep_response_policies",
                column: "account_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "keep_request_participants");

            migrationBuilder.DropTable(
                name: "keep_response_policies");

            migrationBuilder.DropIndex(
                name: "ix_keep_requests_account_attention",
                table: "keep_requests");

            migrationBuilder.DropColumn(
                name: "attention_clear_reason",
                table: "keep_requests");

            migrationBuilder.DropColumn(
                name: "attention_cleared_at_utc",
                table: "keep_requests");

            migrationBuilder.DropColumn(
                name: "attention_cleared_by_account_user_id",
                table: "keep_requests");

            migrationBuilder.DropColumn(
                name: "attention_level",
                table: "keep_requests");

            migrationBuilder.DropColumn(
                name: "attention_reason",
                table: "keep_requests");

            migrationBuilder.DropColumn(
                name: "attention_since_utc",
                table: "keep_requests");

            migrationBuilder.DropColumn(
                name: "feedback_comment",
                table: "keep_requests");

            migrationBuilder.DropColumn(
                name: "feedback_submitted_at_utc",
                table: "keep_requests");

            migrationBuilder.DropColumn(
                name: "feedback_was_resolved",
                table: "keep_requests");

            migrationBuilder.DropColumn(
                name: "first_responded_at_utc",
                table: "keep_requests");

            migrationBuilder.DropColumn(
                name: "first_responder_account_user_id",
                table: "keep_requests");

            migrationBuilder.DropColumn(
                name: "first_response_due_at_utc",
                table: "keep_requests");

            migrationBuilder.DropColumn(
                name: "first_response_event_id",
                table: "keep_requests");

            migrationBuilder.DropColumn(
                name: "next_attention_at_utc",
                table: "keep_requests");

            migrationBuilder.DropColumn(
                name: "origin",
                table: "keep_requests");

            migrationBuilder.DropColumn(
                name: "priority_band",
                table: "keep_requests");

            migrationBuilder.DropColumn(
                name: "waiting_direction",
                table: "keep_requests");

            migrationBuilder.DropColumn(
                name: "actor_display_name",
                table: "keep_request_events");

            migrationBuilder.DropColumn(
                name: "actor_type",
                table: "keep_request_events");

            migrationBuilder.DropColumn(
                name: "communication_channel",
                table: "keep_request_events");

            migrationBuilder.DropColumn(
                name: "message_intent",
                table: "keep_request_events");

            migrationBuilder.RenameColumn(
                name: "terminated_at_utc",
                table: "keep_requests",
                newName: "closed_at_utc");
        }
    }
}

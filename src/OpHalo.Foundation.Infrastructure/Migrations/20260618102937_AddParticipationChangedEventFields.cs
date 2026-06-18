using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpHalo.Foundation.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddParticipationChangedEventFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_keep_request_participants_request_id",
                table: "keep_request_participants");

            migrationBuilder.AddColumn<string>(
                name: "participation_action",
                table: "keep_request_events",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "participation_internal_note",
                table: "keep_request_events",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "participation_notification_intended_recipient_account_user_id",
                table: "keep_request_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "participation_notification_intent_kind",
                table: "keep_request_events",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "participation_previous_responsible_account_user_id",
                table: "keep_request_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "participation_target_account_user_id",
                table: "keep_request_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "participation_target_display_name",
                table: "keep_request_events",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_keep_request_participants_request_id",
                table: "keep_request_participants",
                column: "request_id",
                unique: true,
                filter: "participation_type = 'Responsible' AND detached_at_utc IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_keep_request_participants_request_id",
                table: "keep_request_participants");

            migrationBuilder.DropColumn(
                name: "participation_action",
                table: "keep_request_events");

            migrationBuilder.DropColumn(
                name: "participation_internal_note",
                table: "keep_request_events");

            migrationBuilder.DropColumn(
                name: "participation_notification_intended_recipient_account_user_id",
                table: "keep_request_events");

            migrationBuilder.DropColumn(
                name: "participation_notification_intent_kind",
                table: "keep_request_events");

            migrationBuilder.DropColumn(
                name: "participation_previous_responsible_account_user_id",
                table: "keep_request_events");

            migrationBuilder.DropColumn(
                name: "participation_target_account_user_id",
                table: "keep_request_events");

            migrationBuilder.DropColumn(
                name: "participation_target_display_name",
                table: "keep_request_events");

            migrationBuilder.CreateIndex(
                name: "ix_keep_request_participants_request_id",
                table: "keep_request_participants",
                column: "request_id");
        }
    }
}

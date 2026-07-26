using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpHalo.Foundation.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationPrepareConfirmObligation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "pending_notification_channel",
                table: "keep_requests",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "pending_notification_prepared_at_utc",
                table: "keep_requests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "pending_notification_prepared_by_account_user_id",
                table: "keep_requests",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "pending_notification_related_event_id",
                table: "keep_requests",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "related_event_id",
                table: "keep_request_events",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "pending_notification_channel",
                table: "keep_requests");

            migrationBuilder.DropColumn(
                name: "pending_notification_prepared_at_utc",
                table: "keep_requests");

            migrationBuilder.DropColumn(
                name: "pending_notification_prepared_by_account_user_id",
                table: "keep_requests");

            migrationBuilder.DropColumn(
                name: "pending_notification_related_event_id",
                table: "keep_requests");

            migrationBuilder.DropColumn(
                name: "related_event_id",
                table: "keep_request_events");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpHalo.Foundation.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class KeepP6b1FollowUpOnPlannedFor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "follow_up_note",
                table: "keep_requests",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "follow_up_on_date",
                table: "keep_requests",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "follow_up_reason",
                table: "keep_requests",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "planned_for_date",
                table: "keep_requests",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "follow_up_on_date",
                table: "keep_request_events",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "follow_up_on_reason",
                table: "keep_request_events",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "planned_for_date",
                table: "keep_request_events",
                type: "date",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "follow_up_note",
                table: "keep_requests");

            migrationBuilder.DropColumn(
                name: "follow_up_on_date",
                table: "keep_requests");

            migrationBuilder.DropColumn(
                name: "follow_up_reason",
                table: "keep_requests");

            migrationBuilder.DropColumn(
                name: "planned_for_date",
                table: "keep_requests");

            migrationBuilder.DropColumn(
                name: "follow_up_on_date",
                table: "keep_request_events");

            migrationBuilder.DropColumn(
                name: "follow_up_on_reason",
                table: "keep_request_events");

            migrationBuilder.DropColumn(
                name: "planned_for_date",
                table: "keep_request_events");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpHalo.Foundation.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFeedbackReviewFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "feedback_review_note",
                table: "keep_requests",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "feedback_reviewed_at_utc",
                table: "keep_requests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "feedback_reviewed_by_account_user_id",
                table: "keep_requests",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "feedback_review_note",
                table: "keep_requests");

            migrationBuilder.DropColumn(
                name: "feedback_reviewed_at_utc",
                table: "keep_requests");

            migrationBuilder.DropColumn(
                name: "feedback_reviewed_by_account_user_id",
                table: "keep_requests");
        }
    }
}

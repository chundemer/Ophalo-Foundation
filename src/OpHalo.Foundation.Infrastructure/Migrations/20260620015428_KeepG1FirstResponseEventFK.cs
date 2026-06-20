using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpHalo.Foundation.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class KeepG1FirstResponseEventFK : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_keep_request_events_account_id_request_id",
                table: "keep_request_events");

            migrationBuilder.AddUniqueConstraint(
                name: "ak_keep_request_events_account_request_event",
                table: "keep_request_events",
                columns: new[] { "account_id", "request_id", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_keep_requests_account_id_id_first_response_event_id",
                table: "keep_requests",
                columns: new[] { "account_id", "id", "first_response_event_id" });

            migrationBuilder.AddForeignKey(
                name: "fk_keep_requests_first_response_event",
                table: "keep_requests",
                columns: new[] { "account_id", "id", "first_response_event_id" },
                principalTable: "keep_request_events",
                principalColumns: new[] { "account_id", "request_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_keep_requests_first_response_event",
                table: "keep_requests");

            migrationBuilder.DropIndex(
                name: "ix_keep_requests_account_id_id_first_response_event_id",
                table: "keep_requests");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_keep_request_events_account_request_event",
                table: "keep_request_events");

            migrationBuilder.CreateIndex(
                name: "ix_keep_request_events_account_id_request_id",
                table: "keep_request_events",
                columns: new[] { "account_id", "request_id" });
        }
    }
}

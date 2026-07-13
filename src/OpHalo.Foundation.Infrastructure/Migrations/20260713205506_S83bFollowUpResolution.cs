using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpHalo.Foundation.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class S83bFollowUpResolution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "follow_up_completion_reason",
                table: "keep_request_events",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "follow_up_resolution_outcome",
                table: "keep_request_events",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "follow_up_completion_reason",
                table: "keep_request_events");

            migrationBuilder.DropColumn(
                name: "follow_up_resolution_outcome",
                table: "keep_request_events");
        }
    }
}

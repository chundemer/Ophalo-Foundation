using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpHalo.Foundation.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddExternalContactEventFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "external_contact_cleared_attention",
                table: "keep_request_events",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "external_contact_direction",
                table: "keep_request_events",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "external_contact_outcome",
                table: "keep_request_events",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "external_contact_requires_follow_up",
                table: "keep_request_events",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "external_contact_set_first_response",
                table: "keep_request_events",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "external_contact_cleared_attention",
                table: "keep_request_events");

            migrationBuilder.DropColumn(
                name: "external_contact_direction",
                table: "keep_request_events");

            migrationBuilder.DropColumn(
                name: "external_contact_outcome",
                table: "keep_request_events");

            migrationBuilder.DropColumn(
                name: "external_contact_requires_follow_up",
                table: "keep_request_events");

            migrationBuilder.DropColumn(
                name: "external_contact_set_first_response",
                table: "keep_request_events");
        }
    }
}

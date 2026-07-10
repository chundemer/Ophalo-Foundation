using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpHalo.Foundation.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddServiceLocationToKeepRequest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "service_address_line1",
                table: "keep_requests",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "service_address_line2",
                table: "keep_requests",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "service_city",
                table: "keep_requests",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "service_state",
                table: "keep_requests",
                type: "character varying(2)",
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "service_zip",
                table: "keep_requests",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "service_address_line1",
                table: "keep_requests");

            migrationBuilder.DropColumn(
                name: "service_address_line2",
                table: "keep_requests");

            migrationBuilder.DropColumn(
                name: "service_city",
                table: "keep_requests");

            migrationBuilder.DropColumn(
                name: "service_state",
                table: "keep_requests");

            migrationBuilder.DropColumn(
                name: "service_zip",
                table: "keep_requests");
        }
    }
}

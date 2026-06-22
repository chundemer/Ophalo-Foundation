using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpHalo.Foundation.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class KeepG5ConcurrencyVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // G5/ADR-330: application-managed opaque concurrency token. Existing rows must each
            // receive their own nonempty value, so add the column nullable, backfill every row
            // with an independent gen_random_uuid(), then enforce NOT NULL. No database default
            // remains — new rows are always assigned Guid.NewGuid() by the entity factory.
            migrationBuilder.AddColumn<Guid>(
                name: "concurrency_version",
                table: "keep_requests",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql(
                "UPDATE keep_requests SET concurrency_version = gen_random_uuid() " +
                "WHERE concurrency_version IS NULL;");

            migrationBuilder.AlterColumn<Guid>(
                name: "concurrency_version",
                table: "keep_requests",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "concurrency_version",
                table: "keep_requests");
        }
    }
}

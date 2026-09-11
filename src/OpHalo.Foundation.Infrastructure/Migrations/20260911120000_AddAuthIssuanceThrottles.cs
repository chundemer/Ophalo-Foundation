using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OpHalo.Foundation.Infrastructure.Persistence;

#nullable disable

namespace OpHalo.Foundation.Infrastructure.Migrations
{
    /// <summary>
    /// GAP-094/BL154: PostgreSQL-authoritative fixed-window counter table backing
    /// <c>EfAuthIssuanceThrottle</c>. Hand-written, not scaffolded — the table is deliberately
    /// entity-free (pure counter state, not a domain concept; see EfAuthIssuanceThrottle's class
    /// remarks), so there is no corresponding OpHaloDbContext model change and no
    /// OpHaloDbContextModelSnapshot.cs update. The [DbContext]/[Migration] attributes below (and
    /// the absence of a *.Designer.cs) reflect that: nothing here is scaffolded from a model diff.
    /// </summary>
    [DbContext(typeof(OpHaloDbContext))]
    [Migration("20260911120000_AddAuthIssuanceThrottles")]
    public partial class AddAuthIssuanceThrottles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "auth_issuance_throttles",
                columns: table => new
                {
                    scope = table.Column<string>(type: "text", nullable: false),
                    key_hash = table.Column<string>(type: "text", nullable: false),
                    window_start_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_auth_issuance_throttles", x => new { x.scope, x.key_hash });
                });

            // Backs EfAuthIssuanceThrottle's per-acquisition prune (WHERE window_start_utc < cutoff)
            // — without this the prune is a full table scan on every successful auth request.
            migrationBuilder.CreateIndex(
                name: "ix_auth_issuance_throttles_window_start_utc",
                table: "auth_issuance_throttles",
                column: "window_start_utc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "auth_issuance_throttles");
        }
    }
}

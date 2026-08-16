using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpHalo.Foundation.Infrastructure.Migrations;

/// <inheritdoc />
public partial class RemovedProposedScopeLineSnapshotCleanupIndex : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateIndex(
            name: "ix_keep_pricebook_removed_scope_line_snapshots_removed_at_utc",
            table: "keep_pricebook_removed_scope_line_snapshots",
            column: "removed_at_utc");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ix_keep_pricebook_removed_scope_line_snapshots_removed_at_utc",
            table: "keep_pricebook_removed_scope_line_snapshots");
    }
}

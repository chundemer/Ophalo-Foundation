using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpHalo.Foundation.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class KeepPublicIntakeSlugAlias : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "keep_public_intake_slug_aliases",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    intake_link_id = table.Column<Guid>(type: "uuid", nullable: false),
                    slug = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    retired_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    deleted_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_keep_public_intake_slug_aliases", x => x.id);
                    table.ForeignKey(
                        name: "fk_keep_public_intake_slug_aliases_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_keep_public_intake_slug_aliases_keep_public_intake_links_in",
                        column: x => x.intake_link_id,
                        principalTable: "keep_public_intake_links",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_keep_public_intake_slug_aliases_account_id",
                table: "keep_public_intake_slug_aliases",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_keep_public_intake_slug_aliases_active_slug",
                table: "keep_public_intake_slug_aliases",
                column: "slug",
                unique: true,
                filter: "retired_at_utc IS NULL AND deleted_at_utc IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_keep_public_intake_slug_aliases_intake_link_id",
                table: "keep_public_intake_slug_aliases",
                column: "intake_link_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "keep_public_intake_slug_aliases");
        }
    }
}

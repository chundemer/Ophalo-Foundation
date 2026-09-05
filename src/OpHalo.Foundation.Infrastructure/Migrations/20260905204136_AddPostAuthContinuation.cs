using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpHalo.Foundation.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPostAuthContinuation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "post_auth_continuations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_account_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    client_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    device_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    issued_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    expires_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    consumed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_post_auth_continuations", x => x.id);
                    table.ForeignKey(
                        name: "fk_post_auth_continuations_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_post_auth_continuations_expires_at_utc",
                table: "post_auth_continuations",
                column: "expires_at_utc");

            migrationBuilder.CreateIndex(
                name: "ix_post_auth_continuations_token_hash",
                table: "post_auth_continuations",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_post_auth_continuations_user_id",
                table: "post_auth_continuations",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "post_auth_continuations");
        }
    }
}

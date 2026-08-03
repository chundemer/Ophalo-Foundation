using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpHalo.Foundation.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PriceBookDirectEntryFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "keep_pricebook_account_states",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    publish_lock_version = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    deleted_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_keep_pricebook_account_states", x => x.id);
                    table.ForeignKey(
                        name: "fk_keep_pricebook_account_states_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "keep_pricebook_manual_price_overrides",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    catalog_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_account_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    old_sell_price = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    new_sell_price = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    old_cost = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    new_cost = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    deleted_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_keep_pricebook_manual_price_overrides", x => x.id);
                    table.ForeignKey(
                        name: "fk_keep_pricebook_manual_price_overrides_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_keep_pricebook_manual_price_overrides_keep_pricebook_catalo",
                        columns: x => new { x.account_id, x.catalog_item_id },
                        principalTable: "keep_pricebook_catalog_items",
                        principalColumns: new[] { "account_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "keep_pricebook_versions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_number = table.Column<int>(type: "integer", nullable: false),
                    source_import_id = table.Column<Guid>(type: "uuid", nullable: true),
                    published_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    published_by_account_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    deleted_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_keep_pricebook_versions", x => x.id);
                    table.UniqueConstraint("ak_keep_pricebook_versions_account_id", x => new { x.account_id, x.id });
                    table.ForeignKey(
                        name: "fk_keep_pricebook_versions_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "keep_pricebook_version_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    price_book_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    catalog_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    display_name_snapshot = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    type_snapshot = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    unit_of_measure_snapshot = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    currency_snapshot = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    cost_snapshot = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    sell_price_snapshot = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    deleted_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_keep_pricebook_version_lines", x => x.id);
                    table.UniqueConstraint("ak_keep_pricebook_version_lines_account_id", x => new { x.account_id, x.id });
                    table.ForeignKey(
                        name: "fk_keep_pricebook_version_lines_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_keep_pricebook_version_lines_keep_pricebook_catalog_items_a",
                        columns: x => new { x.account_id, x.catalog_item_id },
                        principalTable: "keep_pricebook_catalog_items",
                        principalColumns: new[] { "account_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_keep_pricebook_version_lines_keep_pricebook_versions_accoun",
                        columns: x => new { x.account_id, x.price_book_version_id },
                        principalTable: "keep_pricebook_versions",
                        principalColumns: new[] { "account_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_keep_pricebook_catalog_items_account_id_current_price_book_",
                table: "keep_pricebook_catalog_items",
                columns: new[] { "account_id", "current_price_book_version_line_id" });

            migrationBuilder.CreateIndex(
                name: "ix_keep_pricebook_account_states_account_id",
                table: "keep_pricebook_account_states",
                column: "account_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_keep_pricebook_manual_price_overrides_account_id_catalog_item_id",
                table: "keep_pricebook_manual_price_overrides",
                columns: new[] { "account_id", "catalog_item_id" });

            migrationBuilder.CreateIndex(
                name: "ix_keep_pricebook_version_lines_account_id_catalog_item_id",
                table: "keep_pricebook_version_lines",
                columns: new[] { "account_id", "catalog_item_id" });

            migrationBuilder.CreateIndex(
                name: "ix_keep_pricebook_version_lines_account_id_price_book_version_",
                table: "keep_pricebook_version_lines",
                columns: new[] { "account_id", "price_book_version_id" });

            migrationBuilder.CreateIndex(
                name: "ix_keep_pricebook_version_lines_version_id_catalog_item_id",
                table: "keep_pricebook_version_lines",
                columns: new[] { "price_book_version_id", "catalog_item_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_keep_pricebook_versions_account_id_status",
                table: "keep_pricebook_versions",
                columns: new[] { "account_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_keep_pricebook_versions_account_id_version_number",
                table: "keep_pricebook_versions",
                columns: new[] { "account_id", "version_number" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_keep_pricebook_catalog_items_price_book_version_line_accoun",
                table: "keep_pricebook_catalog_items",
                columns: new[] { "account_id", "current_price_book_version_line_id" },
                principalTable: "keep_pricebook_version_lines",
                principalColumns: new[] { "account_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_keep_pricebook_catalog_items_price_book_version_line_accoun",
                table: "keep_pricebook_catalog_items");

            migrationBuilder.DropTable(
                name: "keep_pricebook_account_states");

            migrationBuilder.DropTable(
                name: "keep_pricebook_manual_price_overrides");

            migrationBuilder.DropTable(
                name: "keep_pricebook_version_lines");

            migrationBuilder.DropTable(
                name: "keep_pricebook_versions");

            migrationBuilder.DropIndex(
                name: "ix_keep_pricebook_catalog_items_account_id_current_price_book_",
                table: "keep_pricebook_catalog_items");
        }
    }
}

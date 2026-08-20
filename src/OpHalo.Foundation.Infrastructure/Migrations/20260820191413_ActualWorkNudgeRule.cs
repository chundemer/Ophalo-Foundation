using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpHalo.Foundation.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ActualWorkNudgeRule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "keep_pricebook_actual_work_nudge_rules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trigger_catalog_item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    trigger_offering_assembly_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    deleted_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_keep_pricebook_actual_work_nudge_rules", x => x.id);
                    table.UniqueConstraint("ak_keep_pricebook_actual_work_nudge_rules_account_id", x => new { x.account_id, x.id });
                    table.CheckConstraint("ck_keep_pricebook_actual_work_nudge_rules_exclusive_trigger", "(trigger_catalog_item_id IS NOT NULL AND trigger_offering_assembly_id IS NULL) OR (trigger_catalog_item_id IS NULL AND trigger_offering_assembly_id IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_keep_pricebook_actual_work_nudge_rules_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_keep_pricebook_actual_work_nudge_rules_catalog_item_account",
                        columns: x => new { x.account_id, x.trigger_catalog_item_id },
                        principalTable: "keep_pricebook_catalog_items",
                        principalColumns: new[] { "account_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_keep_pricebook_actual_work_nudge_rules_offering_assembly_ac",
                        columns: x => new { x.account_id, x.trigger_offering_assembly_id },
                        principalTable: "keep_pricebook_offering_assemblies",
                        principalColumns: new[] { "account_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "keep_pricebook_actual_work_nudge_suggestions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actual_work_nudge_rule_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order = table.Column<int>(type: "integer", nullable: false),
                    suggested_catalog_item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    suggested_offering_assembly_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    deleted_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_keep_pricebook_actual_work_nudge_suggestions", x => x.id);
                    table.CheckConstraint("ck_keep_pricebook_aw_nudge_suggestions_exclusive_target", "(suggested_catalog_item_id IS NOT NULL AND suggested_offering_assembly_id IS NULL) OR (suggested_catalog_item_id IS NULL AND suggested_offering_assembly_id IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_keep_pricebook_actual_work_nudge_suggestions_catalog_item_a",
                        columns: x => new { x.account_id, x.suggested_catalog_item_id },
                        principalTable: "keep_pricebook_catalog_items",
                        principalColumns: new[] { "account_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_keep_pricebook_actual_work_nudge_suggestions_keep_pricebook",
                        columns: x => new { x.account_id, x.actual_work_nudge_rule_id },
                        principalTable: "keep_pricebook_actual_work_nudge_rules",
                        principalColumns: new[] { "account_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_keep_pricebook_actual_work_nudge_suggestions_offering_assem",
                        columns: x => new { x.account_id, x.suggested_offering_assembly_id },
                        principalTable: "keep_pricebook_offering_assemblies",
                        principalColumns: new[] { "account_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_keep_aw_nudge_rules_trigger_catalog_item",
                table: "keep_pricebook_actual_work_nudge_rules",
                columns: new[] { "account_id", "trigger_catalog_item_id" },
                unique: true,
                filter: "trigger_catalog_item_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_keep_aw_nudge_rules_trigger_offering_assembly",
                table: "keep_pricebook_actual_work_nudge_rules",
                columns: new[] { "account_id", "trigger_offering_assembly_id" },
                unique: true,
                filter: "trigger_offering_assembly_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_keep_pricebook_actual_work_nudge_suggestions_account_id_act",
                table: "keep_pricebook_actual_work_nudge_suggestions",
                columns: new[] { "account_id", "actual_work_nudge_rule_id" });

            migrationBuilder.CreateIndex(
                name: "ix_keep_pricebook_actual_work_nudge_suggestions_account_id_sug",
                table: "keep_pricebook_actual_work_nudge_suggestions",
                columns: new[] { "account_id", "suggested_catalog_item_id" });

            migrationBuilder.CreateIndex(
                name: "ix_keep_pricebook_actual_work_nudge_suggestions_account_id_sug1",
                table: "keep_pricebook_actual_work_nudge_suggestions",
                columns: new[] { "account_id", "suggested_offering_assembly_id" });

            migrationBuilder.CreateIndex(
                name: "ux_keep_pricebook_aw_nudge_suggestions_rule_order",
                table: "keep_pricebook_actual_work_nudge_suggestions",
                columns: new[] { "actual_work_nudge_rule_id", "order" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "keep_pricebook_actual_work_nudge_suggestions");

            migrationBuilder.DropTable(
                name: "keep_pricebook_actual_work_nudge_rules");
        }
    }
}

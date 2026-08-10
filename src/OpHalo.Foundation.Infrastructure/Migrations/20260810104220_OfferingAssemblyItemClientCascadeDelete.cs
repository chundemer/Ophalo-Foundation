using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpHalo.Foundation.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class OfferingAssemblyItemClientCascadeDelete : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_keep_pricebook_offering_assembly_items_keep_pricebook_offer",
                table: "keep_pricebook_offering_assembly_items");

            migrationBuilder.AddForeignKey(
                name: "fk_keep_pricebook_offering_assembly_items_keep_pricebook_offer",
                table: "keep_pricebook_offering_assembly_items",
                columns: new[] { "account_id", "offering_assembly_id" },
                principalTable: "keep_pricebook_offering_assemblies",
                principalColumns: new[] { "account_id", "id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_keep_pricebook_offering_assembly_items_keep_pricebook_offer",
                table: "keep_pricebook_offering_assembly_items");

            migrationBuilder.AddForeignKey(
                name: "fk_keep_pricebook_offering_assembly_items_keep_pricebook_offer",
                table: "keep_pricebook_offering_assembly_items",
                columns: new[] { "account_id", "offering_assembly_id" },
                principalTable: "keep_pricebook_offering_assemblies",
                principalColumns: new[] { "account_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }
    }
}

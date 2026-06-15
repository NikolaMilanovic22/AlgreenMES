using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlGreenMES.Modules.Production.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RenameMaterialFieldsToEnglish : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "napomena",
                schema: "production",
                table: "stock_movements",
                newName: "notes");

            migrationBuilder.RenameColumn(
                name: "pozicija",
                schema: "production",
                table: "materials",
                newName: "location");

            migrationBuilder.RenameColumn(
                name: "naziv",
                schema: "production",
                table: "materials",
                newName: "name");

            migrationBuilder.RenameColumn(
                name: "napomena",
                schema: "production",
                table: "materials",
                newName: "notes");

            migrationBuilder.RenameColumn(
                name: "min_kolicina",
                schema: "production",
                table: "materials",
                newName: "min_quantity");

            migrationBuilder.RenameColumn(
                name: "max_kolicina",
                schema: "production",
                table: "materials",
                newName: "max_quantity");

            migrationBuilder.RenameColumn(
                name: "kod",
                schema: "production",
                table: "materials",
                newName: "code");

            migrationBuilder.RenameColumn(
                name: "kategorija",
                schema: "production",
                table: "materials",
                newName: "category");

            migrationBuilder.RenameColumn(
                name: "jedinica_mere",
                schema: "production",
                table: "materials",
                newName: "unit");

            migrationBuilder.RenameColumn(
                name: "dimenzija_z",
                schema: "production",
                table: "materials",
                newName: "dimension_z");

            migrationBuilder.RenameColumn(
                name: "dimenzija_y",
                schema: "production",
                table: "materials",
                newName: "dimension_y");

            migrationBuilder.RenameColumn(
                name: "dimenzija_x",
                schema: "production",
                table: "materials",
                newName: "dimension_x");

            migrationBuilder.RenameIndex(
                name: "ix_materials_tenant_id_kod",
                schema: "production",
                table: "materials",
                newName: "ix_materials_tenant_id_code");

            migrationBuilder.RenameIndex(
                name: "ix_materials_tenant_id_kategorija",
                schema: "production",
                table: "materials",
                newName: "ix_materials_tenant_id_category");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "notes",
                schema: "production",
                table: "stock_movements",
                newName: "napomena");

            migrationBuilder.RenameColumn(
                name: "unit",
                schema: "production",
                table: "materials",
                newName: "jedinica_mere");

            migrationBuilder.RenameColumn(
                name: "notes",
                schema: "production",
                table: "materials",
                newName: "napomena");

            migrationBuilder.RenameColumn(
                name: "name",
                schema: "production",
                table: "materials",
                newName: "naziv");

            migrationBuilder.RenameColumn(
                name: "min_quantity",
                schema: "production",
                table: "materials",
                newName: "min_kolicina");

            migrationBuilder.RenameColumn(
                name: "max_quantity",
                schema: "production",
                table: "materials",
                newName: "max_kolicina");

            migrationBuilder.RenameColumn(
                name: "location",
                schema: "production",
                table: "materials",
                newName: "pozicija");

            migrationBuilder.RenameColumn(
                name: "dimension_z",
                schema: "production",
                table: "materials",
                newName: "dimenzija_z");

            migrationBuilder.RenameColumn(
                name: "dimension_y",
                schema: "production",
                table: "materials",
                newName: "dimenzija_y");

            migrationBuilder.RenameColumn(
                name: "dimension_x",
                schema: "production",
                table: "materials",
                newName: "dimenzija_x");

            migrationBuilder.RenameColumn(
                name: "code",
                schema: "production",
                table: "materials",
                newName: "kod");

            migrationBuilder.RenameColumn(
                name: "category",
                schema: "production",
                table: "materials",
                newName: "kategorija");

            migrationBuilder.RenameIndex(
                name: "ix_materials_tenant_id_code",
                schema: "production",
                table: "materials",
                newName: "ix_materials_tenant_id_kod");

            migrationBuilder.RenameIndex(
                name: "ix_materials_tenant_id_category",
                schema: "production",
                table: "materials",
                newName: "ix_materials_tenant_id_kategorija");
        }
    }
}

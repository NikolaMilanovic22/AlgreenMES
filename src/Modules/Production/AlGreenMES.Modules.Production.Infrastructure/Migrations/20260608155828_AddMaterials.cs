using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlGreenMES.Modules.Production.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMaterials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "materials",
                schema: "production",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kod = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    naziv = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    jedinica_mere = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    dimenzija_x = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: true),
                    dimenzija_y = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: true),
                    dimenzija_z = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: true),
                    kategorija = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    min_kolicina = table.Column<int>(type: "integer", nullable: false),
                    max_kolicina = table.Column<int>(type: "integer", nullable: false),
                    pozicija = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    napomena = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_materials", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_materials_tenant_id_kategorija",
                schema: "production",
                table: "materials",
                columns: new[] { "tenant_id", "kategorija" });

            migrationBuilder.CreateIndex(
                name: "ix_materials_tenant_id_kod",
                schema: "production",
                table: "materials",
                columns: new[] { "tenant_id", "kod" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "materials",
                schema: "production");
        }
    }
}

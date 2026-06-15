using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlGreenMES.Modules.Production.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStockMovements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "stock_movements",
                schema: "production",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    material_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    total_price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    movement_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    document_reference = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    napomena = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_movements", x => x.id);
                    table.ForeignKey(
                        name: "fk_stock_movements_materials_material_id",
                        column: x => x.material_id,
                        principalSchema: "production",
                        principalTable: "materials",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_stock_movements_material_id",
                schema: "production",
                table: "stock_movements",
                column: "material_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_movements_tenant_id_document_reference",
                schema: "production",
                table: "stock_movements",
                columns: new[] { "tenant_id", "document_reference" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_movements_tenant_id_material_id",
                schema: "production",
                table: "stock_movements",
                columns: new[] { "tenant_id", "material_id" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_movements_tenant_id_movement_date",
                schema: "production",
                table: "stock_movements",
                columns: new[] { "tenant_id", "movement_date" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "stock_movements",
                schema: "production");
        }
    }
}

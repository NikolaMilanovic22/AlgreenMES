using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlGreenMES.Modules.Production.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RenameStockMovementTypeToEnglish : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // StockMovementType enum values renamed Ulaz/Izlaz -> Inflow/Outflow.
            // EF doesn't detect this (model snapshot matches because the column
            // is just string<->enum). Hand-write the data migration.
            migrationBuilder.Sql("UPDATE production.stock_movements SET type = 'Inflow' WHERE type = 'Ulaz';");
            migrationBuilder.Sql("UPDATE production.stock_movements SET type = 'Outflow' WHERE type = 'Izlaz';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE production.stock_movements SET type = 'Ulaz' WHERE type = 'Inflow';");
            migrationBuilder.Sql("UPDATE production.stock_movements SET type = 'Izlaz' WHERE type = 'Outflow';");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlGreenMES.Modules.Production.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStockMovementProcessId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "process_id",
                schema: "production",
                table: "stock_movements",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_stock_movements_process_id",
                schema: "production",
                table: "stock_movements",
                column: "process_id");

            migrationBuilder.AddForeignKey(
                name: "fk_stock_movements_processes_process_id",
                schema: "production",
                table: "stock_movements",
                column: "process_id",
                principalSchema: "production",
                principalTable: "processes",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_stock_movements_processes_process_id",
                schema: "production",
                table: "stock_movements");

            migrationBuilder.DropIndex(
                name: "ix_stock_movements_process_id",
                schema: "production",
                table: "stock_movements");

            migrationBuilder.DropColumn(
                name: "process_id",
                schema: "production",
                table: "stock_movements");
        }
    }
}

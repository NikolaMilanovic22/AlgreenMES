using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlGreenMES.Modules.Orders.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPausedByStationAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "paused_by_station_at",
                schema: "orders",
                table: "order_item_sub_processes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "paused_by_station_at",
                schema: "orders",
                table: "order_item_processes",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "paused_by_station_at",
                schema: "orders",
                table: "order_item_sub_processes");

            migrationBuilder.DropColumn(
                name: "paused_by_station_at",
                schema: "orders",
                table: "order_item_processes");
        }
    }
}

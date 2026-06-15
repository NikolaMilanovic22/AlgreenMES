using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlGreenMES.Modules.Orders.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RenamePausedByStationAtToPausedOnLogoutAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "paused_by_station_at",
                schema: "orders",
                table: "order_item_sub_processes",
                newName: "paused_on_logout_at");

            migrationBuilder.RenameColumn(
                name: "paused_by_station_at",
                schema: "orders",
                table: "order_item_processes",
                newName: "paused_on_logout_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "paused_on_logout_at",
                schema: "orders",
                table: "order_item_sub_processes",
                newName: "paused_by_station_at");

            migrationBuilder.RenameColumn(
                name: "paused_on_logout_at",
                schema: "orders",
                table: "order_item_processes",
                newName: "paused_by_station_at");
        }
    }
}

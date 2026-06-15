using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlGreenMES.Modules.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddShiftTimeTrackingConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "alarm_before_logout_minutes",
                schema: "identity",
                table: "shifts",
                type: "integer",
                nullable: false,
                defaultValue: 5);

            migrationBuilder.AddColumn<int>(
                name: "auto_logout_after_hours",
                schema: "identity",
                table: "shifts",
                type: "integer",
                nullable: false,
                defaultValue: 2);

            migrationBuilder.AddColumn<int>(
                name: "break_minutes",
                schema: "identity",
                table: "shifts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "max_overtime_hours",
                schema: "identity",
                table: "shifts",
                type: "integer",
                nullable: false,
                defaultValue: 6);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "alarm_before_logout_minutes",
                schema: "identity",
                table: "shifts");

            migrationBuilder.DropColumn(
                name: "auto_logout_after_hours",
                schema: "identity",
                table: "shifts");

            migrationBuilder.DropColumn(
                name: "break_minutes",
                schema: "identity",
                table: "shifts");

            migrationBuilder.DropColumn(
                name: "max_overtime_hours",
                schema: "identity",
                table: "shifts");
        }
    }
}

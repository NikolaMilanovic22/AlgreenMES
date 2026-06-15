using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlGreenMES.Modules.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddShiftAutoLogoutRegular : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "auto_logout_regular_minutes",
                schema: "identity",
                table: "shifts",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "auto_logout_regular_minutes",
                schema: "identity",
                table: "shifts");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlGreenMES.Modules.Tenancy.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantDisabledFeatures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "disabled_features",
                schema: "tenancy",
                table: "tenants",
                type: "text",
                nullable: false,
                defaultValue: "[]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "disabled_features",
                schema: "tenancy",
                table: "tenants");
        }
    }
}

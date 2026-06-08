using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlGreenMES.Modules.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUserRoleAssignments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "user_role_assignments",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_role_assignments", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_role_assignments_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_user_role_assignments_user_id_role",
                schema: "identity",
                table: "user_role_assignments",
                columns: new[] { "user_id", "role" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "user_role_assignments",
                schema: "identity");
        }
    }
}

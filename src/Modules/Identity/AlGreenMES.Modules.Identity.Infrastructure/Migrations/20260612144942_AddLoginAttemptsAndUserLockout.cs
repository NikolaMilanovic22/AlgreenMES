using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlGreenMES.Modules.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLoginAttemptsAndUserLockout : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "access_failed_count",
                schema: "identity",
                table: "users",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "lockout_end",
                schema: "identity",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "login_attempts",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ip_address = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    user_agent = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    succeeded = table.Column<bool>(type: "boolean", nullable: false),
                    failure_reason = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    attempted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_login_attempts", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_login_attempts_email_attempted_at",
                schema: "identity",
                table: "login_attempts",
                columns: new[] { "email", "attempted_at" });

            migrationBuilder.CreateIndex(
                name: "ix_login_attempts_tenant_attempted_at",
                schema: "identity",
                table: "login_attempts",
                columns: new[] { "tenant_id", "attempted_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "login_attempts",
                schema: "identity");

            migrationBuilder.DropColumn(
                name: "access_failed_count",
                schema: "identity",
                table: "users");

            migrationBuilder.DropColumn(
                name: "lockout_end",
                schema: "identity",
                table: "users");
        }
    }
}

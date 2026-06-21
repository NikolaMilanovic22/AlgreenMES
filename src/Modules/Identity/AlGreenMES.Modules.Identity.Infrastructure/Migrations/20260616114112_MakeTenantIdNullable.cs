using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlGreenMES.Modules.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MakeTenantIdNullable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "tenant_id",
                schema: "identity",
                table: "users",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            // Tenantless SuperAdmin rollout (Milos 16.06.2026). Existing
            // SuperAdmin accounts (e.g. the bootstrap superadmin@demo.com on
            // alblue staging) carry a tenant_id pointing at whatever tenant
            // they were seeded in. Clear it now that the column allows NULL
            // so the new login + read-only middleware can treat them as
            // truly tenantless. On databases without SA rows (algreen prod
            // as of 16.06.2026) this is a no-op zero-row UPDATE.
            // Role is stored as the enum name string (UserRole.SuperAdmin → "SuperAdmin").
            migrationBuilder.Sql(@"UPDATE identity.users SET tenant_id = NULL WHERE role = 'SuperAdmin';");

            migrationBuilder.AlterColumn<Guid>(
                name: "tenant_id",
                schema: "identity",
                table: "shifts",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "tenant_id",
                schema: "identity",
                table: "refresh_tokens",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "tenant_id",
                schema: "identity",
                table: "users",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "tenant_id",
                schema: "identity",
                table: "shifts",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "tenant_id",
                schema: "identity",
                table: "refresh_tokens",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}

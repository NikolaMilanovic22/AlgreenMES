using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlGreenMES.Modules.Orders.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderTypeEntity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "order_types",
                schema: "orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    allows_manual_processes = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order_types", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_order_types_tenant_id_code",
                schema: "orders",
                table: "order_types",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            // Seed the four default types for every existing tenant. Names default to
            // the current Serbian labels; admins can rename through /admin/order-types
            // after deploy. allows_manual_processes starts off (matches today's
            // enum-driven behavior — orders generate processes from product category).
            migrationBuilder.Sql(@"
                INSERT INTO orders.order_types (id, code, name, allows_manual_processes, is_active, tenant_id, created_at)
                SELECT gen_random_uuid(), seed.code, seed.name, false, true, t.id, NOW()
                FROM tenancy.tenants t
                CROSS JOIN (VALUES
                    ('STANDARD',  'Standardna'),
                    ('REPAIR',    'Popravka'),
                    ('COMPLAINT', 'Reklamacija'),
                    ('REWORK',    'Dorada')
                ) AS seed(code, name)
                ON CONFLICT (tenant_id, code) DO NOTHING;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "order_types",
                schema: "orders");
        }
    }
}

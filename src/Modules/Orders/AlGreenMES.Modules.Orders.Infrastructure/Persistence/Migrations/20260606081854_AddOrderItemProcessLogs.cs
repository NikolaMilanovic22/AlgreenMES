using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlGreenMES.Modules.Orders.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderItemProcessLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "order_item_process_logs",
                schema: "orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_item_process_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    start_time = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    end_time = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    duration_seconds = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order_item_process_logs", x => x.id);
                    table.ForeignKey(
                        name: "fk_order_item_process_logs_order_item_processes_order_item_pro",
                        column: x => x.order_item_process_id,
                        principalSchema: "orders",
                        principalTable: "order_item_processes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_order_item_process_logs_order_item_process_id",
                schema: "orders",
                table: "order_item_process_logs",
                column: "order_item_process_id");

            migrationBuilder.CreateIndex(
                name: "ix_order_item_process_logs_tenant_id_start_time",
                schema: "orders",
                table: "order_item_process_logs",
                columns: new[] { "tenant_id", "start_time" });

            migrationBuilder.CreateIndex(
                name: "ix_order_item_process_logs_user_id",
                schema: "orders",
                table: "order_item_process_logs",
                column: "user_id");

            // Backfill: for each existing OIP that has StartedByUserId set AND
            // no non-withdrawn sub-processes (i.e. process-level work tracked
            // via the prior Bug B short-cut), synthesize a single log spanning
            // (started_at → paused_at OR completed_at). Bojan's day-1 test
            // data benefits; subsequent pause/resume cycles will be captured
            // properly by the entity going forward.
            migrationBuilder.Sql(@"
                INSERT INTO orders.order_item_process_logs
                    (id, order_item_process_id, user_id, tenant_id,
                     start_time, end_time, duration_seconds, created_at)
                SELECT
                    gen_random_uuid(),
                    oip.id,
                    oip.started_by_user_id,
                    oip.tenant_id,
                    oip.started_at,
                    COALESCE(oip.paused_at, oip.completed_at),
                    CASE WHEN COALESCE(oip.paused_at, oip.completed_at) IS NOT NULL
                         THEN EXTRACT(EPOCH FROM (COALESCE(oip.paused_at, oip.completed_at) - oip.started_at))::int
                         ELSE NULL END,
                    NOW()
                FROM orders.order_item_processes oip
                WHERE oip.started_by_user_id IS NOT NULL
                  AND oip.started_at IS NOT NULL
                  AND NOT EXISTS (
                      SELECT 1 FROM orders.order_item_sub_processes sub
                      WHERE sub.order_item_process_id = oip.id
                        AND sub.is_withdrawn = false
                  );
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "order_item_process_logs",
                schema: "orders");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlGreenMES.Modules.Orders.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "orders");

            migrationBuilder.CreateTable(
                name: "notifications",
                schema: "orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    title = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    reference_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    reference_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_read = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notifications", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "orders",
                schema: "orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    delivery_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    priority = table.Column<int>(type: "integer", nullable: false),
                    order_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    custom_warning_days = table.Column<int>(type: "integer", nullable: true),
                    custom_critical_days = table.Column<int>(type: "integer", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_orders", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "work_sessions",
                schema: "orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    process_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    check_in_time = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    check_out_time = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    duration_minutes = table.Column<int>(type: "integer", nullable: true),
                    date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_work_sessions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "change_requests",
                schema: "orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requested_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    handled_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    handled_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    response_note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_change_requests", x => x.id);
                    table.ForeignKey(
                        name: "fk_change_requests_orders_order_id",
                        column: x => x.order_id,
                        principalSchema: "orders",
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "order_items",
                schema: "orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_category_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_order_items_orders_order_id",
                        column: x => x.order_id,
                        principalSchema: "orders",
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "order_item_processes",
                schema: "orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    process_id = table.Column<Guid>(type: "uuid", nullable: false),
                    complexity = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    complexity_overridden = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    total_duration_minutes = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    is_withdrawn = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    withdrawn_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    withdrawn_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    withdrawn_reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    blocked_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    blocked_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    block_reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    unblocked_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    unblocked_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    stopped_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    stopped_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    stopped_reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order_item_processes", x => x.id);
                    table.ForeignKey(
                        name: "fk_order_item_processes_order_items_order_item_id",
                        column: x => x.order_item_id,
                        principalSchema: "orders",
                        principalTable: "order_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "order_item_special_requests",
                schema: "orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    special_request_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order_item_special_requests", x => x.id);
                    table.ForeignKey(
                        name: "fk_order_item_special_requests_order_items_order_item_id",
                        column: x => x.order_item_id,
                        principalSchema: "orders",
                        principalTable: "order_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "order_item_sub_processes",
                schema: "orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_item_process_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sub_process_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    total_duration_minutes = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    is_withdrawn = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    withdrawn_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    withdrawn_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    withdrawn_reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    stopped_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    stopped_reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order_item_sub_processes", x => x.id);
                    table.ForeignKey(
                        name: "fk_order_item_sub_processes_order_item_processes_order_item_pr",
                        column: x => x.order_item_process_id,
                        principalSchema: "orders",
                        principalTable: "order_item_processes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "block_requests",
                schema: "orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_item_process_id = table.Column<Guid>(type: "uuid", nullable: true),
                    order_item_sub_process_id = table.Column<Guid>(type: "uuid", nullable: true),
                    requested_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    handled_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    handled_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    block_reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    rejection_note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_block_requests", x => x.id);
                    table.ForeignKey(
                        name: "fk_block_requests_order_item_processes_order_item_process_id",
                        column: x => x.order_item_process_id,
                        principalSchema: "orders",
                        principalTable: "order_item_processes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_block_requests_order_item_sub_processes_order_item_sub_proc",
                        column: x => x.order_item_sub_process_id,
                        principalSchema: "orders",
                        principalTable: "order_item_sub_processes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "order_item_sub_process_logs",
                schema: "orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_item_sub_process_id = table.Column<Guid>(type: "uuid", nullable: false),
                    work_session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    start_time = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    end_time = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    duration_minutes = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order_item_sub_process_logs", x => x.id);
                    table.ForeignKey(
                        name: "fk_order_item_sub_process_logs_order_item_sub_processes_order_",
                        column: x => x.order_item_sub_process_id,
                        principalSchema: "orders",
                        principalTable: "order_item_sub_processes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_order_item_sub_process_logs_work_sessions_work_session_id",
                        column: x => x.work_session_id,
                        principalSchema: "orders",
                        principalTable: "work_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_block_requests_order_item_process_id",
                schema: "orders",
                table: "block_requests",
                column: "order_item_process_id");

            migrationBuilder.CreateIndex(
                name: "ix_block_requests_order_item_sub_process_id",
                schema: "orders",
                table: "block_requests",
                column: "order_item_sub_process_id");

            migrationBuilder.CreateIndex(
                name: "ix_block_requests_tenant_id_status",
                schema: "orders",
                table: "block_requests",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_change_requests_order_id",
                schema: "orders",
                table: "change_requests",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "ix_change_requests_tenant_id_status",
                schema: "orders",
                table: "change_requests",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_notifications_user_id",
                schema: "orders",
                table: "notifications",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_notifications_user_id_is_read",
                schema: "orders",
                table: "notifications",
                columns: new[] { "user_id", "is_read" });

            migrationBuilder.CreateIndex(
                name: "ix_order_item_processes_order_item_id_process_id",
                schema: "orders",
                table: "order_item_processes",
                columns: new[] { "order_item_id", "process_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_order_item_processes_tenant_id_status",
                schema: "orders",
                table: "order_item_processes",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_order_item_special_requests_order_item_id_special_request_t",
                schema: "orders",
                table: "order_item_special_requests",
                columns: new[] { "order_item_id", "special_request_type_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_order_item_sub_process_logs_order_item_sub_process_id",
                schema: "orders",
                table: "order_item_sub_process_logs",
                column: "order_item_sub_process_id");

            migrationBuilder.CreateIndex(
                name: "ix_order_item_sub_process_logs_work_session_id",
                schema: "orders",
                table: "order_item_sub_process_logs",
                column: "work_session_id");

            migrationBuilder.CreateIndex(
                name: "ix_order_item_sub_processes_order_item_process_id_sub_process_",
                schema: "orders",
                table: "order_item_sub_processes",
                columns: new[] { "order_item_process_id", "sub_process_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_order_items_order_id",
                schema: "orders",
                table: "order_items",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "ix_orders_tenant_id_delivery_date",
                schema: "orders",
                table: "orders",
                columns: new[] { "tenant_id", "delivery_date" });

            migrationBuilder.CreateIndex(
                name: "ix_orders_tenant_id_order_number",
                schema: "orders",
                table: "orders",
                columns: new[] { "tenant_id", "order_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_orders_tenant_id_priority",
                schema: "orders",
                table: "orders",
                columns: new[] { "tenant_id", "priority" });

            migrationBuilder.CreateIndex(
                name: "ix_orders_tenant_id_status",
                schema: "orders",
                table: "orders",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_work_sessions_process_id",
                schema: "orders",
                table: "work_sessions",
                column: "process_id");

            migrationBuilder.CreateIndex(
                name: "ix_work_sessions_tenant_id_date",
                schema: "orders",
                table: "work_sessions",
                columns: new[] { "tenant_id", "date" });

            migrationBuilder.CreateIndex(
                name: "ix_work_sessions_user_id",
                schema: "orders",
                table: "work_sessions",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "block_requests",
                schema: "orders");

            migrationBuilder.DropTable(
                name: "change_requests",
                schema: "orders");

            migrationBuilder.DropTable(
                name: "notifications",
                schema: "orders");

            migrationBuilder.DropTable(
                name: "order_item_special_requests",
                schema: "orders");

            migrationBuilder.DropTable(
                name: "order_item_sub_process_logs",
                schema: "orders");

            migrationBuilder.DropTable(
                name: "order_item_sub_processes",
                schema: "orders");

            migrationBuilder.DropTable(
                name: "work_sessions",
                schema: "orders");

            migrationBuilder.DropTable(
                name: "order_item_processes",
                schema: "orders");

            migrationBuilder.DropTable(
                name: "order_items",
                schema: "orders");

            migrationBuilder.DropTable(
                name: "orders",
                schema: "orders");
        }
    }
}

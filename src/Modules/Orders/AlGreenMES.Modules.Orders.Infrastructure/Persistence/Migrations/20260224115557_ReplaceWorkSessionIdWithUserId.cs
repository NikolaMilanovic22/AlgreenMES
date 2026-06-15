using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlGreenMES.Modules.Orders.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceWorkSessionIdWithUserId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_order_item_sub_process_logs_work_sessions_work_session_id",
                schema: "orders",
                table: "order_item_sub_process_logs");

            migrationBuilder.RenameColumn(
                name: "work_session_id",
                schema: "orders",
                table: "order_item_sub_process_logs",
                newName: "user_id");

            migrationBuilder.RenameIndex(
                name: "ix_order_item_sub_process_logs_work_session_id",
                schema: "orders",
                table: "order_item_sub_process_logs",
                newName: "ix_order_item_sub_process_logs_user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "user_id",
                schema: "orders",
                table: "order_item_sub_process_logs",
                newName: "work_session_id");

            migrationBuilder.RenameIndex(
                name: "ix_order_item_sub_process_logs_user_id",
                schema: "orders",
                table: "order_item_sub_process_logs",
                newName: "ix_order_item_sub_process_logs_work_session_id");

            migrationBuilder.AddForeignKey(
                name: "fk_order_item_sub_process_logs_work_sessions_work_session_id",
                schema: "orders",
                table: "order_item_sub_process_logs",
                column: "work_session_id",
                principalSchema: "orders",
                principalTable: "work_sessions",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}

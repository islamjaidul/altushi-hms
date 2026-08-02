using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hms.Hr.Data.Migrations
{
    /// <inheritdoc />
    public partial class HrPayrollIntegrity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddForeignKey(
                name: "fk_payroll_component_line_payroll_line_payroll_line_id",
                schema: "hr",
                table: "payroll_component_line",
                column: "payroll_line_id",
                principalSchema: "hr",
                principalTable: "payroll_line",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_payroll_component_line_payroll_run_run_id",
                schema: "hr",
                table: "payroll_component_line",
                column: "run_id",
                principalSchema: "hr",
                principalTable: "payroll_run",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_payroll_line_employee_employee_id",
                schema: "hr",
                table: "payroll_line",
                column: "employee_id",
                principalSchema: "hr",
                principalTable: "employee",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_payroll_line_payroll_run_run_id",
                schema: "hr",
                table: "payroll_line",
                column: "run_id",
                principalSchema: "hr",
                principalTable: "payroll_run",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_payslip_employee_employee_id",
                schema: "hr",
                table: "payslip",
                column: "employee_id",
                principalSchema: "hr",
                principalTable: "employee",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_payslip_payroll_line_payroll_line_id",
                schema: "hr",
                table: "payslip",
                column: "payroll_line_id",
                principalSchema: "hr",
                principalTable: "payroll_line",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_punch_punch_import_batches_import_batch_id",
                schema: "hr",
                table: "punch",
                column: "import_batch_id",
                principalSchema: "hr",
                principalTable: "punch_import_batch",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_payroll_component_line_payroll_line_payroll_line_id",
                schema: "hr",
                table: "payroll_component_line");

            migrationBuilder.DropForeignKey(
                name: "fk_payroll_component_line_payroll_run_run_id",
                schema: "hr",
                table: "payroll_component_line");

            migrationBuilder.DropForeignKey(
                name: "fk_payroll_line_employee_employee_id",
                schema: "hr",
                table: "payroll_line");

            migrationBuilder.DropForeignKey(
                name: "fk_payroll_line_payroll_run_run_id",
                schema: "hr",
                table: "payroll_line");

            migrationBuilder.DropForeignKey(
                name: "fk_payslip_employee_employee_id",
                schema: "hr",
                table: "payslip");

            migrationBuilder.DropForeignKey(
                name: "fk_payslip_payroll_line_payroll_line_id",
                schema: "hr",
                table: "payslip");

            migrationBuilder.DropForeignKey(
                name: "fk_punch_punch_import_batches_import_batch_id",
                schema: "hr",
                table: "punch");
        }
    }
}

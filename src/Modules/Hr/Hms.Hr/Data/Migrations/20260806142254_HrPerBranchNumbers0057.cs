using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hms.Hr.Data.Migrations
{
    /// <inheritdoc />
    public partial class HrPerBranchNumbers0057 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_payslip_payslip_no",
                schema: "hr",
                table: "payslip");

            migrationBuilder.DropIndex(
                name: "ix_payroll_run_run_no",
                schema: "hr",
                table: "payroll_run");

            migrationBuilder.DropIndex(
                name: "ix_leave_application_application_no",
                schema: "hr",
                table: "leave_application");

            migrationBuilder.DropIndex(
                name: "ix_employee_employee_code",
                schema: "hr",
                table: "employee");

            migrationBuilder.CreateIndex(
                name: "ix_payslip_branch_id_payslip_no",
                schema: "hr",
                table: "payslip",
                columns: new[] { "branch_id", "payslip_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payroll_run_branch_id_run_no",
                schema: "hr",
                table: "payroll_run",
                columns: new[] { "branch_id", "run_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_leave_application_branch_id_application_no",
                schema: "hr",
                table: "leave_application",
                columns: new[] { "branch_id", "application_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_employee_branch_id_employee_code",
                schema: "hr",
                table: "employee",
                columns: new[] { "branch_id", "employee_code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_payslip_branch_id_payslip_no",
                schema: "hr",
                table: "payslip");

            migrationBuilder.DropIndex(
                name: "ix_payroll_run_branch_id_run_no",
                schema: "hr",
                table: "payroll_run");

            migrationBuilder.DropIndex(
                name: "ix_leave_application_branch_id_application_no",
                schema: "hr",
                table: "leave_application");

            migrationBuilder.DropIndex(
                name: "ix_employee_branch_id_employee_code",
                schema: "hr",
                table: "employee");

            migrationBuilder.CreateIndex(
                name: "ix_payslip_payslip_no",
                schema: "hr",
                table: "payslip",
                column: "payslip_no",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payroll_run_run_no",
                schema: "hr",
                table: "payroll_run",
                column: "run_no",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_leave_application_application_no",
                schema: "hr",
                table: "leave_application",
                column: "application_no",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_employee_employee_code",
                schema: "hr",
                table: "employee",
                column: "employee_code",
                unique: true);
        }
    }
}

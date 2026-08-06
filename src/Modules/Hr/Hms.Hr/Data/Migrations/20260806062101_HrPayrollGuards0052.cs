using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hms.Hr.Data.Migrations
{
    /// <summary>
    /// Spec 0052 WP3: a locked run may be reversed once. <c>ReverseAsync</c> checked first and then
    /// inserted, which is a race; the row lock added to <c>PayrollService.LoadAsync</c> serialises
    /// the transitions and this index makes the invariant a database fact rather than a consequence
    /// of timing. Additive under 03 §12 — one partial unique index, no column or table touched.
    /// </summary>
    public partial class HrPayrollGuards0052 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_payroll_run_reversal_of_run_id",
                schema: "hr",
                table: "payroll_run",
                column: "reversal_of_run_id",
                unique: true,
                filter: "reversal_of_run_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_payroll_run_reversal_of_run_id",
                schema: "hr",
                table: "payroll_run");
        }
    }
}

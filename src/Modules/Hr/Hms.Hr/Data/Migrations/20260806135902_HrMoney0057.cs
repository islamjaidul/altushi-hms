using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Hms.Hr.Data.Migrations
{
    /// <inheritdoc />
    public partial class HrMoney0057 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_loan_loan_no",
                schema: "hr",
                table: "loan");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "disbursed_at",
                schema: "hr",
                table: "payroll_run",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "variance_reviewed_at",
                schema: "hr",
                table: "payroll_run",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "variance_reviewed_by",
                schema: "hr",
                table: "payroll_run",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "variance_toleration_bp",
                schema: "hr",
                table: "payroll_policy",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "held",
                schema: "hr",
                table: "payroll_line",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "hold_reason",
                schema: "hr",
                table: "payroll_line",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "approved_at",
                schema: "hr",
                table: "loan",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "approved_by",
                schema: "hr",
                table: "loan",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "closed_at",
                schema: "hr",
                table: "loan",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "created_by_name",
                schema: "hr",
                table: "loan",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "disbursed_at",
                schema: "hr",
                table: "loan",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "disbursed_by",
                schema: "hr",
                table: "loan",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "purpose",
                schema: "hr",
                table: "loan",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "write_off_reason",
                schema: "hr",
                table: "loan",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "payment_method",
                schema: "hr",
                table: "employee",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "bonus_sheet",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    sheet_no = table.Column<string>(type: "text", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    title = table.Column<string>(type: "text", nullable: false),
                    period = table.Column<DateOnly>(type: "date", nullable: false),
                    state = table.Column<string>(type: "text", nullable: false),
                    based_on_component_id = table.Column<long>(type: "bigint", nullable: true),
                    percent_bp = table.Column<long>(type: "bigint", nullable: false),
                    flat_amount_taka = table.Column<long>(type: "bigint", nullable: false),
                    minimum_service_months = table.Column<int>(type: "integer", nullable: false),
                    eligible_types = table.Column<string>(type: "text", nullable: false),
                    total_taka = table.Column<long>(type: "bigint", nullable: false),
                    eligible_count = table.Column<int>(type: "integer", nullable: false),
                    excluded_count = table.Column<int>(type: "integer", nullable: false),
                    approval_request_id = table.Column<long>(type: "bigint", nullable: true),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    approved_by = table.Column<long>(type: "bigint", nullable: true),
                    paid_run_id = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false),
                    created_by_name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bonus_sheet", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "compensation_run",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    run_no = table.Column<string>(type: "text", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    title = table.Column<string>(type: "text", nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    state = table.Column<string>(type: "text", nullable: false),
                    org_unit_id = table.Column<long>(type: "bigint", nullable: true),
                    grade_id = table.Column<long>(type: "bigint", nullable: true),
                    designation_id = table.Column<long>(type: "bigint", nullable: true),
                    minimum_service_months = table.Column<int>(type: "integer", nullable: false),
                    percent_bp = table.Column<long>(type: "bigint", nullable: false),
                    flat_amount_taka = table.Column<long>(type: "bigint", nullable: false),
                    new_designation_id = table.Column<long>(type: "bigint", nullable: true),
                    new_grade_id = table.Column<long>(type: "bigint", nullable: true),
                    affected_count = table.Column<int>(type: "integer", nullable: false),
                    total_increase_taka = table.Column<long>(type: "bigint", nullable: false),
                    approval_request_id = table.Column<long>(type: "bigint", nullable: true),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    approved_by = table.Column<long>(type: "bigint", nullable: true),
                    applied_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    applied_by = table.Column<long>(type: "bigint", nullable: true),
                    letters_issued = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false),
                    created_by_name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_compensation_run", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "disbursement",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    run_id = table.Column<long>(type: "bigint", nullable: false),
                    batch_no = table.Column<string>(type: "text", nullable: false),
                    state = table.Column<string>(type: "text", nullable: false),
                    value_date = table.Column<DateOnly>(type: "date", nullable: false),
                    total_taka = table.Column<long>(type: "bigint", nullable: false),
                    bank_taka = table.Column<long>(type: "bigint", nullable: false),
                    cash_taka = table.Column<long>(type: "bigint", nullable: false),
                    cheque_taka = table.Column<long>(type: "bigint", nullable: false),
                    line_count = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false),
                    created_by_name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_disbursement", x => x.id);
                    table.ForeignKey(
                        name: "fk_disbursement_payroll_run_run_id",
                        column: x => x.run_id,
                        principalSchema: "hr",
                        principalTable: "payroll_run",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "salary_hold",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    employee_id = table.Column<long>(type: "bigint", nullable: false),
                    reason = table.Column<string>(type: "text", nullable: false),
                    held_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    held_by = table.Column<long>(type: "bigint", nullable: false),
                    held_by_name = table.Column<string>(type: "text", nullable: false),
                    released_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    released_by = table.Column<long>(type: "bigint", nullable: true),
                    release_reason = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_salary_hold", x => x.id);
                    table.ForeignKey(
                        name: "fk_salary_hold_employee_employee_id",
                        column: x => x.employee_id,
                        principalSchema: "hr",
                        principalTable: "employee",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "variance_note",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    run_id = table.Column<long>(type: "bigint", nullable: false),
                    employee_id = table.Column<long>(type: "bigint", nullable: false),
                    previous_net_taka = table.Column<long>(type: "bigint", nullable: false),
                    current_net_taka = table.Column<long>(type: "bigint", nullable: false),
                    delta_bp = table.Column<int>(type: "integer", nullable: false),
                    reason = table.Column<string>(type: "text", nullable: true),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reviewed_by = table.Column<long>(type: "bigint", nullable: true),
                    reviewed_by_name = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_variance_note", x => x.id);
                    table.ForeignKey(
                        name: "fk_variance_note_payroll_run_run_id",
                        column: x => x.run_id,
                        principalSchema: "hr",
                        principalTable: "payroll_run",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "bonus_line",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    sheet_id = table.Column<long>(type: "bigint", nullable: false),
                    employee_id = table.Column<long>(type: "bigint", nullable: false),
                    eligible = table.Column<bool>(type: "boolean", nullable: false),
                    amount_taka = table.Column<long>(type: "bigint", nullable: false),
                    basis = table.Column<string>(type: "text", nullable: true),
                    exclusion_reason = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bonus_line", x => x.id);
                    table.ForeignKey(
                        name: "fk_bonus_line_bonus_sheet_sheet_id",
                        column: x => x.sheet_id,
                        principalSchema: "hr",
                        principalTable: "bonus_sheet",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "compensation_line",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    run_id = table.Column<long>(type: "bigint", nullable: false),
                    employee_id = table.Column<long>(type: "bigint", nullable: false),
                    old_gross_taka = table.Column<long>(type: "bigint", nullable: false),
                    new_gross_taka = table.Column<long>(type: "bigint", nullable: false),
                    old_designation_id = table.Column<long>(type: "bigint", nullable: true),
                    new_designation_id = table.Column<long>(type: "bigint", nullable: true),
                    old_grade_id = table.Column<long>(type: "bigint", nullable: true),
                    new_grade_id = table.Column<long>(type: "bigint", nullable: true),
                    basis = table.Column<string>(type: "text", nullable: true),
                    applied = table.Column<bool>(type: "boolean", nullable: false),
                    skipped = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_compensation_line", x => x.id);
                    table.ForeignKey(
                        name: "fk_compensation_line_compensation_run_run_id",
                        column: x => x.run_id,
                        principalSchema: "hr",
                        principalTable: "compensation_run",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "disbursement_line",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    disbursement_id = table.Column<long>(type: "bigint", nullable: false),
                    payroll_line_id = table.Column<long>(type: "bigint", nullable: false),
                    employee_id = table.Column<long>(type: "bigint", nullable: false),
                    employee_code = table.Column<string>(type: "text", nullable: false),
                    employee_name = table.Column<string>(type: "text", nullable: false),
                    method = table.Column<string>(type: "text", nullable: false),
                    amount_taka = table.Column<long>(type: "bigint", nullable: false),
                    bank_name = table.Column<string>(type: "text", nullable: true),
                    bank_account_no = table.Column<string>(type: "text", nullable: true),
                    bank_routing_no = table.Column<string>(type: "text", nullable: true),
                    reference = table.Column<string>(type: "text", nullable: true),
                    paid_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    paid_by = table.Column<long>(type: "bigint", nullable: true),
                    paid_by_name = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_disbursement_line", x => x.id);
                    table.ForeignKey(
                        name: "fk_disbursement_line_disbursement_disbursement_id",
                        column: x => x.disbursement_id,
                        principalSchema: "hr",
                        principalTable: "disbursement",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_disbursement_line_payroll_line_payroll_line_id",
                        column: x => x.payroll_line_id,
                        principalSchema: "hr",
                        principalTable: "payroll_line",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_loan_branch_id_loan_no",
                schema: "hr",
                table: "loan",
                columns: new[] { "branch_id", "loan_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_bonus_line_sheet_id_employee_id",
                schema: "hr",
                table: "bonus_line",
                columns: new[] { "sheet_id", "employee_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_bonus_sheet_branch_id_period_state",
                schema: "hr",
                table: "bonus_sheet",
                columns: new[] { "branch_id", "period", "state" });

            migrationBuilder.CreateIndex(
                name: "ix_bonus_sheet_branch_id_sheet_no",
                schema: "hr",
                table: "bonus_sheet",
                columns: new[] { "branch_id", "sheet_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_compensation_line_run_id_employee_id",
                schema: "hr",
                table: "compensation_line",
                columns: new[] { "run_id", "employee_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_compensation_run_branch_id_effective_from_state",
                schema: "hr",
                table: "compensation_run",
                columns: new[] { "branch_id", "effective_from", "state" });

            migrationBuilder.CreateIndex(
                name: "ix_compensation_run_branch_id_run_no",
                schema: "hr",
                table: "compensation_run",
                columns: new[] { "branch_id", "run_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_disbursement_branch_id_batch_no",
                schema: "hr",
                table: "disbursement",
                columns: new[] { "branch_id", "batch_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_disbursement_run_id",
                schema: "hr",
                table: "disbursement",
                column: "run_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_disbursement_line_disbursement_id_employee_id",
                schema: "hr",
                table: "disbursement_line",
                columns: new[] { "disbursement_id", "employee_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_disbursement_line_payroll_line_id",
                schema: "hr",
                table: "disbursement_line",
                column: "payroll_line_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_salary_hold_branch_id_released_at",
                schema: "hr",
                table: "salary_hold",
                columns: new[] { "branch_id", "released_at" });

            migrationBuilder.CreateIndex(
                name: "ix_salary_hold_employee_id",
                schema: "hr",
                table: "salary_hold",
                column: "employee_id",
                unique: true,
                filter: "released_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_variance_note_run_id_employee_id",
                schema: "hr",
                table: "variance_note",
                columns: new[] { "run_id", "employee_id" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_loan_employee_employee_id",
                schema: "hr",
                table: "loan",
                column: "employee_id",
                principalSchema: "hr",
                principalTable: "employee",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            // ---- Invariants EF cannot express (spec 0057). Money arithmetic that cannot be wrong
            // beats money arithmetic that is checked.

            // A disbursement's split must add up to its total, and every line must be positive —
            // a negative payment is a recovery, and a recovery belongs on the payslip, not here.
            migrationBuilder.Sql("""
                ALTER TABLE hr.disbursement
                ADD CONSTRAINT ck_disbursement_split
                    CHECK (total_taka = bank_taka + cash_taka + cheque_taka);

                ALTER TABLE hr.disbursement_line
                ADD CONSTRAINT ck_disbursement_line_positive
                    CHECK (amount_taka > 0);
                """);

            // A bonus line is either eligible with an amount, or excluded with a reason. "Excluded
            // and silent" is how a person gets forgotten rather than assessed.
            migrationBuilder.Sql("""
                ALTER TABLE hr.bonus_line
                ADD CONSTRAINT ck_bonus_line_decided
                    CHECK ((eligible AND amount_taka >= 0)
                           OR (NOT eligible AND exclusion_reason IS NOT NULL));
                """);

            // Recovery can never exceed what was lent, and an installment count of zero would make
            // the recovery schedule undefined.
            migrationBuilder.Sql("""
                ALTER TABLE hr.loan
                ADD CONSTRAINT ck_loan_recovery
                    CHECK (recovered_taka >= 0 AND recovered_taka <= principal_taka);

                ALTER TABLE hr.loan
                ADD CONSTRAINT ck_loan_schedule
                    CHECK (principal_taka > 0 AND installment_count > 0 AND installment_taka > 0);
                """);

            // A compensation line's new pay is what will be written. Zero would silently unpay
            // somebody on the day an increment applies.
            migrationBuilder.Sql("""
                ALTER TABLE hr.compensation_line
                ADD CONSTRAINT ck_compensation_line_new_pay
                    CHECK (new_gross_taka > 0);
                """);

            // The variance tolerance is a percentage in basis points; a negative one would invert
            // the comparison and clear every out-of-range line silently.
            migrationBuilder.Sql("""
                ALTER TABLE hr.payroll_policy
                ADD CONSTRAINT ck_payroll_policy_variance
                    CHECK (variance_toleration_bp >= 0);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_loan_employee_employee_id",
                schema: "hr",
                table: "loan");

            migrationBuilder.DropTable(
                name: "bonus_line",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "compensation_line",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "disbursement_line",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "salary_hold",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "variance_note",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "bonus_sheet",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "compensation_run",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "disbursement",
                schema: "hr");

            migrationBuilder.DropIndex(
                name: "ix_loan_branch_id_loan_no",
                schema: "hr",
                table: "loan");

            migrationBuilder.DropColumn(
                name: "disbursed_at",
                schema: "hr",
                table: "payroll_run");

            migrationBuilder.DropColumn(
                name: "variance_reviewed_at",
                schema: "hr",
                table: "payroll_run");

            migrationBuilder.DropColumn(
                name: "variance_reviewed_by",
                schema: "hr",
                table: "payroll_run");

            migrationBuilder.DropColumn(
                name: "variance_toleration_bp",
                schema: "hr",
                table: "payroll_policy");

            migrationBuilder.DropColumn(
                name: "held",
                schema: "hr",
                table: "payroll_line");

            migrationBuilder.DropColumn(
                name: "hold_reason",
                schema: "hr",
                table: "payroll_line");

            migrationBuilder.DropColumn(
                name: "approved_at",
                schema: "hr",
                table: "loan");

            migrationBuilder.DropColumn(
                name: "approved_by",
                schema: "hr",
                table: "loan");

            migrationBuilder.DropColumn(
                name: "closed_at",
                schema: "hr",
                table: "loan");

            migrationBuilder.DropColumn(
                name: "created_by_name",
                schema: "hr",
                table: "loan");

            migrationBuilder.DropColumn(
                name: "disbursed_at",
                schema: "hr",
                table: "loan");

            migrationBuilder.DropColumn(
                name: "disbursed_by",
                schema: "hr",
                table: "loan");

            migrationBuilder.DropColumn(
                name: "purpose",
                schema: "hr",
                table: "loan");

            migrationBuilder.DropColumn(
                name: "write_off_reason",
                schema: "hr",
                table: "loan");

            migrationBuilder.DropColumn(
                name: "payment_method",
                schema: "hr",
                table: "employee");

            migrationBuilder.CreateIndex(
                name: "ix_loan_loan_no",
                schema: "hr",
                table: "loan",
                column: "loan_no",
                unique: true);
        }
    }
}

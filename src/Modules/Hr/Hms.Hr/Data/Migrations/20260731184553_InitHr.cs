using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Hms.Hr.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitHr : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "hr");

            migrationBuilder.CreateTable(
                name: "attendance_correction",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    attendance_day_id = table.Column<long>(type: "bigint", nullable: false),
                    from_status = table.Column<string>(type: "text", nullable: false),
                    to_status = table.Column<string>(type: "text", nullable: false),
                    from_worked_minutes = table.Column<int>(type: "integer", nullable: true),
                    to_worked_minutes = table.Column<int>(type: "integer", nullable: true),
                    reason = table.Column<string>(type: "text", nullable: false),
                    arrears_for_run_id = table.Column<long>(type: "bigint", nullable: true),
                    arrears_settled = table.Column<bool>(type: "boolean", nullable: false),
                    corrected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    corrected_by = table.Column<long>(type: "bigint", nullable: false),
                    corrected_by_name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_attendance_correction", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "attendance_day",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    employee_id = table.Column<long>(type: "bigint", nullable: false),
                    on_date = table.Column<DateOnly>(type: "date", nullable: false),
                    shift_id = table.Column<long>(type: "bigint", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false),
                    first_in = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_out = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    worked_minutes = table.Column<int>(type: "integer", nullable: false),
                    late_minutes = table.Column<int>(type: "integer", nullable: false),
                    early_out_minutes = table.Column<int>(type: "integer", nullable: false),
                    overtime_minutes = table.Column<int>(type: "integer", nullable: false),
                    payable_fraction_bp = table.Column<int>(type: "integer", nullable: false),
                    leave_application_id = table.Column<long>(type: "bigint", nullable: true),
                    corrected = table.Column<bool>(type: "boolean", nullable: false),
                    derived_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_attendance_day", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "deduction_rule",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    per_absent_day_bp = table.Column<int>(type: "integer", nullable: false),
                    per_leave_without_pay_day_bp = table.Column<int>(type: "integer", nullable: false),
                    based_on_component_id = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_deduction_rule", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "designation",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    code = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_designation", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "employee",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    employee_code = table.Column<string>(type: "text", nullable: false),
                    person_ref = table.Column<string>(type: "text", nullable: true),
                    full_name = table.Column<string>(type: "text", nullable: false),
                    father_name = table.Column<string>(type: "text", nullable: true),
                    mother_name = table.Column<string>(type: "text", nullable: true),
                    date_of_birth = table.Column<DateOnly>(type: "date", nullable: true),
                    gender = table.Column<string>(type: "text", nullable: true),
                    national_id = table.Column<string>(type: "text", nullable: true),
                    phone = table.Column<string>(type: "text", nullable: true),
                    email = table.Column<string>(type: "text", nullable: true),
                    present_address = table.Column<string>(type: "text", nullable: true),
                    permanent_address = table.Column<string>(type: "text", nullable: true),
                    blood_group = table.Column<string>(type: "text", nullable: true),
                    emergency_contact = table.Column<string>(type: "text", nullable: true),
                    joined_on = table.Column<DateOnly>(type: "date", nullable: false),
                    confirmed_on = table.Column<DateOnly>(type: "date", nullable: true),
                    separated_on = table.Column<DateOnly>(type: "date", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false),
                    bank_name = table.Column<string>(type: "text", nullable: true),
                    bank_branch = table.Column<string>(type: "text", nullable: true),
                    bank_account_no = table.Column<string>(type: "text", nullable: true),
                    bank_routing_no = table.Column<string>(type: "text", nullable: true),
                    tin = table.Column<string>(type: "text", nullable: true),
                    user_ref = table.Column<string>(type: "text", nullable: true),
                    documents_json = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employee", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "employee_assignment",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    employee_id = table.Column<long>(type: "bigint", nullable: false),
                    org_unit_id = table.Column<long>(type: "bigint", nullable: false),
                    designation_id = table.Column<long>(type: "bigint", nullable: false),
                    grade_id = table.Column<long>(type: "bigint", nullable: false),
                    work_location_id = table.Column<long>(type: "bigint", nullable: true),
                    reports_to_employee_id = table.Column<long>(type: "bigint", nullable: true),
                    weekly_off_pattern_id = table.Column<long>(type: "bigint", nullable: true),
                    holiday_calendar_id = table.Column<long>(type: "bigint", nullable: true),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    reason = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employee_assignment", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "employee_ledger_entry",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    employee_id = table.Column<long>(type: "bigint", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    on_date = table.Column<DateOnly>(type: "date", nullable: false),
                    narration = table.Column<string>(type: "text", nullable: false),
                    employee_share_taka = table.Column<long>(type: "bigint", nullable: false),
                    employer_share_taka = table.Column<long>(type: "bigint", nullable: false),
                    payroll_run_id = table.Column<long>(type: "bigint", nullable: true),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_by = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employee_ledger_entry", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "employee_pay_component",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    pay_structure_id = table.Column<long>(type: "bigint", nullable: false),
                    component_id = table.Column<long>(type: "bigint", nullable: false),
                    amount_taka = table.Column<long>(type: "bigint", nullable: false),
                    percent_bp_override = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employee_pay_component", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "employee_pay_structure",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    employee_id = table.Column<long>(type: "bigint", nullable: false),
                    pay_scale_id = table.Column<long>(type: "bigint", nullable: true),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    reason = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employee_pay_structure", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "employment_event",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    employee_id = table.Column<long>(type: "bigint", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    on_date = table.Column<DateOnly>(type: "date", nullable: false),
                    note = table.Column<string>(type: "text", nullable: true),
                    detail_json = table.Column<string>(type: "jsonb", nullable: true),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_by = table.Column<long>(type: "bigint", nullable: false),
                    recorded_by_name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employment_event", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "grace_time_rule",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    grace_minutes = table.Column<int>(type: "integer", nullable: false),
                    free_late_count_per_month = table.Column<int>(type: "integer", nullable: false),
                    deduction_per_late_bp = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_grace_time_rule", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "grade",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    code = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    rank = table.Column<int>(type: "integer", nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_grade", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "gratuity_rule",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    minimum_service_months = table.Column<int>(type: "integer", nullable: false),
                    days_per_year_bp = table.Column<int>(type: "integer", nullable: false),
                    based_on_component_id = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_gratuity_rule", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "holiday",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    calendar_id = table.Column<long>(type: "bigint", nullable: false),
                    on_date = table.Column<DateOnly>(type: "date", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    half_day = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_holiday", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "holiday_calendar",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_holiday_calendar", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "holiday_pay_policy",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    multiplier_bp = table.Column<long>(type: "bigint", nullable: false),
                    grants_comp_off = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_holiday_pay_policy", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "leave_application",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    application_no = table.Column<string>(type: "text", nullable: false),
                    employee_id = table.Column<long>(type: "bigint", nullable: false),
                    leave_type_id = table.Column<long>(type: "bigint", nullable: false),
                    from_date = table.Column<DateOnly>(type: "date", nullable: false),
                    to_date = table.Column<DateOnly>(type: "date", nullable: false),
                    days_bp = table.Column<int>(type: "integer", nullable: false),
                    reason = table.Column<string>(type: "text", nullable: false),
                    attachment_path = table.Column<string>(type: "text", nullable: true),
                    state = table.Column<string>(type: "text", nullable: false),
                    recommended_by = table.Column<long>(type: "bigint", nullable: true),
                    recommended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    decided_by = table.Column<long>(type: "bigint", nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    decision_note = table.Column<string>(type: "text", nullable: true),
                    approval_request_id = table.Column<long>(type: "bigint", nullable: true),
                    applied_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    applied_by = table.Column<long>(type: "bigint", nullable: false),
                    applied_by_name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_leave_application", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "leave_balance",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    employee_id = table.Column<long>(type: "bigint", nullable: false),
                    leave_type_id = table.Column<long>(type: "bigint", nullable: false),
                    leave_year = table.Column<int>(type: "integer", nullable: false),
                    opening_bp = table.Column<int>(type: "integer", nullable: false),
                    accrued_bp = table.Column<int>(type: "integer", nullable: false),
                    availed_bp = table.Column<int>(type: "integer", nullable: false),
                    encashed_bp = table.Column<int>(type: "integer", nullable: false),
                    adjustment_bp = table.Column<int>(type: "integer", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_leave_balance", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "leave_encashment",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    employee_id = table.Column<long>(type: "bigint", nullable: false),
                    leave_type_id = table.Column<long>(type: "bigint", nullable: false),
                    leave_year = table.Column<int>(type: "integer", nullable: false),
                    days_bp = table.Column<int>(type: "integer", nullable: false),
                    amount_taka = table.Column<long>(type: "bigint", nullable: false),
                    payroll_run_id = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_leave_encashment", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "leave_policy",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    leave_type_id = table.Column<long>(type: "bigint", nullable: false),
                    grade_id = table.Column<long>(type: "bigint", nullable: true),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    annual_entitlement_bp = table.Column<int>(type: "integer", nullable: false),
                    accrual_per_month_bp = table.Column<int>(type: "integer", nullable: false),
                    max_carry_forward_bp = table.Column<int>(type: "integer", nullable: false),
                    encashable = table.Column<bool>(type: "boolean", nullable: false),
                    counts_sandwiched_days = table.Column<bool>(type: "boolean", nullable: false),
                    min_notice_days = table.Column<int>(type: "integer", nullable: false),
                    max_consecutive_days = table.Column<int>(type: "integer", nullable: false),
                    requires_attachment = table.Column<bool>(type: "boolean", nullable: false),
                    approval_tiers = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_leave_policy", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "leave_type",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    code = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    accrual = table.Column<string>(type: "text", nullable: false),
                    paid = table.Column<bool>(type: "boolean", nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_leave_type", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "loan",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    loan_no = table.Column<string>(type: "text", nullable: false),
                    employee_id = table.Column<long>(type: "bigint", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    principal_taka = table.Column<long>(type: "bigint", nullable: false),
                    installment_taka = table.Column<long>(type: "bigint", nullable: false),
                    installment_count = table.Column<int>(type: "integer", nullable: false),
                    start_period = table.Column<DateOnly>(type: "date", nullable: false),
                    state = table.Column<string>(type: "text", nullable: false),
                    recovered_taka = table.Column<long>(type: "bigint", nullable: false),
                    carried_taka = table.Column<long>(type: "bigint", nullable: false),
                    approval_request_id = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_loan", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "loan_installment",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    loan_id = table.Column<long>(type: "bigint", nullable: false),
                    payroll_run_id = table.Column<long>(type: "bigint", nullable: true),
                    period = table.Column<DateOnly>(type: "date", nullable: false),
                    amount_taka = table.Column<long>(type: "bigint", nullable: false),
                    deferred = table.Column<bool>(type: "boolean", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_loan_installment", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "org_unit",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    code = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    parent_id = table.Column<long>(type: "bigint", nullable: true),
                    head_employee_id = table.Column<long>(type: "bigint", nullable: true),
                    active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_org_unit", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "overtime_rule",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    grade_id = table.Column<long>(type: "bigint", nullable: true),
                    threshold_minutes = table.Column<int>(type: "integer", nullable: false),
                    multiplier_bp = table.Column<long>(type: "bigint", nullable: false),
                    max_minutes_per_month = table.Column<int>(type: "integer", nullable: false),
                    based_on_component_id = table.Column<long>(type: "bigint", nullable: true),
                    bank_instead_of_pay = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_overtime_rule", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "pay_component",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    code = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    calc_method = table.Column<string>(type: "text", nullable: false),
                    based_on_component_id = table.Column<long>(type: "bigint", nullable: true),
                    percent_bp = table.Column<long>(type: "bigint", nullable: false),
                    taxable = table.Column<bool>(type: "boolean", nullable: false),
                    pf_applicable = table.Column<bool>(type: "boolean", nullable: false),
                    computed_kind = table.Column<string>(type: "text", nullable: true),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pay_component", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "pay_scale",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    grade_id = table.Column<long>(type: "bigint", nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    basic_taka = table.Column<long>(type: "bigint", nullable: false),
                    min_taka = table.Column<long>(type: "bigint", nullable: true),
                    max_taka = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pay_scale", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "payroll_component_line",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    payroll_line_id = table.Column<long>(type: "bigint", nullable: false),
                    run_id = table.Column<long>(type: "bigint", nullable: false),
                    component_id = table.Column<long>(type: "bigint", nullable: false),
                    component_code = table.Column<string>(type: "text", nullable: false),
                    component_name = table.Column<string>(type: "text", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    amount_taka = table.Column<long>(type: "bigint", nullable: false),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    basis = table.Column<string>(type: "text", nullable: true),
                    source_ref_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payroll_component_line", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "payroll_line",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    run_id = table.Column<long>(type: "bigint", nullable: false),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    employee_id = table.Column<long>(type: "bigint", nullable: false),
                    employee_code = table.Column<string>(type: "text", nullable: false),
                    employee_name = table.Column<string>(type: "text", nullable: false),
                    org_unit_id = table.Column<long>(type: "bigint", nullable: true),
                    designation_id = table.Column<long>(type: "bigint", nullable: true),
                    grade_id = table.Column<long>(type: "bigint", nullable: true),
                    pay_structure_id = table.Column<long>(type: "bigint", nullable: true),
                    policy_stamp_json = table.Column<string>(type: "jsonb", nullable: true),
                    period_days = table.Column<int>(type: "integer", nullable: false),
                    payable_days_bp = table.Column<int>(type: "integer", nullable: false),
                    present_days_bp = table.Column<int>(type: "integer", nullable: false),
                    absent_days_bp = table.Column<int>(type: "integer", nullable: false),
                    leave_days_bp = table.Column<int>(type: "integer", nullable: false),
                    leave_without_pay_days_bp = table.Column<int>(type: "integer", nullable: false),
                    late_count = table.Column<int>(type: "integer", nullable: false),
                    overtime_minutes = table.Column<int>(type: "integer", nullable: false),
                    gross_earnings_taka = table.Column<long>(type: "bigint", nullable: false),
                    total_deductions_taka = table.Column<long>(type: "bigint", nullable: false),
                    net_pay_taka = table.Column<long>(type: "bigint", nullable: false),
                    employer_cost_taka = table.Column<long>(type: "bigint", nullable: false),
                    carried_shortfall_taka = table.Column<long>(type: "bigint", nullable: false),
                    note = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payroll_line", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "payroll_policy",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    day_count_convention = table.Column<string>(type: "text", nullable: false),
                    minimum_net_pay_taka = table.Column<long>(type: "bigint", nullable: false),
                    rounding_residue_component_id = table.Column<long>(type: "bigint", nullable: true),
                    leave_year_start_month = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payroll_policy", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "payroll_run",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    run_no = table.Column<string>(type: "text", nullable: false),
                    period = table.Column<DateOnly>(type: "date", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    state = table.Column<string>(type: "text", nullable: false),
                    reversal_of_run_id = table.Column<long>(type: "bigint", nullable: true),
                    approval_request_id = table.Column<long>(type: "bigint", nullable: true),
                    employee_count = table.Column<int>(type: "integer", nullable: false),
                    total_gross_taka = table.Column<long>(type: "bigint", nullable: false),
                    total_deduction_taka = table.Column<long>(type: "bigint", nullable: false),
                    total_net_taka = table.Column<long>(type: "bigint", nullable: false),
                    total_employer_cost_taka = table.Column<long>(type: "bigint", nullable: false),
                    exception_count = table.Column<int>(type: "integer", nullable: false),
                    generated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    generated_by = table.Column<long>(type: "bigint", nullable: false),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reviewed_by = table.Column<long>(type: "bigint", nullable: true),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    approved_by = table.Column<long>(type: "bigint", nullable: true),
                    locked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    locked_by = table.Column<long>(type: "bigint", nullable: true),
                    posted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    posted_by = table.Column<long>(type: "bigint", nullable: true),
                    journal_json = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payroll_run", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "payslip",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    payroll_line_id = table.Column<long>(type: "bigint", nullable: false),
                    employee_id = table.Column<long>(type: "bigint", nullable: false),
                    payslip_no = table.Column<string>(type: "text", nullable: false),
                    issued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    issued_by = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payslip", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "pf_policy",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    employee_share_bp = table.Column<long>(type: "bigint", nullable: false),
                    employer_share_bp = table.Column<long>(type: "bigint", nullable: false),
                    eligibility_months = table.Column<int>(type: "integer", nullable: false),
                    monthly_cap_taka = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pf_policy", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "punch",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    employee_id = table.Column<long>(type: "bigint", nullable: false),
                    device_id = table.Column<string>(type: "text", nullable: false),
                    punched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    source = table.Column<string>(type: "text", nullable: false),
                    direction = table.Column<string>(type: "text", nullable: true),
                    import_batch_id = table.Column<long>(type: "bigint", nullable: true),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_punch", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "punch_import_batch",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    file_name = table.Column<string>(type: "text", nullable: false),
                    rows_read = table.Column<int>(type: "integer", nullable: false),
                    rows_accepted = table.Column<int>(type: "integer", nullable: false),
                    rows_duplicate = table.Column<int>(type: "integer", nullable: false),
                    rows_rejected = table.Column<int>(type: "integer", nullable: false),
                    rejections_json = table.Column<string>(type: "jsonb", nullable: true),
                    imported_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    imported_by = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_punch_import_batch", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "roster",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    org_unit_id = table.Column<long>(type: "bigint", nullable: false),
                    from_date = table.Column<DateOnly>(type: "date", nullable: false),
                    to_date = table.Column<DateOnly>(type: "date", nullable: false),
                    published = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_roster", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "roster_entry",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    roster_id = table.Column<long>(type: "bigint", nullable: false),
                    employee_id = table.Column<long>(type: "bigint", nullable: false),
                    on_date = table.Column<DateOnly>(type: "date", nullable: false),
                    shift_id = table.Column<long>(type: "bigint", nullable: false),
                    weekly_off = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_roster_entry", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "shift",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    code = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    starts_at = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    ends_at = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    ends_next_day = table.Column<bool>(type: "boolean", nullable: false),
                    pair_tolerance_minutes = table.Column<int>(type: "integer", nullable: false),
                    break_minutes = table.Column<int>(type: "integer", nullable: false),
                    standard_minutes = table.Column<int>(type: "integer", nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shift", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tax_slab",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    category = table.Column<string>(type: "text", nullable: true),
                    ordinal = table.Column<int>(type: "integer", nullable: false),
                    up_to_taka = table.Column<long>(type: "bigint", nullable: false),
                    rate_bp = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tax_slab", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "weekly_off_pattern",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    day_mask = table.Column<int>(type: "integer", nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_weekly_off_pattern", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "work_location",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    code = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    address = table.Column<string>(type: "text", nullable: true),
                    active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_work_location", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_attendance_correction_arrears_for_run_id_arrears_settled",
                schema: "hr",
                table: "attendance_correction",
                columns: new[] { "arrears_for_run_id", "arrears_settled" });

            migrationBuilder.CreateIndex(
                name: "ix_attendance_correction_attendance_day_id",
                schema: "hr",
                table: "attendance_correction",
                column: "attendance_day_id");

            migrationBuilder.CreateIndex(
                name: "ix_attendance_day_branch_id_on_date_status",
                schema: "hr",
                table: "attendance_day",
                columns: new[] { "branch_id", "on_date", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_attendance_day_employee_id_on_date",
                schema: "hr",
                table: "attendance_day",
                columns: new[] { "employee_id", "on_date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_deduction_rule_branch_id_effective_from",
                schema: "hr",
                table: "deduction_rule",
                columns: new[] { "branch_id", "effective_from" });

            migrationBuilder.CreateIndex(
                name: "ix_designation_branch_id_code",
                schema: "hr",
                table: "designation",
                columns: new[] { "branch_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_employee_employee_code",
                schema: "hr",
                table: "employee",
                column: "employee_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_employee_person_ref",
                schema: "hr",
                table: "employee",
                column: "person_ref");

            migrationBuilder.CreateIndex(
                name: "ix_employee_status",
                schema: "hr",
                table: "employee",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_employee_user_ref",
                schema: "hr",
                table: "employee",
                column: "user_ref",
                unique: true,
                filter: "user_ref IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_employee_assignment_employee_id_effective_from",
                schema: "hr",
                table: "employee_assignment",
                columns: new[] { "employee_id", "effective_from" });

            migrationBuilder.CreateIndex(
                name: "ix_employee_assignment_org_unit_id",
                schema: "hr",
                table: "employee_assignment",
                column: "org_unit_id");

            migrationBuilder.CreateIndex(
                name: "ix_employee_ledger_entry_employee_id_kind_on_date",
                schema: "hr",
                table: "employee_ledger_entry",
                columns: new[] { "employee_id", "kind", "on_date" });

            migrationBuilder.CreateIndex(
                name: "ix_employee_pay_component_pay_structure_id_component_id",
                schema: "hr",
                table: "employee_pay_component",
                columns: new[] { "pay_structure_id", "component_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_employee_pay_structure_employee_id_effective_from",
                schema: "hr",
                table: "employee_pay_structure",
                columns: new[] { "employee_id", "effective_from" });

            migrationBuilder.CreateIndex(
                name: "ix_employment_event_employee_id_on_date",
                schema: "hr",
                table: "employment_event",
                columns: new[] { "employee_id", "on_date" });

            migrationBuilder.CreateIndex(
                name: "ix_grace_time_rule_branch_id_effective_from",
                schema: "hr",
                table: "grace_time_rule",
                columns: new[] { "branch_id", "effective_from" });

            migrationBuilder.CreateIndex(
                name: "ix_grade_branch_id_code",
                schema: "hr",
                table: "grade",
                columns: new[] { "branch_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_gratuity_rule_branch_id_effective_from",
                schema: "hr",
                table: "gratuity_rule",
                columns: new[] { "branch_id", "effective_from" });

            migrationBuilder.CreateIndex(
                name: "ix_holiday_calendar_id_on_date",
                schema: "hr",
                table: "holiday",
                columns: new[] { "calendar_id", "on_date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_holiday_pay_policy_branch_id_effective_from",
                schema: "hr",
                table: "holiday_pay_policy",
                columns: new[] { "branch_id", "effective_from" });

            migrationBuilder.CreateIndex(
                name: "ix_leave_application_application_no",
                schema: "hr",
                table: "leave_application",
                column: "application_no",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_leave_application_employee_id_from_date",
                schema: "hr",
                table: "leave_application",
                columns: new[] { "employee_id", "from_date" });

            migrationBuilder.CreateIndex(
                name: "ix_leave_application_state",
                schema: "hr",
                table: "leave_application",
                column: "state");

            migrationBuilder.CreateIndex(
                name: "ix_leave_balance_employee_id_leave_type_id_leave_year",
                schema: "hr",
                table: "leave_balance",
                columns: new[] { "employee_id", "leave_type_id", "leave_year" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_leave_encashment_employee_id_leave_year",
                schema: "hr",
                table: "leave_encashment",
                columns: new[] { "employee_id", "leave_year" });

            migrationBuilder.CreateIndex(
                name: "ix_leave_policy_leave_type_id_effective_from",
                schema: "hr",
                table: "leave_policy",
                columns: new[] { "leave_type_id", "effective_from" });

            migrationBuilder.CreateIndex(
                name: "ix_leave_type_branch_id_code",
                schema: "hr",
                table: "leave_type",
                columns: new[] { "branch_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_loan_employee_id_state",
                schema: "hr",
                table: "loan",
                columns: new[] { "employee_id", "state" });

            migrationBuilder.CreateIndex(
                name: "ix_loan_loan_no",
                schema: "hr",
                table: "loan",
                column: "loan_no",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_loan_installment_loan_id_period",
                schema: "hr",
                table: "loan_installment",
                columns: new[] { "loan_id", "period" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_org_unit_branch_id_code",
                schema: "hr",
                table: "org_unit",
                columns: new[] { "branch_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_org_unit_parent_id",
                schema: "hr",
                table: "org_unit",
                column: "parent_id");

            migrationBuilder.CreateIndex(
                name: "ix_overtime_rule_branch_id_effective_from",
                schema: "hr",
                table: "overtime_rule",
                columns: new[] { "branch_id", "effective_from" });

            migrationBuilder.CreateIndex(
                name: "ix_pay_component_branch_id_code",
                schema: "hr",
                table: "pay_component",
                columns: new[] { "branch_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pay_scale_grade_id_effective_from",
                schema: "hr",
                table: "pay_scale",
                columns: new[] { "grade_id", "effective_from" });

            migrationBuilder.CreateIndex(
                name: "ix_payroll_component_line_payroll_line_id",
                schema: "hr",
                table: "payroll_component_line",
                column: "payroll_line_id");

            migrationBuilder.CreateIndex(
                name: "ix_payroll_component_line_run_id",
                schema: "hr",
                table: "payroll_component_line",
                column: "run_id");

            migrationBuilder.CreateIndex(
                name: "ix_payroll_line_employee_id",
                schema: "hr",
                table: "payroll_line",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_payroll_line_run_id_employee_id",
                schema: "hr",
                table: "payroll_line",
                columns: new[] { "run_id", "employee_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payroll_policy_branch_id_effective_from",
                schema: "hr",
                table: "payroll_policy",
                columns: new[] { "branch_id", "effective_from" });

            migrationBuilder.CreateIndex(
                name: "ix_payroll_run_branch_id_period_kind_sequence",
                schema: "hr",
                table: "payroll_run",
                columns: new[] { "branch_id", "period", "kind", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payroll_run_run_no",
                schema: "hr",
                table: "payroll_run",
                column: "run_no",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payroll_run_state",
                schema: "hr",
                table: "payroll_run",
                column: "state");

            migrationBuilder.CreateIndex(
                name: "ix_payslip_employee_id",
                schema: "hr",
                table: "payslip",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_payslip_payroll_line_id",
                schema: "hr",
                table: "payslip",
                column: "payroll_line_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payslip_payslip_no",
                schema: "hr",
                table: "payslip",
                column: "payslip_no",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pf_policy_branch_id_effective_from",
                schema: "hr",
                table: "pf_policy",
                columns: new[] { "branch_id", "effective_from" });

            migrationBuilder.CreateIndex(
                name: "ix_punch_employee_id_device_id_punched_at",
                schema: "hr",
                table: "punch",
                columns: new[] { "employee_id", "device_id", "punched_at" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_punch_import_batch_id",
                schema: "hr",
                table: "punch",
                column: "import_batch_id");

            migrationBuilder.CreateIndex(
                name: "ix_roster_org_unit_id_from_date",
                schema: "hr",
                table: "roster",
                columns: new[] { "org_unit_id", "from_date" });

            migrationBuilder.CreateIndex(
                name: "ix_roster_entry_employee_id_on_date",
                schema: "hr",
                table: "roster_entry",
                columns: new[] { "employee_id", "on_date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_roster_entry_roster_id",
                schema: "hr",
                table: "roster_entry",
                column: "roster_id");

            migrationBuilder.CreateIndex(
                name: "ix_shift_branch_id_code",
                schema: "hr",
                table: "shift",
                columns: new[] { "branch_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tax_slab_branch_id_effective_from_ordinal",
                schema: "hr",
                table: "tax_slab",
                columns: new[] { "branch_id", "effective_from", "ordinal" });

            migrationBuilder.CreateIndex(
                name: "ix_weekly_off_pattern_branch_id_effective_from",
                schema: "hr",
                table: "weekly_off_pattern",
                columns: new[] { "branch_id", "effective_from" });

            migrationBuilder.CreateIndex(
                name: "ix_work_location_branch_id_code",
                schema: "hr",
                table: "work_location",
                columns: new[] { "branch_id", "code" },
                unique: true);

            // ---- Effective-dated integrity (ADR-0027, hard rule 5).
            //
            // EF cannot express an exclusion constraint, and a CHECK cannot see sibling rows — so
            // without these, two overlapping pay structures could coexist and a payroll run would
            // silently resolve whichever the ORDER BY happened to reach first. Postgres refuses the
            // second row instead. btree_gist is already installed by deploy/db-init/01-roles.sh.
            //
            // daterange(from, to, '[]') is inclusive at both ends, matching how the resolver reads
            // EffectiveFrom <= d <= EffectiveTo; a NULL EffectiveTo means "still open", which
            // 'infinity' models exactly.
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS btree_gist;");

            foreach (var (table, scope) in new[]
                     {
                         ("employee_pay_structure", "employee_id"),
                         ("employee_assignment", "employee_id"),
                         ("payroll_policy", "branch_id"),
                         ("pf_policy", "branch_id"),
                         ("gratuity_rule", "branch_id"),
                         ("grace_time_rule", "branch_id"),
                         ("holiday_pay_policy", "branch_id"),
                         ("deduction_rule", "branch_id"),
                     })
            {
                migrationBuilder.Sql($"""
                    ALTER TABLE hr.{table}
                    ADD CONSTRAINT ck_{table}_effective_order
                        CHECK (effective_to IS NULL OR effective_to >= effective_from);

                    ALTER TABLE hr.{table}
                    ADD CONSTRAINT ex_{table}_no_overlap
                        EXCLUDE USING gist (
                            {scope} WITH =,
                            daterange(effective_from, COALESCE(effective_to, 'infinity'::date), '[]') WITH &&
                        );
                    """);
            }

            // A payroll line's money must add up. The same class of invariant billing enforces on an
            // invoice: arithmetic that cannot be wrong is better than arithmetic that is checked.
            migrationBuilder.Sql("""
                ALTER TABLE hr.payroll_line
                ADD CONSTRAINT ck_payroll_line_net
                    CHECK (net_pay_taka = gross_earnings_taka - total_deductions_taka
                                          + carried_shortfall_taka);

                ALTER TABLE hr.payroll_line
                ADD CONSTRAINT ck_payroll_line_non_negative
                    CHECK (gross_earnings_taka >= 0 OR net_pay_taka <= 0);
                """);

            // Attendance fractions are basis points of a day: 0..10000. A payable fraction outside
            // that range would quietly over- or under-pay someone.
            migrationBuilder.Sql("""
                ALTER TABLE hr.attendance_day
                ADD CONSTRAINT ck_attendance_day_fraction
                    CHECK (payable_fraction_bp BETWEEN 0 AND 10000);
                """);

            // A leave balance may not be spent below zero. Concurrency is guarded by the row version
            // too, but the constraint is what makes it impossible rather than unlikely.
            migrationBuilder.Sql("""
                ALTER TABLE hr.leave_balance
                ADD CONSTRAINT ck_leave_balance_not_overdrawn
                    CHECK (opening_bp + accrued_bp + adjustment_bp - availed_bp - encashed_bp >= 0);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "attendance_correction",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "attendance_day",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "deduction_rule",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "designation",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "employee",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "employee_assignment",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "employee_ledger_entry",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "employee_pay_component",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "employee_pay_structure",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "employment_event",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "grace_time_rule",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "grade",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "gratuity_rule",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "holiday",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "holiday_calendar",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "holiday_pay_policy",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "leave_application",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "leave_balance",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "leave_encashment",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "leave_policy",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "leave_type",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "loan",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "loan_installment",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "org_unit",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "overtime_rule",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "pay_component",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "pay_scale",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "payroll_component_line",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "payroll_line",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "payroll_policy",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "payroll_run",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "payslip",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "pf_policy",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "punch",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "punch_import_batch",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "roster",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "roster_entry",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "shift",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "tax_slab",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "weekly_off_pattern",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "work_location",
                schema: "hr");
        }
    }
}

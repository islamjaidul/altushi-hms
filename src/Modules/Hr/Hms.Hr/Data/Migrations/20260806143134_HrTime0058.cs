using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Hms.Hr.Data.Migrations
{
    /// <inheritdoc />
    public partial class HrTime0058 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "week_starts_on",
                schema: "hr",
                table: "payroll_policy",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "comp_off_expiry_days",
                schema: "hr",
                table: "overtime_rule",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "require_approval",
                schema: "hr",
                table: "overtime_rule",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "attendance_device",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    code = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    location = table.Column<string>(type: "text", nullable: true),
                    serial_no = table.Column<string>(type: "text", nullable: true),
                    work_location_id = table.Column<long>(type: "bigint", nullable: true),
                    expected_interval_minutes = table.Column<int>(type: "integer", nullable: false),
                    push_key_hash = table.Column<string>(type: "text", nullable: true),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_batch_punches = table.Column<int>(type: "integer", nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_attendance_device", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "comp_off_request",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    employee_id = table.Column<long>(type: "bigint", nullable: false),
                    on_date = table.Column<DateOnly>(type: "date", nullable: false),
                    minutes = table.Column<int>(type: "integer", nullable: false),
                    reason = table.Column<string>(type: "text", nullable: true),
                    state = table.Column<string>(type: "text", nullable: false),
                    decided_by = table.Column<long>(type: "bigint", nullable: true),
                    decided_by_name = table.Column<string>(type: "text", nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    decision_note = table.Column<string>(type: "text", nullable: true),
                    raised_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    raised_by = table.Column<long>(type: "bigint", nullable: false),
                    raised_by_name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_comp_off_request", x => x.id);
                    table.ForeignKey(
                        name: "fk_comp_off_request_employee_employee_id",
                        column: x => x.employee_id,
                        principalSchema: "hr",
                        principalTable: "employee",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "overtime_bank_entry",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    employee_id = table.Column<long>(type: "bigint", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    on_date = table.Column<DateOnly>(type: "date", nullable: false),
                    minutes = table.Column<int>(type: "integer", nullable: false),
                    narration = table.Column<string>(type: "text", nullable: false),
                    expires_on = table.Column<DateOnly>(type: "date", nullable: true),
                    overtime_request_id = table.Column<long>(type: "bigint", nullable: true),
                    comp_off_request_id = table.Column<long>(type: "bigint", nullable: true),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_by = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_overtime_bank_entry", x => x.id);
                    table.ForeignKey(
                        name: "fk_overtime_bank_entry_employee_employee_id",
                        column: x => x.employee_id,
                        principalSchema: "hr",
                        principalTable: "employee",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "overtime_request",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    employee_id = table.Column<long>(type: "bigint", nullable: false),
                    on_date = table.Column<DateOnly>(type: "date", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    minutes = table.Column<int>(type: "integer", nullable: false),
                    reason = table.Column<string>(type: "text", nullable: true),
                    state = table.Column<string>(type: "text", nullable: false),
                    banked = table.Column<bool>(type: "boolean", nullable: false),
                    decided_by = table.Column<long>(type: "bigint", nullable: true),
                    decided_by_name = table.Column<string>(type: "text", nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    decision_note = table.Column<string>(type: "text", nullable: true),
                    raised_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    raised_by = table.Column<long>(type: "bigint", nullable: false),
                    raised_by_name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_overtime_request", x => x.id);
                    table.ForeignKey(
                        name: "fk_overtime_request_employee_employee_id",
                        column: x => x.employee_id,
                        principalSchema: "hr",
                        principalTable: "employee",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "regularization_request",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    employee_id = table.Column<long>(type: "bigint", nullable: false),
                    on_date = table.Column<DateOnly>(type: "date", nullable: false),
                    requested_status = table.Column<string>(type: "text", nullable: false),
                    requested_in = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    requested_out = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reason = table.Column<string>(type: "text", nullable: false),
                    state = table.Column<string>(type: "text", nullable: false),
                    decision_note = table.Column<string>(type: "text", nullable: true),
                    decided_by = table.Column<long>(type: "bigint", nullable: true),
                    decided_by_name = table.Column<string>(type: "text", nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    correction_id = table.Column<long>(type: "bigint", nullable: true),
                    raised_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    raised_by = table.Column<long>(type: "bigint", nullable: false),
                    raised_by_name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_regularization_request", x => x.id);
                    table.ForeignKey(
                        name: "fk_regularization_request_employee_employee_id",
                        column: x => x.employee_id,
                        principalSchema: "hr",
                        principalTable: "employee",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "roster_pattern",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    code = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_roster_pattern", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "shift_requirement",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    org_unit_id = table.Column<long>(type: "bigint", nullable: false),
                    shift_id = table.Column<long>(type: "bigint", nullable: false),
                    weekday = table.Column<int>(type: "integer", nullable: true),
                    required_headcount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shift_requirement", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "shift_swap_request",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    requester_employee_id = table.Column<long>(type: "bigint", nullable: false),
                    counterpart_employee_id = table.Column<long>(type: "bigint", nullable: false),
                    requester_date = table.Column<DateOnly>(type: "date", nullable: false),
                    counterpart_date = table.Column<DateOnly>(type: "date", nullable: false),
                    reason = table.Column<string>(type: "text", nullable: true),
                    state = table.Column<string>(type: "text", nullable: false),
                    decided_by = table.Column<long>(type: "bigint", nullable: true),
                    decided_by_name = table.Column<string>(type: "text", nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    decision_note = table.Column<string>(type: "text", nullable: true),
                    raised_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    raised_by = table.Column<long>(type: "bigint", nullable: false),
                    raised_by_name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shift_swap_request", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "short_leave_request",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    employee_id = table.Column<long>(type: "bigint", nullable: false),
                    on_date = table.Column<DateOnly>(type: "date", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    from_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    to_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    reason = table.Column<string>(type: "text", nullable: false),
                    state = table.Column<string>(type: "text", nullable: false),
                    decided_by = table.Column<long>(type: "bigint", nullable: true),
                    decided_by_name = table.Column<string>(type: "text", nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    decision_note = table.Column<string>(type: "text", nullable: true),
                    raised_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    raised_by = table.Column<long>(type: "bigint", nullable: false),
                    raised_by_name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_short_leave_request", x => x.id);
                    table.ForeignKey(
                        name: "fk_short_leave_request_employee_employee_id",
                        column: x => x.employee_id,
                        principalSchema: "hr",
                        principalTable: "employee",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "roster_pattern_step",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    pattern_id = table.Column<long>(type: "bigint", nullable: false),
                    ordinal = table.Column<int>(type: "integer", nullable: false),
                    shift_id = table.Column<long>(type: "bigint", nullable: true),
                    days = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_roster_pattern_step", x => x.id);
                    table.ForeignKey(
                        name: "fk_roster_pattern_step_roster_pattern_pattern_id",
                        column: x => x.pattern_id,
                        principalSchema: "hr",
                        principalTable: "roster_pattern",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_attendance_device_branch_id_active_last_seen_at",
                schema: "hr",
                table: "attendance_device",
                columns: new[] { "branch_id", "active", "last_seen_at" });

            migrationBuilder.CreateIndex(
                name: "ix_attendance_device_branch_id_code",
                schema: "hr",
                table: "attendance_device",
                columns: new[] { "branch_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_comp_off_request_employee_id_on_date",
                schema: "hr",
                table: "comp_off_request",
                columns: new[] { "employee_id", "on_date" },
                unique: true,
                filter: "state IN ('raised', 'recommended', 'approved', 'applied')");

            migrationBuilder.CreateIndex(
                name: "ix_overtime_bank_entry_branch_id_kind_expires_on",
                schema: "hr",
                table: "overtime_bank_entry",
                columns: new[] { "branch_id", "kind", "expires_on" });

            migrationBuilder.CreateIndex(
                name: "ix_overtime_bank_entry_employee_id_on_date",
                schema: "hr",
                table: "overtime_bank_entry",
                columns: new[] { "employee_id", "on_date" });

            migrationBuilder.CreateIndex(
                name: "ix_overtime_request_branch_id_state_on_date",
                schema: "hr",
                table: "overtime_request",
                columns: new[] { "branch_id", "state", "on_date" });

            migrationBuilder.CreateIndex(
                name: "ix_overtime_request_employee_id_on_date",
                schema: "hr",
                table: "overtime_request",
                columns: new[] { "employee_id", "on_date" },
                unique: true,
                filter: "state IN ('raised', 'recommended', 'approved')");

            migrationBuilder.CreateIndex(
                name: "ix_regularization_request_branch_id_state",
                schema: "hr",
                table: "regularization_request",
                columns: new[] { "branch_id", "state" });

            migrationBuilder.CreateIndex(
                name: "ix_regularization_request_employee_id_on_date",
                schema: "hr",
                table: "regularization_request",
                columns: new[] { "employee_id", "on_date" },
                unique: true,
                filter: "state IN ('raised', 'recommended', 'approved')");

            migrationBuilder.CreateIndex(
                name: "ix_roster_pattern_branch_id_code",
                schema: "hr",
                table: "roster_pattern",
                columns: new[] { "branch_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_roster_pattern_step_pattern_id_ordinal",
                schema: "hr",
                table: "roster_pattern_step",
                columns: new[] { "pattern_id", "ordinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_shift_requirement_org_unit_id_shift_id_weekday",
                schema: "hr",
                table: "shift_requirement",
                columns: new[] { "org_unit_id", "shift_id", "weekday" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_shift_swap_request_branch_id_state",
                schema: "hr",
                table: "shift_swap_request",
                columns: new[] { "branch_id", "state" });

            migrationBuilder.CreateIndex(
                name: "ix_shift_swap_request_requester_employee_id_requester_date",
                schema: "hr",
                table: "shift_swap_request",
                columns: new[] { "requester_employee_id", "requester_date" });

            migrationBuilder.CreateIndex(
                name: "ix_short_leave_request_branch_id_state",
                schema: "hr",
                table: "short_leave_request",
                columns: new[] { "branch_id", "state" });

            migrationBuilder.CreateIndex(
                name: "ix_short_leave_request_employee_id_on_date",
                schema: "hr",
                table: "short_leave_request",
                columns: new[] { "employee_id", "on_date" });

            // ---- Invariants EF cannot express (spec 0058).

            // Banked minutes are always positive; the kind carries the direction. A negative credit
            // would silently reverse a balance nobody asked to reverse.
            migrationBuilder.Sql("""
                ALTER TABLE hr.overtime_bank_entry
                ADD CONSTRAINT ck_overtime_bank_positive
                    CHECK (minutes > 0);

                ALTER TABLE hr.overtime_request
                ADD CONSTRAINT ck_overtime_request_minutes
                    CHECK (minutes > 0 AND minutes <= 1440);
                """);

            // An hourly absence that ends before it begins is not an absence.
            migrationBuilder.Sql("""
                ALTER TABLE hr.short_leave_request
                ADD CONSTRAINT ck_short_leave_span
                    CHECK (to_time > from_time);
                """);

            // A pattern step covers at least one day; zero would make the cycle infinite.
            migrationBuilder.Sql("""
                ALTER TABLE hr.roster_pattern_step
                ADD CONSTRAINT ck_roster_pattern_step_days
                    CHECK (days BETWEEN 1 AND 366);

                ALTER TABLE hr.shift_requirement
                ADD CONSTRAINT ck_shift_requirement_headcount
                    CHECK (required_headcount >= 0);
                """);

            // Nobody swaps a shift with themselves.
            migrationBuilder.Sql("""
                ALTER TABLE hr.shift_swap_request
                ADD CONSTRAINT ck_shift_swap_two_people
                    CHECK (requester_employee_id <> counterpart_employee_id);
                """);

            // A device that is expected to report every zero minutes is always silent.
            migrationBuilder.Sql("""
                ALTER TABLE hr.attendance_device
                ADD CONSTRAINT ck_attendance_device_interval
                    CHECK (expected_interval_minutes BETWEEN 1 AND 10080);

                ALTER TABLE hr.overtime_rule
                ADD CONSTRAINT ck_overtime_rule_comp_off_expiry
                    CHECK (comp_off_expiry_days BETWEEN 0 AND 3650);

                ALTER TABLE hr.payroll_policy
                ADD CONSTRAINT ck_payroll_policy_week_start
                    CHECK (week_starts_on BETWEEN 0 AND 6);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "attendance_device",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "comp_off_request",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "overtime_bank_entry",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "overtime_request",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "regularization_request",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "roster_pattern_step",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "shift_requirement",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "shift_swap_request",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "short_leave_request",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "roster_pattern",
                schema: "hr");

            migrationBuilder.DropColumn(
                name: "week_starts_on",
                schema: "hr",
                table: "payroll_policy");

            migrationBuilder.DropColumn(
                name: "comp_off_expiry_days",
                schema: "hr",
                table: "overtime_rule");

            migrationBuilder.DropColumn(
                name: "require_approval",
                schema: "hr",
                table: "overtime_rule");
        }
    }
}

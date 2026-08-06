using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Hms.Hr.Data.Migrations
{
    /// <inheritdoc />
    public partial class HrSelfService0059 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "appraisal",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    employee_id = table.Column<long>(type: "bigint", nullable: false),
                    cycle = table.Column<string>(type: "text", nullable: false),
                    period_from = table.Column<DateOnly>(type: "date", nullable: false),
                    period_to = table.Column<DateOnly>(type: "date", nullable: false),
                    state = table.Column<string>(type: "text", nullable: false),
                    self_rating = table.Column<int>(type: "integer", nullable: true),
                    self_comment = table.Column<string>(type: "text", nullable: true),
                    self_rated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    manager_rating = table.Column<int>(type: "integer", nullable: true),
                    manager_comment = table.Column<string>(type: "text", nullable: true),
                    manager_id = table.Column<long>(type: "bigint", nullable: true),
                    manager_name = table.Column<string>(type: "text", nullable: true),
                    manager_rated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    outcome = table.Column<string>(type: "text", nullable: true),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_appraisal", x => x.id);
                    table.ForeignKey(
                        name: "fk_appraisal_employee_employee_id",
                        column: x => x.employee_id,
                        principalSchema: "hr",
                        principalTable: "employee",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "approver_delegation",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    from_employee_id = table.Column<long>(type: "bigint", nullable: false),
                    to_employee_id = table.Column<long>(type: "bigint", nullable: false),
                    from_date = table.Column<DateOnly>(type: "date", nullable: false),
                    to_date = table.Column<DateOnly>(type: "date", nullable: false),
                    reason = table.Column<string>(type: "text", nullable: true),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false),
                    created_by_name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_approver_delegation", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "asset",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    code = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    category = table.Column<string>(type: "text", nullable: true),
                    serial_no = table.Column<string>(type: "text", nullable: true),
                    value_taka = table.Column<long>(type: "bigint", nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_asset", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "disciplinary_action",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    employee_id = table.Column<long>(type: "bigint", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    on_date = table.Column<DateOnly>(type: "date", nullable: false),
                    subject = table.Column<string>(type: "text", nullable: false),
                    detail = table.Column<string>(type: "text", nullable: true),
                    employee_response = table.Column<string>(type: "text", nullable: true),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: true),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    supersedes_id = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false),
                    created_by_name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_disciplinary_action", x => x.id);
                    table.ForeignKey(
                        name: "fk_disciplinary_action_employee_employee_id",
                        column: x => x.employee_id,
                        principalSchema: "hr",
                        principalTable: "employee",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "expense_claim",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    employee_id = table.Column<long>(type: "bigint", nullable: false),
                    claim_no = table.Column<string>(type: "text", nullable: false),
                    incurred_on = table.Column<DateOnly>(type: "date", nullable: false),
                    category = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    amount_taka = table.Column<long>(type: "bigint", nullable: false),
                    attachment_path = table.Column<string>(type: "text", nullable: true),
                    state = table.Column<string>(type: "text", nullable: false),
                    approved_amount_taka = table.Column<long>(type: "bigint", nullable: true),
                    decided_by = table.Column<long>(type: "bigint", nullable: true),
                    decided_by_name = table.Column<string>(type: "text", nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    decision_note = table.Column<string>(type: "text", nullable: true),
                    reimbursed_via = table.Column<string>(type: "text", nullable: true),
                    reimbursed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    raised_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    raised_by = table.Column<long>(type: "bigint", nullable: false),
                    raised_by_name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_expense_claim", x => x.id);
                    table.ForeignKey(
                        name: "fk_expense_claim_employee_employee_id",
                        column: x => x.employee_id,
                        principalSchema: "hr",
                        principalTable: "employee",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "leave_year_close",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    closing_year = table.Column<DateOnly>(type: "date", nullable: false),
                    opening_year = table.Column<DateOnly>(type: "date", nullable: false),
                    state = table.Column<string>(type: "text", nullable: false),
                    employee_count = table.Column<int>(type: "integer", nullable: false),
                    accrued_bp = table.Column<int>(type: "integer", nullable: false),
                    carried_bp = table.Column<int>(type: "integer", nullable: false),
                    lapsed_bp = table.Column<int>(type: "integer", nullable: false),
                    encashed_bp = table.Column<int>(type: "integer", nullable: false),
                    previewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    committed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    committed_by = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false),
                    created_by_name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_leave_year_close", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "notice",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    title = table.Column<string>(type: "text", nullable: false),
                    body = table.Column<string>(type: "text", nullable: false),
                    publish_from = table.Column<DateOnly>(type: "date", nullable: false),
                    publish_to = table.Column<DateOnly>(type: "date", nullable: true),
                    requires_acknowledgement = table.Column<bool>(type: "boolean", nullable: false),
                    org_unit_id = table.Column<long>(type: "bigint", nullable: true),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false),
                    created_by_name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notice", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "profile_change_request",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    employee_id = table.Column<long>(type: "bigint", nullable: false),
                    phone = table.Column<string>(type: "text", nullable: true),
                    email = table.Column<string>(type: "text", nullable: true),
                    present_address = table.Column<string>(type: "text", nullable: true),
                    emergency_contact = table.Column<string>(type: "text", nullable: true),
                    bank_name = table.Column<string>(type: "text", nullable: true),
                    bank_branch = table.Column<string>(type: "text", nullable: true),
                    bank_account_no = table.Column<string>(type: "text", nullable: true),
                    bank_routing_no = table.Column<string>(type: "text", nullable: true),
                    state = table.Column<string>(type: "text", nullable: false),
                    decision_note = table.Column<string>(type: "text", nullable: true),
                    decided_by = table.Column<long>(type: "bigint", nullable: true),
                    decided_by_name = table.Column<string>(type: "text", nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    raised_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    raised_by = table.Column<long>(type: "bigint", nullable: false),
                    raised_by_name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_profile_change_request", x => x.id);
                    table.ForeignKey(
                        name: "fk_profile_change_request_employee_employee_id",
                        column: x => x.employee_id,
                        principalSchema: "hr",
                        principalTable: "employee",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "saved_report_view",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    report_key = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    query_string = table.Column<string>(type: "text", nullable: false),
                    shared = table.Column<bool>(type: "boolean", nullable: false),
                    owner_user_id = table.Column<long>(type: "bigint", nullable: false),
                    owner_name = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_saved_report_view", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "training_record",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    employee_id = table.Column<long>(type: "bigint", nullable: false),
                    title = table.Column<string>(type: "text", nullable: false),
                    provider = table.Column<string>(type: "text", nullable: true),
                    completed_on = table.Column<DateOnly>(type: "date", nullable: false),
                    expires_on = table.Column<DateOnly>(type: "date", nullable: true),
                    certificate_no = table.Column<string>(type: "text", nullable: true),
                    note = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false),
                    created_by_name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_training_record", x => x.id);
                    table.ForeignKey(
                        name: "fk_training_record_employee_employee_id",
                        column: x => x.employee_id,
                        principalSchema: "hr",
                        principalTable: "employee",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "asset_issue",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    asset_id = table.Column<long>(type: "bigint", nullable: false),
                    employee_id = table.Column<long>(type: "bigint", nullable: false),
                    issued_on = table.Column<DateOnly>(type: "date", nullable: false),
                    issued_by = table.Column<long>(type: "bigint", nullable: false),
                    issued_by_name = table.Column<string>(type: "text", nullable: false),
                    returned_on = table.Column<DateOnly>(type: "date", nullable: true),
                    returned_to = table.Column<long>(type: "bigint", nullable: true),
                    condition = table.Column<string>(type: "text", nullable: true),
                    recoverable_taka = table.Column<long>(type: "bigint", nullable: false),
                    note = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_asset_issue", x => x.id);
                    table.ForeignKey(
                        name: "fk_asset_issue_asset_asset_id",
                        column: x => x.asset_id,
                        principalSchema: "hr",
                        principalTable: "asset",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "leave_close_line",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    close_id = table.Column<long>(type: "bigint", nullable: false),
                    employee_id = table.Column<long>(type: "bigint", nullable: false),
                    leave_type_id = table.Column<long>(type: "bigint", nullable: false),
                    closing_balance_bp = table.Column<int>(type: "integer", nullable: false),
                    accrue_bp = table.Column<int>(type: "integer", nullable: false),
                    carry_bp = table.Column<int>(type: "integer", nullable: false),
                    lapse_bp = table.Column<int>(type: "integer", nullable: false),
                    encash_bp = table.Column<int>(type: "integer", nullable: false),
                    basis = table.Column<string>(type: "text", nullable: true),
                    applied = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_leave_close_line", x => x.id);
                    table.ForeignKey(
                        name: "fk_leave_close_line_leave_year_close_close_id",
                        column: x => x.close_id,
                        principalSchema: "hr",
                        principalTable: "leave_year_close",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "notice_ack",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    notice_id = table.Column<long>(type: "bigint", nullable: false),
                    employee_id = table.Column<long>(type: "bigint", nullable: false),
                    acknowledged_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notice_ack", x => x.id);
                    table.ForeignKey(
                        name: "fk_notice_ack_notice_notice_id",
                        column: x => x.notice_id,
                        principalSchema: "hr",
                        principalTable: "notice",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_appraisal_employee_id_cycle",
                schema: "hr",
                table: "appraisal",
                columns: new[] { "employee_id", "cycle" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_approver_delegation_from_employee_id_from_date_to_date",
                schema: "hr",
                table: "approver_delegation",
                columns: new[] { "from_employee_id", "from_date", "to_date" });

            migrationBuilder.CreateIndex(
                name: "ix_asset_branch_id_code",
                schema: "hr",
                table: "asset",
                columns: new[] { "branch_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_asset_issue_asset_id",
                schema: "hr",
                table: "asset_issue",
                column: "asset_id",
                unique: true,
                filter: "returned_on IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_asset_issue_employee_id_returned_on",
                schema: "hr",
                table: "asset_issue",
                columns: new[] { "employee_id", "returned_on" });

            migrationBuilder.CreateIndex(
                name: "ix_disciplinary_action_employee_id_on_date",
                schema: "hr",
                table: "disciplinary_action",
                columns: new[] { "employee_id", "on_date" });

            migrationBuilder.CreateIndex(
                name: "ix_expense_claim_branch_id_claim_no",
                schema: "hr",
                table: "expense_claim",
                columns: new[] { "branch_id", "claim_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_expense_claim_employee_id_state",
                schema: "hr",
                table: "expense_claim",
                columns: new[] { "employee_id", "state" });

            migrationBuilder.CreateIndex(
                name: "ix_leave_close_line_close_id_employee_id_leave_type_id",
                schema: "hr",
                table: "leave_close_line",
                columns: new[] { "close_id", "employee_id", "leave_type_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_leave_year_close_branch_id_closing_year",
                schema: "hr",
                table: "leave_year_close",
                columns: new[] { "branch_id", "closing_year" },
                unique: true,
                filter: "state <> 'cancelled'");

            migrationBuilder.CreateIndex(
                name: "ix_notice_branch_id_active_publish_from",
                schema: "hr",
                table: "notice",
                columns: new[] { "branch_id", "active", "publish_from" });

            migrationBuilder.CreateIndex(
                name: "ix_notice_ack_notice_id_employee_id",
                schema: "hr",
                table: "notice_ack",
                columns: new[] { "notice_id", "employee_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_profile_change_request_employee_id",
                schema: "hr",
                table: "profile_change_request",
                column: "employee_id",
                unique: true,
                filter: "state IN ('raised', 'recommended')");

            migrationBuilder.CreateIndex(
                name: "ix_saved_report_view_branch_id_report_key_owner_user_id_name",
                schema: "hr",
                table: "saved_report_view",
                columns: new[] { "branch_id", "report_key", "owner_user_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_training_record_branch_id_expires_on",
                schema: "hr",
                table: "training_record",
                columns: new[] { "branch_id", "expires_on" },
                filter: "expires_on IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_training_record_employee_id",
                schema: "hr",
                table: "training_record",
                column: "employee_id");

            // ---- Invariants EF cannot express (spec 0059).

            // A leave-close movement is basis points of a day; a negative accrual or a negative
            // lapse would silently reverse the close's own arithmetic.
            migrationBuilder.Sql("""
                ALTER TABLE hr.leave_close_line
                ADD CONSTRAINT ck_leave_close_line_non_negative
                    CHECK (accrue_bp >= 0 AND carry_bp >= 0 AND lapse_bp >= 0 AND encash_bp >= 0);

                ALTER TABLE hr.leave_close_line
                ADD CONSTRAINT ck_leave_close_line_balances
                    CHECK (carry_bp + lapse_bp + encash_bp <= closing_balance_bp);
                """);

            // A claim is for money; zero is not a claim, and negative is a refund.
            migrationBuilder.Sql("""
                ALTER TABLE hr.expense_claim
                ADD CONSTRAINT ck_expense_claim_amount
                    CHECK (amount_taka > 0
                           AND (approved_amount_taka IS NULL
                                OR (approved_amount_taka >= 0
                                    AND approved_amount_taka <= amount_taka)));
                """);

            // An asset comes back on or after the day it went out, and what is recovered for it is
            // never negative.
            migrationBuilder.Sql("""
                ALTER TABLE hr.asset_issue
                ADD CONSTRAINT ck_asset_issue_dates
                    CHECK (returned_on IS NULL OR returned_on >= issued_on);

                ALTER TABLE hr.asset_issue
                ADD CONSTRAINT ck_asset_issue_recoverable
                    CHECK (recoverable_taka >= 0);
                """);

            // A delegation that ends before it starts delegates nothing, and nobody delegates to
            // themselves — that is just being on duty.
            migrationBuilder.Sql("""
                ALTER TABLE hr.approver_delegation
                ADD CONSTRAINT ck_approver_delegation_span
                    CHECK (to_date >= from_date AND from_employee_id <> to_employee_id);
                """);

            // A notice with an end date ends after it begins; a training certificate does not
            // expire before it was earned.
            migrationBuilder.Sql("""
                ALTER TABLE hr.notice
                ADD CONSTRAINT ck_notice_span
                    CHECK (publish_to IS NULL OR publish_to >= publish_from);

                ALTER TABLE hr.training_record
                ADD CONSTRAINT ck_training_record_dates
                    CHECK (expires_on IS NULL OR expires_on >= completed_on);
                """);

            // The close opens the year after the one it closes.
            migrationBuilder.Sql("""
                ALTER TABLE hr.leave_year_close
                ADD CONSTRAINT ck_leave_year_close_order
                    CHECK (opening_year > closing_year);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "appraisal",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "approver_delegation",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "asset_issue",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "disciplinary_action",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "expense_claim",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "leave_close_line",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "notice_ack",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "profile_change_request",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "saved_report_view",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "training_record",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "asset",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "leave_year_close",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "notice",
                schema: "hr");
        }
    }
}

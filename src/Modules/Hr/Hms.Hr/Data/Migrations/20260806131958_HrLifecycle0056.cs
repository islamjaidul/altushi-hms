using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Hms.Hr.Data.Migrations
{
    /// <inheritdoc />
    public partial class HrLifecycle0056 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "contract_ends_on",
                schema: "hr",
                table: "employee",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "employment_type",
                schema: "hr",
                table: "employee",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateOnly>(
                name: "probation_due_on",
                schema: "hr",
                table: "employee",
                type: "date",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "employee_dependant",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    employee_id = table.Column<long>(type: "bigint", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    relation = table.Column<string>(type: "text", nullable: false),
                    date_of_birth = table.Column<DateOnly>(type: "date", nullable: true),
                    phone = table.Column<string>(type: "text", nullable: true),
                    national_id = table.Column<string>(type: "text", nullable: true),
                    address = table.Column<string>(type: "text", nullable: true),
                    is_nominee = table.Column<bool>(type: "boolean", nullable: false),
                    purpose = table.Column<string>(type: "text", nullable: true),
                    share_bp = table.Column<int>(type: "integer", nullable: false),
                    supersedes_id = table.Column<long>(type: "bigint", nullable: true),
                    superseded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    superseded_by = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false),
                    created_by_name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employee_dependant", x => x.id);
                    table.ForeignKey(
                        name: "fk_employee_dependant_employee_employee_id",
                        column: x => x.employee_id,
                        principalSchema: "hr",
                        principalTable: "employee",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "employee_document",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    employee_id = table.Column<long>(type: "bigint", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    title = table.Column<string>(type: "text", nullable: false),
                    number = table.Column<string>(type: "text", nullable: true),
                    issuing_body = table.Column<string>(type: "text", nullable: true),
                    issued_on = table.Column<DateOnly>(type: "date", nullable: true),
                    expires_on = table.Column<DateOnly>(type: "date", nullable: true),
                    file_path = table.Column<string>(type: "text", nullable: true),
                    note = table.Column<string>(type: "text", nullable: true),
                    supersedes_id = table.Column<long>(type: "bigint", nullable: true),
                    superseded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    superseded_by = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false),
                    created_by_name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employee_document", x => x.id);
                    table.ForeignKey(
                        name: "fk_employee_document_employee_employee_id",
                        column: x => x.employee_id,
                        principalSchema: "hr",
                        principalTable: "employee",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "employment_policy",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    probation_months = table.Column<int>(type: "integer", nullable: false),
                    notice_period_days = table.Column<int>(type: "integer", nullable: false),
                    adjust_notice_pay = table.Column<bool>(type: "boolean", nullable: false),
                    encash_leave_on_settlement = table.Column<bool>(type: "boolean", nullable: false),
                    encashable_leave_type_id = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employment_policy", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "issued_letter",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    employee_id = table.Column<long>(type: "bigint", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    letter_no = table.Column<string>(type: "text", nullable: false),
                    title = table.Column<string>(type: "text", nullable: false),
                    body = table.Column<string>(type: "text", nullable: false),
                    issued_on = table.Column<DateOnly>(type: "date", nullable: false),
                    separation_id = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false),
                    created_by_name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_issued_letter", x => x.id);
                    table.ForeignKey(
                        name: "fk_issued_letter_employee_employee_id",
                        column: x => x.employee_id,
                        principalSchema: "hr",
                        principalTable: "employee",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "letter_template",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    title = table.Column<string>(type: "text", nullable: false),
                    body = table.Column<string>(type: "text", nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_letter_template", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "notification_setting",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    days_before = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notification_setting", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "separation",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    employee_id = table.Column<long>(type: "bigint", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    state = table.Column<string>(type: "text", nullable: false),
                    notice_on = table.Column<DateOnly>(type: "date", nullable: false),
                    notice_days = table.Column<int>(type: "integer", nullable: false),
                    intended_last_working_day = table.Column<DateOnly>(type: "date", nullable: false),
                    last_working_day = table.Column<DateOnly>(type: "date", nullable: false),
                    relieving_on = table.Column<DateOnly>(type: "date", nullable: true),
                    reason = table.Column<string>(type: "text", nullable: true),
                    exit_interview_note = table.Column<string>(type: "text", nullable: true),
                    computed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    computed_by = table.Column<long>(type: "bigint", nullable: true),
                    net_payable_taka = table.Column<long>(type: "bigint", nullable: false),
                    computation_notes = table.Column<string>(type: "text", nullable: true),
                    approval_request_id = table.Column<long>(type: "bigint", nullable: true),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    approved_by = table.Column<long>(type: "bigint", nullable: true),
                    paid_run_id = table.Column<long>(type: "bigint", nullable: true),
                    paid_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    documents_issued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false),
                    created_by_name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_separation", x => x.id);
                    table.ForeignKey(
                        name: "fk_separation_employee_employee_id",
                        column: x => x.employee_id,
                        principalSchema: "hr",
                        principalTable: "employee",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "clearance_item",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    separation_id = table.Column<long>(type: "bigint", nullable: false),
                    department = table.Column<string>(type: "text", nullable: false),
                    org_unit_id = table.Column<long>(type: "bigint", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false),
                    note = table.Column<string>(type: "text", nullable: true),
                    dues_taka = table.Column<long>(type: "bigint", nullable: false),
                    signed_off_by = table.Column<long>(type: "bigint", nullable: true),
                    signed_off_by_name = table.Column<string>(type: "text", nullable: true),
                    signed_off_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_clearance_item", x => x.id);
                    table.ForeignKey(
                        name: "fk_clearance_item_separation_separation_id",
                        column: x => x.separation_id,
                        principalSchema: "hr",
                        principalTable: "separation",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "settlement_line",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    separation_id = table.Column<long>(type: "bigint", nullable: false),
                    ordinal = table.Column<int>(type: "integer", nullable: false),
                    label = table.Column<string>(type: "text", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    amount_taka = table.Column<long>(type: "bigint", nullable: false),
                    source = table.Column<string>(type: "text", nullable: false),
                    basis = table.Column<string>(type: "text", nullable: true),
                    added_by = table.Column<long>(type: "bigint", nullable: true),
                    added_by_name = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_settlement_line", x => x.id);
                    table.ForeignKey(
                        name: "fk_settlement_line_separation_separation_id",
                        column: x => x.separation_id,
                        principalSchema: "hr",
                        principalTable: "separation",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_employee_branch_id_contract_ends_on",
                schema: "hr",
                table: "employee",
                columns: new[] { "branch_id", "contract_ends_on" },
                filter: "contract_ends_on IS NOT NULL AND separated_on IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_employee_branch_id_probation_due_on",
                schema: "hr",
                table: "employee",
                columns: new[] { "branch_id", "probation_due_on" },
                filter: "probation_due_on IS NOT NULL AND separated_on IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_clearance_item_separation_id_department",
                schema: "hr",
                table: "clearance_item",
                columns: new[] { "separation_id", "department" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_employee_dependant_employee_id",
                schema: "hr",
                table: "employee_dependant",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_employee_dependant_employee_id_is_nominee_superseded_at",
                schema: "hr",
                table: "employee_dependant",
                columns: new[] { "employee_id", "is_nominee", "superseded_at" });

            migrationBuilder.CreateIndex(
                name: "ix_employee_document_branch_id_expires_on",
                schema: "hr",
                table: "employee_document",
                columns: new[] { "branch_id", "expires_on" },
                filter: "expires_on IS NOT NULL AND superseded_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_employee_document_employee_id",
                schema: "hr",
                table: "employee_document",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_employment_policy_branch_id_effective_from",
                schema: "hr",
                table: "employment_policy",
                columns: new[] { "branch_id", "effective_from" });

            migrationBuilder.CreateIndex(
                name: "ix_issued_letter_branch_id_letter_no",
                schema: "hr",
                table: "issued_letter",
                columns: new[] { "branch_id", "letter_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_issued_letter_employee_id_issued_on",
                schema: "hr",
                table: "issued_letter",
                columns: new[] { "employee_id", "issued_on" });

            migrationBuilder.CreateIndex(
                name: "ix_letter_template_branch_id_kind",
                schema: "hr",
                table: "letter_template",
                columns: new[] { "branch_id", "kind" },
                unique: true,
                filter: "active");

            migrationBuilder.CreateIndex(
                name: "ix_notification_setting_branch_id_kind",
                schema: "hr",
                table: "notification_setting",
                columns: new[] { "branch_id", "kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_separation_branch_id_state",
                schema: "hr",
                table: "separation",
                columns: new[] { "branch_id", "state" });

            migrationBuilder.CreateIndex(
                name: "ix_separation_employee_id",
                schema: "hr",
                table: "separation",
                column: "employee_id",
                unique: true,
                filter: "state <> 'cancelled'");

            migrationBuilder.CreateIndex(
                name: "ix_settlement_line_separation_id_ordinal",
                schema: "hr",
                table: "settlement_line",
                columns: new[] { "separation_id", "ordinal" });

            // ---- Invariants EF cannot express (spec 0056). Each is a fact the services also check;
            // the constraint is what makes it impossible rather than merely checked, and is the only
            // guard that survives a future caller nobody has written yet.

            // The employment policy is effective-dated like every other ADR-0027 row, so it gets the
            // same treatment: no overlapping periods, ever.
            migrationBuilder.Sql("""
                ALTER TABLE hr.employment_policy
                ADD CONSTRAINT ck_employment_policy_effective_order
                    CHECK (effective_to IS NULL OR effective_to >= effective_from);

                ALTER TABLE hr.employment_policy
                ADD CONSTRAINT ex_employment_policy_no_overlap
                    EXCLUDE USING gist (
                        branch_id WITH =,
                        daterange(effective_from, COALESCE(effective_to, 'infinity'::date), '[]') WITH &&
                    );
                """);

            // A nomination is an instruction to pay a share of someone's provident fund or gratuity.
            // A share outside 0–100%, or a nominee with no stated purpose, is an instruction the
            // employer could not execute on the day it matters most.
            migrationBuilder.Sql("""
                ALTER TABLE hr.employee_dependant
                ADD CONSTRAINT ck_employee_dependant_share
                    CHECK (share_bp BETWEEN 0 AND 10000);

                ALTER TABLE hr.employee_dependant
                ADD CONSTRAINT ck_employee_dependant_nominee_purpose
                    CHECK (NOT is_nominee OR purpose IS NOT NULL);
                """);

            // A document cannot expire before it was issued, and a professional licence without the
            // body that issued it cannot be verified by anyone.
            migrationBuilder.Sql("""
                ALTER TABLE hr.employee_document
                ADD CONSTRAINT ck_employee_document_dates
                    CHECK (expires_on IS NULL OR issued_on IS NULL OR expires_on >= issued_on);

                ALTER TABLE hr.employee_document
                ADD CONSTRAINT ck_employee_document_licence_body
                    CHECK (kind <> 'licence' OR issuing_body IS NOT NULL);
                """);

            // Relieving never precedes the last working day, and notice is never served after it.
            migrationBuilder.Sql("""
                ALTER TABLE hr.separation
                ADD CONSTRAINT ck_separation_dates
                    CHECK (last_working_day >= notice_on
                           AND (relieving_on IS NULL OR relieving_on >= last_working_day));
                """);

            // Settlement lines carry direction in `kind`, so a negative amount would net twice — the
            // classic way a statement's total stops matching the sum of the rows an employee reads.
            migrationBuilder.Sql("""
                ALTER TABLE hr.settlement_line
                ADD CONSTRAINT ck_settlement_line_amount
                    CHECK (amount_taka > 0);

                ALTER TABLE hr.clearance_item
                ADD CONSTRAINT ck_clearance_item_dues
                    CHECK (dues_taka >= 0);
                """);

            migrationBuilder.Sql("""
                ALTER TABLE hr.notification_setting
                ADD CONSTRAINT ck_notification_setting_days
                    CHECK (days_before BETWEEN 0 AND 365);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "clearance_item",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "employee_dependant",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "employee_document",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "employment_policy",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "issued_letter",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "letter_template",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "notification_setting",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "settlement_line",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "separation",
                schema: "hr");

            migrationBuilder.DropIndex(
                name: "ix_employee_branch_id_contract_ends_on",
                schema: "hr",
                table: "employee");

            migrationBuilder.DropIndex(
                name: "ix_employee_branch_id_probation_due_on",
                schema: "hr",
                table: "employee");

            migrationBuilder.DropColumn(
                name: "contract_ends_on",
                schema: "hr",
                table: "employee");

            migrationBuilder.DropColumn(
                name: "employment_type",
                schema: "hr",
                table: "employee");

            migrationBuilder.DropColumn(
                name: "probation_due_on",
                schema: "hr",
                table: "employee");
        }
    }
}

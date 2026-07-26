using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Hms.Billing.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitBill : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "bill");

            migrationBuilder.CreateTable(
                name: "charge_line",
                schema: "bill",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    encounter_id = table.Column<long>(type: "bigint", nullable: true),
                    folio_id = table.Column<long>(type: "bigint", nullable: true),
                    source_module = table.Column<string>(type: "text", nullable: false),
                    catalog_kind = table.Column<string>(type: "text", nullable: false),
                    catalog_id = table.Column<long>(type: "bigint", nullable: false),
                    description_snapshot = table.Column<string>(type: "text", nullable: false),
                    qty = table.Column<int>(type: "integer", nullable: false),
                    unit_price = table.Column<long>(type: "bigint", nullable: false),
                    rate_version_id = table.Column<long>(type: "bigint", nullable: true),
                    amount = table.Column<long>(type: "bigint", nullable: false),
                    doctor_id = table.Column<long>(type: "bigint", nullable: true),
                    referrer_id = table.Column<long>(type: "bigint", nullable: true),
                    test_order_id = table.Column<long>(type: "bigint", nullable: true),
                    invoice_id = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_charge_line", x => x.id);
                    table.CheckConstraint("ck_charge_parent", "num_nonnulls(encounter_id, folio_id) = 1");
                });

            migrationBuilder.CreateTable(
                name: "counter",
                schema: "bill",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_counter", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "counter_session",
                schema: "bill",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    counter_id = table.Column<long>(type: "bigint", nullable: false),
                    operator_id = table.Column<long>(type: "bigint", nullable: false),
                    business_day = table.Column<DateOnly>(type: "date", nullable: false),
                    opened_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    opening_float = table.Column<long>(type: "bigint", nullable: false),
                    state = table.Column<string>(type: "text", nullable: false),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    counted_cash = table.Column<long>(type: "bigint", nullable: true),
                    expected_cash = table.Column<long>(type: "bigint", nullable: true),
                    variance = table.Column<long>(type: "bigint", nullable: true),
                    close_approved_by = table.Column<long>(type: "bigint", nullable: true),
                    carry_closed = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_counter_session", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "day_close_summary",
                schema: "bill",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    counter_session_id = table.Column<long>(type: "bigint", nullable: false),
                    business_day = table.Column<DateOnly>(type: "date", nullable: false),
                    dept_split = table.Column<string>(type: "jsonb", nullable: false),
                    tender_totals = table.Column<string>(type: "jsonb", nullable: false),
                    gross = table.Column<long>(type: "bigint", nullable: false),
                    discount = table.Column<long>(type: "bigint", nullable: false),
                    net = table.Column<long>(type: "bigint", nullable: false),
                    due_created = table.Column<long>(type: "bigint", nullable: false),
                    due_collected = table.Column<long>(type: "bigint", nullable: false),
                    refunds = table.Column<long>(type: "bigint", nullable: false),
                    variance = table.Column<long>(type: "bigint", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    supersedes = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_day_close_summary", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "due",
                schema: "bill",
                columns: table => new
                {
                    invoice_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    balance = table.Column<long>(type: "bigint", nullable: false),
                    last_followup = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_due", x => x.invoice_id);
                });

            migrationBuilder.CreateTable(
                name: "encounter",
                schema: "bill",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    patient_id = table.Column<long>(type: "bigint", nullable: false),
                    on_date = table.Column<DateOnly>(type: "date", nullable: false),
                    type = table.Column<string>(type: "text", nullable: false),
                    doctor_id = table.Column<long>(type: "bigint", nullable: true),
                    counter_id = table.Column<long>(type: "bigint", nullable: false),
                    state = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_encounter", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "invoice",
                schema: "bill",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    invoice_no = table.Column<string>(type: "text", nullable: false),
                    fiscal_year = table.Column<string>(type: "text", nullable: false),
                    patient_id = table.Column<long>(type: "bigint", nullable: false),
                    encounter_id = table.Column<long>(type: "bigint", nullable: false),
                    counter_session_id = table.Column<long>(type: "bigint", nullable: false),
                    gross = table.Column<long>(type: "bigint", nullable: false),
                    discount = table.Column<long>(type: "bigint", nullable: false),
                    discount_approval_id = table.Column<long>(type: "bigint", nullable: true),
                    tax = table.Column<long>(type: "bigint", nullable: false),
                    tax_code = table.Column<string>(type: "text", nullable: true),
                    net = table.Column<long>(type: "bigint", nullable: false),
                    rounding_adj = table.Column<short>(type: "smallint", nullable: false),
                    state = table.Column<string>(type: "text", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invoice", x => x.id);
                    table.CheckConstraint("ck_invoice_identity", "net = gross - discount + tax + rounding_adj");
                });

            migrationBuilder.CreateTable(
                name: "invoice_line",
                schema: "bill",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    invoice_id = table.Column<long>(type: "bigint", nullable: false),
                    charge_line_id = table.Column<long>(type: "bigint", nullable: false),
                    description_snapshot = table.Column<string>(type: "text", nullable: false),
                    qty = table.Column<int>(type: "integer", nullable: false),
                    unit_price = table.Column<long>(type: "bigint", nullable: false),
                    amount = table.Column<long>(type: "bigint", nullable: false),
                    rate_version_id = table.Column<long>(type: "bigint", nullable: true),
                    doctor_id = table.Column<long>(type: "bigint", nullable: true),
                    referrer_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invoice_line", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "receipt",
                schema: "bill",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    receipt_no = table.Column<string>(type: "text", nullable: false),
                    invoice_id = table.Column<long>(type: "bigint", nullable: false),
                    counter_session_id = table.Column<long>(type: "bigint", nullable: false),
                    amount = table.Column<long>(type: "bigint", nullable: false),
                    tender = table.Column<string>(type: "text", nullable: false),
                    tender_ref = table.Column<string>(type: "text", nullable: true),
                    operator_id = table.Column<long>(type: "bigint", nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    refund_of_receipt = table.Column<long>(type: "bigint", nullable: true),
                    approval_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_receipt", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_day_close_summary_counter_session_id_version",
                schema: "bill",
                table: "day_close_summary",
                columns: new[] { "counter_session_id", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_invoice_invoice_no",
                schema: "bill",
                table: "invoice",
                column: "invoice_no",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_receipt_counter_session_id",
                schema: "bill",
                table: "receipt",
                column: "counter_session_id");

            migrationBuilder.CreateIndex(
                name: "ix_receipt_receipt_no",
                schema: "bill",
                table: "receipt",
                column: "receipt_no",
                unique: true);

            // Hand-written SQL (00 §2): the constraints the money guarantees live on.
            migrationBuilder.Sql("""
                -- ADR-0015 #1: one open session per counter, by constraint not code
                CREATE UNIQUE INDEX one_open_session_per_counter
                  ON bill.counter_session(counter_id) WHERE state IN ('opened','active','reopened');

                -- 03 §4: receipts are immutable once their session closed (edge 20)
                CREATE OR REPLACE FUNCTION bill.reject_receipt_mutation() RETURNS trigger AS $fn$
                BEGIN
                  IF EXISTS (SELECT 1 FROM bill.counter_session s
                             WHERE s.id = OLD.counter_session_id AND s.state = 'closed') THEN
                    RAISE EXCEPTION 'receipt % belongs to a closed session and is immutable', OLD.id;
                  END IF;
                  RETURN NEW;
                END $fn$ LANGUAGE plpgsql;
                CREATE TRIGGER trg_receipt_immutable
                  BEFORE UPDATE OR DELETE ON bill.receipt
                  FOR EACH ROW EXECUTE FUNCTION bill.reject_receipt_mutation();

                -- C5 / G11: the app role cannot DELETE financial rows or touch audit history.
                DO $do$
                BEGIN
                  IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'hms_app') THEN
                    CREATE ROLE hms_app LOGIN PASSWORD 'hms_app_test';   -- dev/test only; compose db-init owns prod
                  END IF;
                END $do$;
                GRANT USAGE ON SCHEMA bill, kernel TO hms_app;
                GRANT SELECT, INSERT, UPDATE ON ALL TABLES IN SCHEMA bill TO hms_app;
                GRANT SELECT, INSERT ON kernel.audit_event TO hms_app;
                GRANT SELECT, USAGE ON ALL SEQUENCES IN SCHEMA bill TO hms_app;
                REVOKE DELETE ON ALL TABLES IN SCHEMA bill FROM hms_app;
                REVOKE UPDATE, DELETE ON kernel.audit_event FROM hms_app;
                """);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "charge_line",
                schema: "bill");

            migrationBuilder.DropTable(
                name: "counter",
                schema: "bill");

            migrationBuilder.DropTable(
                name: "counter_session",
                schema: "bill");

            migrationBuilder.DropTable(
                name: "day_close_summary",
                schema: "bill");

            migrationBuilder.DropTable(
                name: "due",
                schema: "bill");

            migrationBuilder.DropTable(
                name: "encounter",
                schema: "bill");

            migrationBuilder.DropTable(
                name: "invoice",
                schema: "bill");

            migrationBuilder.DropTable(
                name: "invoice_line",
                schema: "bill");

            migrationBuilder.DropTable(
                name: "receipt",
                schema: "bill");
        }
    }
}

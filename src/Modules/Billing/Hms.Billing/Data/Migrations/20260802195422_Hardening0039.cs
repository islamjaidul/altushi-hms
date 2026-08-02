using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Hms.Billing.Data.Migrations
{
    /// <inheritdoc />
    public partial class Hardening0039 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE bill.receipt ADD CONSTRAINT ck_receipt_tender_ref_len "
                + "CHECK (length(tender_ref) <= 40) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE bill.receipt ADD CONSTRAINT ck_receipt_tender_len "
                + "CHECK (length(tender) <= 40) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE bill.receipt ADD CONSTRAINT ck_receipt_receipt_no_len "
                + "CHECK (length(receipt_no) <= 40) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE bill.invoice_line ADD CONSTRAINT ck_invoice_line_description_snapshot_len "
                + "CHECK (length(description_snapshot) <= 200) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE bill.invoice ADD CONSTRAINT ck_invoice_tax_code_len "
                + "CHECK (length(tax_code) <= 40) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE bill.invoice ADD CONSTRAINT ck_invoice_state_len "
                + "CHECK (length(state) <= 40) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE bill.invoice ADD CONSTRAINT ck_invoice_invoice_no_len "
                + "CHECK (length(invoice_no) <= 40) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE bill.invoice ADD CONSTRAINT ck_invoice_fiscal_year_len "
                + "CHECK (length(fiscal_year) <= 40) NOT VALID;");


            migrationBuilder.Sql(
                "ALTER TABLE bill.encounter ADD CONSTRAINT ck_encounter_type_len "
                + "CHECK (length(type) <= 40) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE bill.encounter ADD CONSTRAINT ck_encounter_state_len "
                + "CHECK (length(state) <= 40) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE bill.due ADD CONSTRAINT ck_due_last_followup_len "
                + "CHECK (length(last_followup) <= 4000) NOT VALID;");

            migrationBuilder.AlterColumn<long>(
                name: "invoice_id",
                schema: "bill",
                table: "due",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint")
                .OldAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            migrationBuilder.Sql(
                "ALTER TABLE bill.counter_session ADD CONSTRAINT ck_counter_session_state_len "
                + "CHECK (length(state) <= 40) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE bill.counter ADD CONSTRAINT ck_counter_name_len "
                + "CHECK (length(name) <= 200) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE bill.counter ADD CONSTRAINT ck_counter_kind_len "
                + "CHECK (length(kind) <= 40) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE bill.charge_line ADD CONSTRAINT ck_charge_line_source_module_len "
                + "CHECK (length(source_module) <= 40) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE bill.charge_line ADD CONSTRAINT ck_charge_line_description_snapshot_len "
                + "CHECK (length(description_snapshot) <= 200) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE bill.charge_line ADD CONSTRAINT ck_charge_line_catalog_kind_len "
                + "CHECK (length(catalog_kind) <= 40) NOT VALID;");

            migrationBuilder.CreateIndex(
                name: "ix_receipt_invoice_id",
                schema: "bill",
                table: "receipt",
                column: "invoice_id");

            migrationBuilder.Sql("""
                ALTER TABLE bill.receipt ADD CONSTRAINT ck_receipt_tender
                    CHECK (tender IN ('cash','card','bkash','nagad','corporate')) NOT VALID;
                """);

            migrationBuilder.CreateIndex(
                name: "ix_invoice_line_charge_line_id",
                schema: "bill",
                table: "invoice_line",
                column: "charge_line_id");

            migrationBuilder.CreateIndex(
                name: "ix_invoice_line_invoice_id",
                schema: "bill",
                table: "invoice_line",
                column: "invoice_id");

            migrationBuilder.Sql("""
                ALTER TABLE bill.invoice_line ADD CONSTRAINT ck_invoice_line_money
                    CHECK (unit_price >= 0 AND amount = qty::bigint * unit_price) NOT VALID;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE bill.invoice_line ADD CONSTRAINT ck_invoice_line_qty
                    CHECK (qty <> 0 AND qty BETWEEN -9999 AND 9999) NOT VALID;
                """);

            migrationBuilder.CreateIndex(
                name: "ix_invoice_counter_session_id",
                schema: "bill",
                table: "invoice",
                column: "counter_session_id");

            migrationBuilder.CreateIndex(
                name: "ix_invoice_created_at",
                schema: "bill",
                table: "invoice",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_invoice_patient_id",
                schema: "bill",
                table: "invoice",
                column: "patient_id");

            migrationBuilder.Sql("""
                ALTER TABLE bill.invoice ADD CONSTRAINT ck_invoice_bounds
                    CHECK (gross >= 0 AND tax >= 0 AND discount BETWEEN 0 AND gross) NOT VALID;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE bill.invoice ADD CONSTRAINT ck_invoice_state
                    CHECK (state IN ('draft','billed','partially_paid','paid','cancelled','refunded')) NOT VALID;
                """);

            migrationBuilder.CreateIndex(
                name: "ix_encounter_counter_id",
                schema: "bill",
                table: "encounter",
                column: "counter_id");

            migrationBuilder.CreateIndex(
                name: "ix_encounter_on_date_type",
                schema: "bill",
                table: "encounter",
                columns: new[] { "on_date", "type" });

            migrationBuilder.CreateIndex(
                name: "ix_encounter_patient_id_on_date",
                schema: "bill",
                table: "encounter",
                columns: new[] { "patient_id", "on_date" });

            migrationBuilder.Sql("""
                ALTER TABLE bill.encounter ADD CONSTRAINT ck_encounter_state
                    CHECK (state IN ('open','closed')) NOT VALID;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE bill.due ADD CONSTRAINT ck_due_balance
                    CHECK (balance >= 0) NOT VALID;
                """);

            migrationBuilder.CreateIndex(
                name: "ix_counter_session_counter_id",
                schema: "bill",
                table: "counter_session",
                column: "counter_id");

            migrationBuilder.Sql("""
                ALTER TABLE bill.counter_session ADD CONSTRAINT ck_session_closed_complete
                    CHECK (state <> 'closed' OR (closed_at IS NOT NULL AND counted_cash IS NOT NULL AND expected_cash IS NOT NULL AND variance IS NOT NULL)) NOT VALID;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE bill.counter_session ADD CONSTRAINT ck_session_opening_float
                    CHECK (opening_float >= 0) NOT VALID;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE bill.counter_session ADD CONSTRAINT ck_session_state
                    CHECK (state IN ('opened','active','close_pending','closed','reopened')) NOT VALID;
                """);

            migrationBuilder.CreateIndex(
                name: "ix_charge_line_invoice_id",
                schema: "bill",
                table: "charge_line",
                column: "invoice_id");

            migrationBuilder.CreateIndex(
                name: "ix_charge_line_unbilled_encounter",
                schema: "bill",
                table: "charge_line",
                column: "encounter_id",
                filter: "invoice_id IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_charge_line_unbilled_folio",
                schema: "bill",
                table: "charge_line",
                column: "folio_id",
                filter: "invoice_id IS NULL");

            migrationBuilder.Sql("""
                ALTER TABLE bill.charge_line ADD CONSTRAINT ck_charge_line_money
                    CHECK (unit_price >= 0 AND amount = qty::bigint * unit_price) NOT VALID;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE bill.charge_line ADD CONSTRAINT ck_charge_line_qty
                    CHECK (qty <> 0 AND qty BETWEEN -9999 AND 9999) NOT VALID;
                """);

            migrationBuilder.Sql(
                "ALTER TABLE bill.charge_line ADD CONSTRAINT fk_charge_line_encounter_encounter_id "
                + "FOREIGN KEY (encounter_id) REFERENCES bill.encounter (id) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE bill.charge_line ADD CONSTRAINT fk_charge_line_invoices_invoice_id "
                + "FOREIGN KEY (invoice_id) REFERENCES bill.invoice (id) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE bill.counter_session ADD CONSTRAINT fk_counter_session_counter_counter_id "
                + "FOREIGN KEY (counter_id) REFERENCES bill.counter (id) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE bill.day_close_summary ADD CONSTRAINT fk_day_close_summary_counter_session_counter_session_id "
                + "FOREIGN KEY (counter_session_id) REFERENCES bill.counter_session (id) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE bill.due ADD CONSTRAINT fk_due_invoice_invoice_id "
                + "FOREIGN KEY (invoice_id) REFERENCES bill.invoice (id) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE bill.encounter ADD CONSTRAINT fk_encounter_counters_counter_id "
                + "FOREIGN KEY (counter_id) REFERENCES bill.counter (id) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE bill.invoice ADD CONSTRAINT fk_invoice_counter_session_counter_session_id "
                + "FOREIGN KEY (counter_session_id) REFERENCES bill.counter_session (id) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE bill.invoice_line ADD CONSTRAINT fk_invoice_line_charge_line_charge_line_id "
                + "FOREIGN KEY (charge_line_id) REFERENCES bill.charge_line (id) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE bill.invoice_line ADD CONSTRAINT fk_invoice_line_invoice_invoice_id "
                + "FOREIGN KEY (invoice_id) REFERENCES bill.invoice (id) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE bill.receipt ADD CONSTRAINT fk_receipt_invoice_invoice_id "
                + "FOREIGN KEY (invoice_id) REFERENCES bill.invoice (id) NOT VALID;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_charge_line_encounter_encounter_id",
                schema: "bill",
                table: "charge_line");

            migrationBuilder.DropForeignKey(
                name: "fk_charge_line_invoices_invoice_id",
                schema: "bill",
                table: "charge_line");

            migrationBuilder.DropForeignKey(
                name: "fk_counter_session_counter_counter_id",
                schema: "bill",
                table: "counter_session");

            migrationBuilder.DropForeignKey(
                name: "fk_day_close_summary_counter_session_counter_session_id",
                schema: "bill",
                table: "day_close_summary");

            migrationBuilder.DropForeignKey(
                name: "fk_due_invoice_invoice_id",
                schema: "bill",
                table: "due");

            migrationBuilder.DropForeignKey(
                name: "fk_encounter_counters_counter_id",
                schema: "bill",
                table: "encounter");

            migrationBuilder.DropForeignKey(
                name: "fk_invoice_counter_session_counter_session_id",
                schema: "bill",
                table: "invoice");

            migrationBuilder.DropForeignKey(
                name: "fk_invoice_line_charge_line_charge_line_id",
                schema: "bill",
                table: "invoice_line");

            migrationBuilder.DropForeignKey(
                name: "fk_invoice_line_invoice_invoice_id",
                schema: "bill",
                table: "invoice_line");

            migrationBuilder.DropForeignKey(
                name: "fk_receipt_invoice_invoice_id",
                schema: "bill",
                table: "receipt");

            migrationBuilder.DropIndex(
                name: "ix_receipt_invoice_id",
                schema: "bill",
                table: "receipt");

            migrationBuilder.DropCheckConstraint(
                name: "ck_receipt_tender",
                schema: "bill",
                table: "receipt");

            migrationBuilder.DropIndex(
                name: "ix_invoice_line_charge_line_id",
                schema: "bill",
                table: "invoice_line");

            migrationBuilder.DropIndex(
                name: "ix_invoice_line_invoice_id",
                schema: "bill",
                table: "invoice_line");

            migrationBuilder.DropCheckConstraint(
                name: "ck_invoice_line_money",
                schema: "bill",
                table: "invoice_line");

            migrationBuilder.DropCheckConstraint(
                name: "ck_invoice_line_qty",
                schema: "bill",
                table: "invoice_line");

            migrationBuilder.DropIndex(
                name: "ix_invoice_counter_session_id",
                schema: "bill",
                table: "invoice");

            migrationBuilder.DropIndex(
                name: "ix_invoice_created_at",
                schema: "bill",
                table: "invoice");

            migrationBuilder.DropIndex(
                name: "ix_invoice_patient_id",
                schema: "bill",
                table: "invoice");

            migrationBuilder.DropCheckConstraint(
                name: "ck_invoice_bounds",
                schema: "bill",
                table: "invoice");

            migrationBuilder.DropCheckConstraint(
                name: "ck_invoice_state",
                schema: "bill",
                table: "invoice");

            migrationBuilder.DropIndex(
                name: "ix_encounter_counter_id",
                schema: "bill",
                table: "encounter");

            migrationBuilder.DropIndex(
                name: "ix_encounter_on_date_type",
                schema: "bill",
                table: "encounter");

            migrationBuilder.DropIndex(
                name: "ix_encounter_patient_id_on_date",
                schema: "bill",
                table: "encounter");

            migrationBuilder.DropCheckConstraint(
                name: "ck_encounter_state",
                schema: "bill",
                table: "encounter");

            migrationBuilder.DropCheckConstraint(
                name: "ck_due_balance",
                schema: "bill",
                table: "due");

            migrationBuilder.DropIndex(
                name: "ix_counter_session_counter_id",
                schema: "bill",
                table: "counter_session");

            migrationBuilder.DropCheckConstraint(
                name: "ck_session_closed_complete",
                schema: "bill",
                table: "counter_session");

            migrationBuilder.DropCheckConstraint(
                name: "ck_session_opening_float",
                schema: "bill",
                table: "counter_session");

            migrationBuilder.DropCheckConstraint(
                name: "ck_session_state",
                schema: "bill",
                table: "counter_session");

            migrationBuilder.DropIndex(
                name: "ix_charge_line_invoice_id",
                schema: "bill",
                table: "charge_line");

            migrationBuilder.DropIndex(
                name: "ix_charge_line_unbilled_encounter",
                schema: "bill",
                table: "charge_line");

            migrationBuilder.DropIndex(
                name: "ix_charge_line_unbilled_folio",
                schema: "bill",
                table: "charge_line");

            migrationBuilder.DropCheckConstraint(
                name: "ck_charge_line_money",
                schema: "bill",
                table: "charge_line");

            migrationBuilder.DropCheckConstraint(
                name: "ck_charge_line_qty",
                schema: "bill",
                table: "charge_line");

            migrationBuilder.DropColumn(
                name: "xmin",
                schema: "bill",
                table: "invoice");

            migrationBuilder.AlterColumn<string>(
                name: "tender_ref",
                schema: "bill",
                table: "receipt",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "tender",
                schema: "bill",
                table: "receipt",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40);

            migrationBuilder.AlterColumn<string>(
                name: "receipt_no",
                schema: "bill",
                table: "receipt",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40);

            migrationBuilder.AlterColumn<string>(
                name: "description_snapshot",
                schema: "bill",
                table: "invoice_line",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "tax_code",
                schema: "bill",
                table: "invoice",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "state",
                schema: "bill",
                table: "invoice",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40);

            migrationBuilder.AlterColumn<string>(
                name: "invoice_no",
                schema: "bill",
                table: "invoice",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40);

            migrationBuilder.AlterColumn<string>(
                name: "fiscal_year",
                schema: "bill",
                table: "invoice",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40);

            migrationBuilder.AlterColumn<string>(
                name: "type",
                schema: "bill",
                table: "encounter",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40);

            migrationBuilder.AlterColumn<string>(
                name: "state",
                schema: "bill",
                table: "encounter",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40);

            migrationBuilder.AlterColumn<string>(
                name: "last_followup",
                schema: "bill",
                table: "due",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(4000)",
                oldMaxLength: 4000,
                oldNullable: true);

            migrationBuilder.AlterColumn<long>(
                name: "invoice_id",
                schema: "bill",
                table: "due",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint")
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            migrationBuilder.AlterColumn<string>(
                name: "state",
                schema: "bill",
                table: "counter_session",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40);

            migrationBuilder.AlterColumn<string>(
                name: "name",
                schema: "bill",
                table: "counter",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "kind",
                schema: "bill",
                table: "counter",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40);

            migrationBuilder.AlterColumn<string>(
                name: "source_module",
                schema: "bill",
                table: "charge_line",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40);

            migrationBuilder.AlterColumn<string>(
                name: "description_snapshot",
                schema: "bill",
                table: "charge_line",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "catalog_kind",
                schema: "bill",
                table: "charge_line",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40);
        }
    }
}

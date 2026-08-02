using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hms.Ipd.Data.Migrations
{
    /// <inheritdoc />
    public partial class Hardening0039 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE ipd.ward ADD CONSTRAINT ck_ward_name_len "
                + "CHECK (length(name) <= 200) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE ipd.ward ADD CONSTRAINT ck_ward_class_len "
                + "CHECK (length(class) <= 40) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE ipd.indent ADD CONSTRAINT ck_indent_state_len "
                + "CHECK (length(state) <= 40) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE ipd.indent ADD CONSTRAINT ck_indent_note_len "
                + "CHECK (length(note) <= 4000) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE ipd.folio ADD CONSTRAINT ck_folio_state_len "
                + "CHECK (length(state) <= 40) NOT VALID;");


            migrationBuilder.Sql(
                "ALTER TABLE ipd.certificate ADD CONSTRAINT ck_certificate_kind_len "
                + "CHECK (length(kind) <= 40) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE ipd.certificate ADD CONSTRAINT ck_certificate_cert_no_len "
                + "CHECK (length(cert_no) <= 40) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE ipd.bed ADD CONSTRAINT ck_bed_state_reason_len "
                + "CHECK (length(state_reason) <= 4000) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE ipd.bed ADD CONSTRAINT ck_bed_state_len "
                + "CHECK (length(state) <= 40) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE ipd.bed ADD CONSTRAINT ck_bed_code_len "
                + "CHECK (length(code) <= 40) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE ipd.admission_package ADD CONSTRAINT ck_admission_package_name_len "
                + "CHECK (length(name) <= 200) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE ipd.admission ADD CONSTRAINT ck_admission_state_len "
                + "CHECK (length(state) <= 40) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE ipd.admission ADD CONSTRAINT ck_admission_source_len "
                + "CHECK (length(source) <= 40) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE ipd.admission ADD CONSTRAINT ck_admission_provisional_dx_len "
                + "CHECK (length(provisional_dx) <= 10000) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE ipd.admission ADD CONSTRAINT ck_admission_clinical_summary_len "
                + "CHECK (length(clinical_summary) <= 10000) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE ipd.admission ADD CONSTRAINT ck_admission_blocked_from_len "
                + "CHECK (length(blocked_from) <= 40) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE ipd.admission ADD CONSTRAINT ck_admission_admission_no_len "
                + "CHECK (length(admission_no) <= 40) NOT VALID;");


            migrationBuilder.Sql("""
                ALTER TABLE ipd.indent_item ADD CONSTRAINT ck_indent_item_qty
                    CHECK (qty_requested > 0 AND qty_issued BETWEEN 0 AND qty_requested AND qty_returned BETWEEN 0 AND qty_issued) NOT VALID;
                """);

            migrationBuilder.CreateIndex(
                name: "ix_indent_admission_id",
                schema: "ipd",
                table: "indent",
                column: "admission_id");

            migrationBuilder.CreateIndex(
                name: "ix_indent_folio_id",
                schema: "ipd",
                table: "indent",
                column: "folio_id");

            migrationBuilder.Sql("""
                ALTER TABLE ipd.indent ADD CONSTRAINT ck_indent_issued
                    CHECK (state <> 'issued' OR (issued_by IS NOT NULL AND issued_at IS NOT NULL)) NOT VALID;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE ipd.indent ADD CONSTRAINT ck_indent_state
                    CHECK (state IN ('requested','issued','cancelled')) NOT VALID;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE ipd.folio ADD CONSTRAINT ck_folio_advance
                    CHECK (advance_applied >= 0) NOT VALID;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE ipd.folio ADD CONSTRAINT ck_folio_locked
                    CHECK (state <> 'locked' OR settlement_invoice_id IS NOT NULL) NOT VALID;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE ipd.folio ADD CONSTRAINT ck_folio_state
                    CHECK (state IN ('open','blocked','settlement_draft','locked')) NOT VALID;
                """);

            migrationBuilder.CreateIndex(
                name: "ix_certificate_admission_id",
                schema: "ipd",
                table: "certificate",
                column: "admission_id");

            migrationBuilder.CreateIndex(
                name: "ix_bed_stay_admission_id1",
                schema: "ipd",
                table: "bed_stay",
                column: "admission_id",
                filter: "to_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_bed_day_bed_id",
                schema: "ipd",
                table: "bed_day",
                column: "bed_id");

            migrationBuilder.CreateIndex(
                name: "ix_bed_state",
                schema: "ipd",
                table: "bed",
                column: "state");

            migrationBuilder.Sql("""
                ALTER TABLE ipd.bed ADD CONSTRAINT ck_bed_state
                    CHECK (state IN ('free','reserved','occupied','cleaning','out_of_service')) NOT VALID;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE ipd.admission_package ADD CONSTRAINT ck_admission_package_pct
                    CHECK (default_service_charge_pct BETWEEN 0 AND 100) NOT VALID;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE ipd.admission ADD CONSTRAINT ck_admission_blocked_from
                    CHECK (blocked_from IS NULL OR blocked_from IN ('reserved','admitted','blocked','discharge_initiated','clinically_cleared','financially_settled','discharged','death','absconded')) NOT VALID;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE ipd.admission ADD CONSTRAINT ck_admission_discharged
                    CHECK (state NOT IN ('discharged','death','absconded') OR discharged_at IS NOT NULL) NOT VALID;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE ipd.admission ADD CONSTRAINT ck_admission_service_charge_pct
                    CHECK (service_charge_pct BETWEEN 0 AND 100) NOT VALID;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE ipd.admission ADD CONSTRAINT ck_admission_state
                    CHECK (state IN ('reserved','admitted','blocked','discharge_initiated','clinically_cleared','financially_settled','discharged','death','absconded')) NOT VALID;
                """);

            migrationBuilder.Sql(
                "ALTER TABLE ipd.bed ADD CONSTRAINT fk_bed_ward_ward_id "
                + "FOREIGN KEY (ward_id) REFERENCES ipd.ward (id) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE ipd.bed_day ADD CONSTRAINT fk_bed_day_admission_admission_id "
                + "FOREIGN KEY (admission_id) REFERENCES ipd.admission (id) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE ipd.bed_day ADD CONSTRAINT fk_bed_day_bed_bed_id "
                + "FOREIGN KEY (bed_id) REFERENCES ipd.bed (id) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE ipd.bed_stay ADD CONSTRAINT fk_bed_stay_admission_admission_id "
                + "FOREIGN KEY (admission_id) REFERENCES ipd.admission (id) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE ipd.bed_stay ADD CONSTRAINT fk_bed_stay_bed_bed_id "
                + "FOREIGN KEY (bed_id) REFERENCES ipd.bed (id) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE ipd.certificate ADD CONSTRAINT fk_certificate_admission_admission_id "
                + "FOREIGN KEY (admission_id) REFERENCES ipd.admission (id) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE ipd.folio ADD CONSTRAINT fk_folio_admission_admission_id "
                + "FOREIGN KEY (admission_id) REFERENCES ipd.admission (id) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE ipd.indent ADD CONSTRAINT fk_indent_admission_admission_id "
                + "FOREIGN KEY (admission_id) REFERENCES ipd.admission (id) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE ipd.indent ADD CONSTRAINT fk_indent_folio_folio_id "
                + "FOREIGN KEY (folio_id) REFERENCES ipd.folio (id) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE ipd.indent_item ADD CONSTRAINT fk_indent_item_indent_indent_id "
                + "FOREIGN KEY (indent_id) REFERENCES ipd.indent (id) NOT VALID;");

            // WP2 Tier-2 #9/#10 — not expressible in EF fluent. Guarded: on a database whose
            // legacy rows already violate one of these (two open stays for one admission), the
            // constraint is skipped with a loud warning instead of refusing to boot a hospital;
            // reconcile and re-run the statement by hand. A fresh database always gets them.
            migrationBuilder.Sql("""
                CREATE EXTENSION IF NOT EXISTS btree_gist;
                DO $$ BEGIN
                    ALTER TABLE ipd.bed_stay ADD CONSTRAINT ex_bed_stay_admission
                        EXCLUDE USING gist (admission_id WITH =,
                            tstzrange(from_at, COALESCE(to_at, 'infinity'::timestamptz)) WITH &&)
                        DEFERRABLE INITIALLY DEFERRED;  -- a transfer closes one stay and opens
                                                        -- the next in one tx; EF orders the
                                                        -- writes, so the rule holds at commit
                EXCEPTION WHEN others THEN
                    RAISE WARNING 'ex_bed_stay_admission not applied: % — reconcile overlapping stays and apply by hand', SQLERRM;
                END $$;
                DO $$ BEGIN
                    ALTER TABLE ipd.bed_stay ADD CONSTRAINT ex_bed_stay_bed
                        EXCLUDE USING gist (bed_id WITH =,
                            tstzrange(from_at, COALESCE(to_at, 'infinity'::timestamptz)) WITH &&)
                        DEFERRABLE INITIALLY DEFERRED;
                EXCEPTION WHEN others THEN
                    RAISE WARNING 'ex_bed_stay_bed not applied: % — reconcile double-occupied beds and apply by hand', SQLERRM;
                END $$;
                DO $$ BEGIN
                    CREATE UNIQUE INDEX ux_admission_open_per_patient ON ipd.admission (patient_id)
                        WHERE state NOT IN ('discharged', 'death', 'absconded');
                EXCEPTION WHEN others THEN
                    RAISE WARNING 'ux_admission_open_per_patient not applied: % — reconcile duplicate open admissions and apply by hand', SQLERRM;
                END $$;
                """);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_bed_ward_ward_id",
                schema: "ipd",
                table: "bed");

            migrationBuilder.DropForeignKey(
                name: "fk_bed_day_admission_admission_id",
                schema: "ipd",
                table: "bed_day");

            migrationBuilder.DropForeignKey(
                name: "fk_bed_day_bed_bed_id",
                schema: "ipd",
                table: "bed_day");

            migrationBuilder.DropForeignKey(
                name: "fk_bed_stay_admission_admission_id",
                schema: "ipd",
                table: "bed_stay");

            migrationBuilder.DropForeignKey(
                name: "fk_bed_stay_bed_bed_id",
                schema: "ipd",
                table: "bed_stay");

            migrationBuilder.DropForeignKey(
                name: "fk_certificate_admission_admission_id",
                schema: "ipd",
                table: "certificate");

            migrationBuilder.DropForeignKey(
                name: "fk_folio_admission_admission_id",
                schema: "ipd",
                table: "folio");

            migrationBuilder.DropForeignKey(
                name: "fk_indent_admission_admission_id",
                schema: "ipd",
                table: "indent");

            migrationBuilder.DropForeignKey(
                name: "fk_indent_folio_folio_id",
                schema: "ipd",
                table: "indent");

            migrationBuilder.DropForeignKey(
                name: "fk_indent_item_indent_indent_id",
                schema: "ipd",
                table: "indent_item");

            migrationBuilder.DropCheckConstraint(
                name: "ck_indent_item_qty",
                schema: "ipd",
                table: "indent_item");

            migrationBuilder.DropIndex(
                name: "ix_indent_admission_id",
                schema: "ipd",
                table: "indent");

            migrationBuilder.DropIndex(
                name: "ix_indent_folio_id",
                schema: "ipd",
                table: "indent");

            migrationBuilder.DropCheckConstraint(
                name: "ck_indent_issued",
                schema: "ipd",
                table: "indent");

            migrationBuilder.DropCheckConstraint(
                name: "ck_indent_state",
                schema: "ipd",
                table: "indent");

            migrationBuilder.DropCheckConstraint(
                name: "ck_folio_advance",
                schema: "ipd",
                table: "folio");

            migrationBuilder.DropCheckConstraint(
                name: "ck_folio_locked",
                schema: "ipd",
                table: "folio");

            migrationBuilder.DropCheckConstraint(
                name: "ck_folio_state",
                schema: "ipd",
                table: "folio");

            migrationBuilder.DropIndex(
                name: "ix_certificate_admission_id",
                schema: "ipd",
                table: "certificate");

            migrationBuilder.DropIndex(
                name: "ix_bed_stay_admission_id1",
                schema: "ipd",
                table: "bed_stay");

            migrationBuilder.DropIndex(
                name: "ix_bed_day_bed_id",
                schema: "ipd",
                table: "bed_day");

            migrationBuilder.DropIndex(
                name: "ix_bed_state",
                schema: "ipd",
                table: "bed");

            migrationBuilder.DropCheckConstraint(
                name: "ck_bed_state",
                schema: "ipd",
                table: "bed");

            migrationBuilder.DropCheckConstraint(
                name: "ck_admission_package_pct",
                schema: "ipd",
                table: "admission_package");

            migrationBuilder.DropCheckConstraint(
                name: "ck_admission_blocked_from",
                schema: "ipd",
                table: "admission");

            migrationBuilder.DropCheckConstraint(
                name: "ck_admission_discharged",
                schema: "ipd",
                table: "admission");

            migrationBuilder.DropCheckConstraint(
                name: "ck_admission_service_charge_pct",
                schema: "ipd",
                table: "admission");

            migrationBuilder.DropCheckConstraint(
                name: "ck_admission_state",
                schema: "ipd",
                table: "admission");

            migrationBuilder.DropColumn(
                name: "xmin",
                schema: "ipd",
                table: "folio");

            migrationBuilder.DropColumn(
                name: "xmin",
                schema: "ipd",
                table: "admission");

            migrationBuilder.AlterColumn<string>(
                name: "name",
                schema: "ipd",
                table: "ward",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "class",
                schema: "ipd",
                table: "ward",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40);

            migrationBuilder.AlterColumn<string>(
                name: "state",
                schema: "ipd",
                table: "indent",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40);

            migrationBuilder.AlterColumn<string>(
                name: "note",
                schema: "ipd",
                table: "indent",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(4000)",
                oldMaxLength: 4000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "state",
                schema: "ipd",
                table: "folio",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40);

            migrationBuilder.AlterColumn<string>(
                name: "kind",
                schema: "ipd",
                table: "certificate",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40);

            migrationBuilder.AlterColumn<string>(
                name: "cert_no",
                schema: "ipd",
                table: "certificate",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40);

            migrationBuilder.AlterColumn<string>(
                name: "state_reason",
                schema: "ipd",
                table: "bed",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(4000)",
                oldMaxLength: 4000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "state",
                schema: "ipd",
                table: "bed",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40);

            migrationBuilder.AlterColumn<string>(
                name: "code",
                schema: "ipd",
                table: "bed",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40);

            migrationBuilder.AlterColumn<string>(
                name: "name",
                schema: "ipd",
                table: "admission_package",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "state",
                schema: "ipd",
                table: "admission",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40);

            migrationBuilder.AlterColumn<string>(
                name: "source",
                schema: "ipd",
                table: "admission",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40);

            migrationBuilder.AlterColumn<string>(
                name: "provisional_dx",
                schema: "ipd",
                table: "admission",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(10000)",
                oldMaxLength: 10000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "clinical_summary",
                schema: "ipd",
                table: "admission",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(10000)",
                oldMaxLength: 10000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "blocked_from",
                schema: "ipd",
                table: "admission",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "admission_no",
                schema: "ipd",
                table: "admission",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40);
        }
    }
}

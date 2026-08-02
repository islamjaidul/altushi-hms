using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hms.Emr.Data.Migrations
{
    /// <inheritdoc />
    public partial class Hardening0039 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE emr.template ADD CONSTRAINT ck_template_on_examination_len "
                + "CHECK (length(on_examination) <= 10000) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE emr.template ADD CONSTRAINT ck_template_name_len "
                + "CHECK (length(name) <= 200) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE emr.template ADD CONSTRAINT ck_template_diagnosis_len "
                + "CHECK (length(diagnosis) <= 10000) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE emr.template ADD CONSTRAINT ck_template_complaint_len "
                + "CHECK (length(complaint) <= 10000) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE emr.template ADD CONSTRAINT ck_template_advice_len "
                + "CHECK (length(advice) <= 10000) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE emr.receive_note ADD CONSTRAINT ck_receive_note_received_from_len "
                + "CHECK (length(received_from) <= 200) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE emr.receive_note ADD CONSTRAINT ck_receive_note_condition_len "
                + "CHECK (length(condition) <= 10000) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE emr.receive_note ADD CONSTRAINT ck_receive_note_belongings_len "
                + "CHECK (length(belongings) <= 4000) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE emr.note_drug ADD CONSTRAINT ck_note_drug_instruction_len "
                + "CHECK (length(instruction) <= 200) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE emr.note_drug ADD CONSTRAINT ck_note_drug_frequency_len "
                + "CHECK (length(frequency) <= 40) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE emr.note_drug ADD CONSTRAINT ck_note_drug_duration_len "
                + "CHECK (length(duration) <= 40) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE emr.note_drug ADD CONSTRAINT ck_note_drug_drug_name_len "
                + "CHECK (length(drug_name) <= 200) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE emr.note_drug ADD CONSTRAINT ck_note_drug_dose_len "
                + "CHECK (length(dose) <= 40) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE emr.note ADD CONSTRAINT ck_note_state_len "
                + "CHECK (length(state) <= 40) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE emr.note ADD CONSTRAINT ck_note_on_examination_len "
                + "CHECK (length(on_examination) <= 10000) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE emr.note ADD CONSTRAINT ck_note_diagnosis_len "
                + "CHECK (length(diagnosis) <= 10000) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE emr.note ADD CONSTRAINT ck_note_complaint_len "
                + "CHECK (length(complaint) <= 10000) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE emr.note ADD CONSTRAINT ck_note_advice_len "
                + "CHECK (length(advice) <= 10000) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE emr.mar_dose ADD CONSTRAINT ck_mar_dose_state_reason_len "
                + "CHECK (length(state_reason) <= 4000) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE emr.mar_dose ADD CONSTRAINT ck_mar_dose_state_len "
                + "CHECK (length(state) <= 40) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE emr.mar_dose ADD CONSTRAINT ck_mar_dose_route_len "
                + "CHECK (length(route) <= 40) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE emr.mar_dose ADD CONSTRAINT ck_mar_dose_drug_name_len "
                + "CHECK (length(drug_name) <= 200) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE emr.mar_dose ADD CONSTRAINT ck_mar_dose_dose_len "
                + "CHECK (length(dose) <= 40) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE emr.glucose_reading ADD CONSTRAINT ck_glucose_reading_timing_len "
                + "CHECK (length(timing) <= 40) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE emr.glucose_reading ADD CONSTRAINT ck_glucose_reading_insulin_route_len "
                + "CHECK (length(insulin_route) <= 40) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE emr.favourite ADD CONSTRAINT ck_favourite_frequency_len "
                + "CHECK (length(frequency) <= 40) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE emr.favourite ADD CONSTRAINT ck_favourite_duration_len "
                + "CHECK (length(duration) <= 40) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE emr.favourite ADD CONSTRAINT ck_favourite_drug_name_len "
                + "CHECK (length(drug_name) <= 200) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE emr.favourite ADD CONSTRAINT ck_favourite_dose_len "
                + "CHECK (length(dose) <= 40) NOT VALID;");

            migrationBuilder.Sql("""
                ALTER TABLE emr.vitals ADD CONSTRAINT ck_vitals_diastolic
                    CHECK (diastolic IS NULL OR diastolic BETWEEN 20 AND 200) NOT VALID;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE emr.vitals ADD CONSTRAINT ck_vitals_pulse
                    CHECK (pulse IS NULL OR pulse BETWEEN 20 AND 300) NOT VALID;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE emr.vitals ADD CONSTRAINT ck_vitals_spo2
                    CHECK (sp_o2 IS NULL OR sp_o2 BETWEEN 0 AND 100) NOT VALID;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE emr.vitals ADD CONSTRAINT ck_vitals_systolic
                    CHECK (systolic IS NULL OR systolic BETWEEN 40 AND 300) NOT VALID;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE emr.vitals ADD CONSTRAINT ck_vitals_temperature
                    CHECK (temperature_tenths_c IS NULL OR temperature_tenths_c BETWEEN 250 AND 460) NOT VALID;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE emr.vitals ADD CONSTRAINT ck_vitals_weight
                    CHECK (weight_tenths_kg IS NULL OR weight_tenths_kg BETWEEN 1 AND 4000) NOT VALID;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE emr.note ADD CONSTRAINT ck_note_finalised
                    CHECK (state NOT IN ('final','superseded') OR (finalised_at IS NOT NULL AND finalised_by IS NOT NULL)) NOT VALID;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE emr.note ADD CONSTRAINT ck_note_state
                    CHECK (state IN ('draft','final','superseded')) NOT VALID;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE emr.mar_dose ADD CONSTRAINT ck_mar_dose_given
                    CHECK (state <> 'given' OR (administered_at IS NOT NULL AND administered_by IS NOT NULL)) NOT VALID;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE emr.mar_dose ADD CONSTRAINT ck_mar_dose_reason
                    CHECK (state NOT IN ('missed','refused') OR state_reason IS NOT NULL) NOT VALID;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE emr.mar_dose ADD CONSTRAINT ck_mar_dose_state
                    CHECK (state IN ('scheduled','given','missed','refused')) NOT VALID;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE emr.glucose_reading ADD CONSTRAINT ck_glucose_insulin
                    CHECK (insulin_units IS NULL OR insulin_units BETWEEN 0 AND 200) NOT VALID;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE emr.glucose_reading ADD CONSTRAINT ck_glucose_value
                    CHECK (glucose_tenths BETWEEN 5 AND 500) NOT VALID;
                """);

            migrationBuilder.Sql(
                "ALTER TABLE emr.note ADD CONSTRAINT fk_note_note_supersedes_id "
                + "FOREIGN KEY (supersedes_id) REFERENCES emr.note (id) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE emr.note_drug ADD CONSTRAINT fk_note_drug_note_note_id "
                + "FOREIGN KEY (note_id) REFERENCES emr.note (id) NOT VALID;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_note_note_supersedes_id",
                schema: "emr",
                table: "note");

            migrationBuilder.DropForeignKey(
                name: "fk_note_drug_note_note_id",
                schema: "emr",
                table: "note_drug");

            migrationBuilder.DropCheckConstraint(
                name: "ck_vitals_diastolic",
                schema: "emr",
                table: "vitals");

            migrationBuilder.DropCheckConstraint(
                name: "ck_vitals_pulse",
                schema: "emr",
                table: "vitals");

            migrationBuilder.DropCheckConstraint(
                name: "ck_vitals_spo2",
                schema: "emr",
                table: "vitals");

            migrationBuilder.DropCheckConstraint(
                name: "ck_vitals_systolic",
                schema: "emr",
                table: "vitals");

            migrationBuilder.DropCheckConstraint(
                name: "ck_vitals_temperature",
                schema: "emr",
                table: "vitals");

            migrationBuilder.DropCheckConstraint(
                name: "ck_vitals_weight",
                schema: "emr",
                table: "vitals");

            migrationBuilder.DropCheckConstraint(
                name: "ck_note_finalised",
                schema: "emr",
                table: "note");

            migrationBuilder.DropCheckConstraint(
                name: "ck_note_state",
                schema: "emr",
                table: "note");

            migrationBuilder.DropCheckConstraint(
                name: "ck_mar_dose_given",
                schema: "emr",
                table: "mar_dose");

            migrationBuilder.DropCheckConstraint(
                name: "ck_mar_dose_reason",
                schema: "emr",
                table: "mar_dose");

            migrationBuilder.DropCheckConstraint(
                name: "ck_mar_dose_state",
                schema: "emr",
                table: "mar_dose");

            migrationBuilder.DropCheckConstraint(
                name: "ck_glucose_insulin",
                schema: "emr",
                table: "glucose_reading");

            migrationBuilder.DropCheckConstraint(
                name: "ck_glucose_value",
                schema: "emr",
                table: "glucose_reading");

            migrationBuilder.AlterColumn<string>(
                name: "on_examination",
                schema: "emr",
                table: "template",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(10000)",
                oldMaxLength: 10000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "name",
                schema: "emr",
                table: "template",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "diagnosis",
                schema: "emr",
                table: "template",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(10000)",
                oldMaxLength: 10000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "complaint",
                schema: "emr",
                table: "template",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(10000)",
                oldMaxLength: 10000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "advice",
                schema: "emr",
                table: "template",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(10000)",
                oldMaxLength: 10000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "received_from",
                schema: "emr",
                table: "receive_note",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "condition",
                schema: "emr",
                table: "receive_note",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(10000)",
                oldMaxLength: 10000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "belongings",
                schema: "emr",
                table: "receive_note",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(4000)",
                oldMaxLength: 4000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "instruction",
                schema: "emr",
                table: "note_drug",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "frequency",
                schema: "emr",
                table: "note_drug",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "duration",
                schema: "emr",
                table: "note_drug",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "drug_name",
                schema: "emr",
                table: "note_drug",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "dose",
                schema: "emr",
                table: "note_drug",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "state",
                schema: "emr",
                table: "note",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40);

            migrationBuilder.AlterColumn<string>(
                name: "on_examination",
                schema: "emr",
                table: "note",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(10000)",
                oldMaxLength: 10000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "diagnosis",
                schema: "emr",
                table: "note",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(10000)",
                oldMaxLength: 10000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "complaint",
                schema: "emr",
                table: "note",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(10000)",
                oldMaxLength: 10000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "advice",
                schema: "emr",
                table: "note",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(10000)",
                oldMaxLength: 10000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "state_reason",
                schema: "emr",
                table: "mar_dose",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(4000)",
                oldMaxLength: 4000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "state",
                schema: "emr",
                table: "mar_dose",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40);

            migrationBuilder.AlterColumn<string>(
                name: "route",
                schema: "emr",
                table: "mar_dose",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "drug_name",
                schema: "emr",
                table: "mar_dose",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "dose",
                schema: "emr",
                table: "mar_dose",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "timing",
                schema: "emr",
                table: "glucose_reading",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "insulin_route",
                schema: "emr",
                table: "glucose_reading",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "frequency",
                schema: "emr",
                table: "favourite",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "duration",
                schema: "emr",
                table: "favourite",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "drug_name",
                schema: "emr",
                table: "favourite",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "dose",
                schema: "emr",
                table: "favourite",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40,
                oldNullable: true);
        }
    }
}

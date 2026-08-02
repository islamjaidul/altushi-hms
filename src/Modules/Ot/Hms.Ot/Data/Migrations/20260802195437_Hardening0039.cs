using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hms.Ot.Data.Migrations
{
    /// <inheritdoc />
    public partial class Hardening0039 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE ot.theatre ADD CONSTRAINT ck_theatre_name_len "
                + "CHECK (length(name) <= 200) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE ot.ot_case ADD CONSTRAINT ck_ot_case_state_len "
                + "CHECK (length(state) <= 40) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE ot.ot_case ADD CONSTRAINT ck_ot_case_procedure_performed_len "
                + "CHECK (length(procedure_performed) <= 10000) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE ot.ot_case ADD CONSTRAINT ck_ot_case_operation_name_len "
                + "CHECK (length(operation_name) <= 200) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE ot.ot_case ADD CONSTRAINT ck_ot_case_findings_len "
                + "CHECK (length(findings) <= 10000) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE ot.ot_case ADD CONSTRAINT ck_ot_case_case_no_len "
                + "CHECK (length(case_no) <= 40) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE ot.ot_case ADD CONSTRAINT ck_ot_case_cancel_reason_len "
                + "CHECK (length(cancel_reason) <= 4000) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE ot.ot_case ADD CONSTRAINT ck_ot_case_anaesthesia_type_len "
                + "CHECK (length(anaesthesia_type) <= 40) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE ot.case_team ADD CONSTRAINT ck_case_team_role_len "
                + "CHECK (length(role) <= 40) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE ot.case_team ADD CONSTRAINT ck_case_team_person_name_len "
                + "CHECK (length(person_name) <= 200) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE ot.case_consumable ADD CONSTRAINT ck_case_consumable_product_name_len "
                + "CHECK (length(product_name) <= 200) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE ot.case_consumable ADD CONSTRAINT ck_case_consumable_batch_no_len "
                + "CHECK (length(batch_no) <= 40) NOT VALID;");

            migrationBuilder.Sql("""
                ALTER TABLE ot.ot_case ADD CONSTRAINT ck_case_cancel_reason
                    CHECK (state NOT IN ('cancelled','postponed') OR cancel_reason IS NOT NULL) NOT VALID;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE ot.ot_case ADD CONSTRAINT ck_case_finished
                    CHECK (state <> 'completed' OR finished_at IS NOT NULL) NOT VALID;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE ot.ot_case ADD CONSTRAINT ck_case_started
                    CHECK (state NOT IN ('in_theatre','completed') OR started_at IS NOT NULL) NOT VALID;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE ot.ot_case ADD CONSTRAINT ck_case_state
                    CHECK (state IN ('scheduled','patient_ready','in_theatre','completed','cancelled','postponed')) NOT VALID;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE ot.case_team ADD CONSTRAINT ck_case_team_amount
                    CHECK (amount_posted >= 0) NOT VALID;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE ot.case_consumable ADD CONSTRAINT ck_case_consumable_qty
                    CHECK (qty > 0 AND unit_price >= 0) NOT VALID;
                """);

            migrationBuilder.Sql(
                "ALTER TABLE ot.case_consumable ADD CONSTRAINT fk_case_consumable_ot_case_case_id "
                + "FOREIGN KEY (case_id) REFERENCES ot.ot_case (id) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE ot.case_team ADD CONSTRAINT fk_case_team_ot_case_case_id "
                + "FOREIGN KEY (case_id) REFERENCES ot.ot_case (id) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE ot.ot_case ADD CONSTRAINT fk_ot_case_theatre_theatre_id "
                + "FOREIGN KEY (theatre_id) REFERENCES ot.theatre (id) NOT VALID;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_case_consumable_ot_case_case_id",
                schema: "ot",
                table: "case_consumable");

            migrationBuilder.DropForeignKey(
                name: "fk_case_team_ot_case_case_id",
                schema: "ot",
                table: "case_team");

            migrationBuilder.DropForeignKey(
                name: "fk_ot_case_theatre_theatre_id",
                schema: "ot",
                table: "ot_case");

            migrationBuilder.DropCheckConstraint(
                name: "ck_case_cancel_reason",
                schema: "ot",
                table: "ot_case");

            migrationBuilder.DropCheckConstraint(
                name: "ck_case_finished",
                schema: "ot",
                table: "ot_case");

            migrationBuilder.DropCheckConstraint(
                name: "ck_case_started",
                schema: "ot",
                table: "ot_case");

            migrationBuilder.DropCheckConstraint(
                name: "ck_case_state",
                schema: "ot",
                table: "ot_case");

            migrationBuilder.DropCheckConstraint(
                name: "ck_case_team_amount",
                schema: "ot",
                table: "case_team");

            migrationBuilder.DropCheckConstraint(
                name: "ck_case_consumable_qty",
                schema: "ot",
                table: "case_consumable");

            migrationBuilder.AlterColumn<string>(
                name: "name",
                schema: "ot",
                table: "theatre",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "state",
                schema: "ot",
                table: "ot_case",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40);

            migrationBuilder.AlterColumn<string>(
                name: "procedure_performed",
                schema: "ot",
                table: "ot_case",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(10000)",
                oldMaxLength: 10000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "operation_name",
                schema: "ot",
                table: "ot_case",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "findings",
                schema: "ot",
                table: "ot_case",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(10000)",
                oldMaxLength: 10000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "case_no",
                schema: "ot",
                table: "ot_case",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40);

            migrationBuilder.AlterColumn<string>(
                name: "cancel_reason",
                schema: "ot",
                table: "ot_case",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(4000)",
                oldMaxLength: 4000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "anaesthesia_type",
                schema: "ot",
                table: "ot_case",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "role",
                schema: "ot",
                table: "case_team",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40);

            migrationBuilder.AlterColumn<string>(
                name: "person_name",
                schema: "ot",
                table: "case_team",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "product_name",
                schema: "ot",
                table: "case_consumable",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "batch_no",
                schema: "ot",
                table: "case_consumable",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40);
        }
    }
}

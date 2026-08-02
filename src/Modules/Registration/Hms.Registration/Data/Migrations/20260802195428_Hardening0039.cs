using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hms.Registration.Data.Migrations
{
    /// <inheritdoc />
    public partial class Hardening0039 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE reg.patient ADD CONSTRAINT ck_patient_uhid_len "
                + "CHECK (length(uhid) <= 40) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE reg.patient ADD CONSTRAINT ck_patient_phone_len "
                + "CHECK (length(phone) <= 20) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE reg.patient ADD CONSTRAINT ck_patient_patient_type_len "
                + "CHECK (length(patient_type) <= 40) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE reg.patient ADD CONSTRAINT ck_patient_nid_len "
                + "CHECK (length(nid) <= 40) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE reg.patient ADD CONSTRAINT ck_patient_guardian_len "
                + "CHECK (length(guardian) <= 200) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE reg.patient ADD CONSTRAINT ck_patient_full_name_len "
                + "CHECK (length(full_name) <= 200) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE reg.patient ADD CONSTRAINT ck_patient_blood_group_len "
                + "CHECK (length(blood_group) <= 40) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE reg.patient ADD CONSTRAINT ck_patient_area_len "
                + "CHECK (length(area) <= 200) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE reg.patient ADD CONSTRAINT ck_patient_address_len "
                + "CHECK (length(address) <= 500) NOT VALID;");

            migrationBuilder.CreateIndex(
                name: "ix_patient_merged_into",
                schema: "reg",
                table: "patient",
                column: "merged_into");

            migrationBuilder.Sql("""
                ALTER TABLE reg.patient ADD CONSTRAINT ck_patient_age
                    CHECK ((age_years IS NULL OR age_years BETWEEN 0 AND 130) AND (age_months IS NULL OR age_months BETWEEN 0 AND 11)) NOT VALID;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE reg.patient ADD CONSTRAINT ck_patient_dob
                    CHECK (dob IS NULL OR (dob >= '1900-01-01' AND dob <= current_date)) NOT VALID;
                """);

            migrationBuilder.Sql(
                "ALTER TABLE reg.patient ADD CONSTRAINT fk_patient_patient_merged_into "
                + "FOREIGN KEY (merged_into) REFERENCES reg.patient (id) NOT VALID;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_patient_patient_merged_into",
                schema: "reg",
                table: "patient");

            migrationBuilder.DropIndex(
                name: "ix_patient_merged_into",
                schema: "reg",
                table: "patient");

            migrationBuilder.DropCheckConstraint(
                name: "ck_patient_age",
                schema: "reg",
                table: "patient");

            migrationBuilder.DropCheckConstraint(
                name: "ck_patient_dob",
                schema: "reg",
                table: "patient");

            migrationBuilder.AlterColumn<string>(
                name: "uhid",
                schema: "reg",
                table: "patient",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40);

            migrationBuilder.AlterColumn<string>(
                name: "phone",
                schema: "reg",
                table: "patient",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "patient_type",
                schema: "reg",
                table: "patient",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40);

            migrationBuilder.AlterColumn<string>(
                name: "nid",
                schema: "reg",
                table: "patient",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "guardian",
                schema: "reg",
                table: "patient",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "full_name",
                schema: "reg",
                table: "patient",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "blood_group",
                schema: "reg",
                table: "patient",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "area",
                schema: "reg",
                table: "patient",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "address",
                schema: "reg",
                table: "patient",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldNullable: true);
        }
    }
}

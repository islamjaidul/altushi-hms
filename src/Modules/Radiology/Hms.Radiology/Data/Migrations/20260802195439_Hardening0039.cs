using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hms.Radiology.Data.Migrations
{
    /// <inheritdoc />
    public partial class Hardening0039 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE radiology.study ADD CONSTRAINT ck_study_technician_note_len "
                + "CHECK (length(technician_note) <= 4000) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE radiology.study ADD CONSTRAINT ck_study_state_len "
                + "CHECK (length(state) <= 40) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE radiology.study ADD CONSTRAINT ck_study_film_size_len "
                + "CHECK (length(film_size) <= 40) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE radiology.study ADD CONSTRAINT ck_study_accession_no_len "
                + "CHECK (length(accession_no) <= 40) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE radiology.modality ADD CONSTRAINT ck_modality_name_len "
                + "CHECK (length(name) <= 200) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE radiology.modality ADD CONSTRAINT ck_modality_code_len "
                + "CHECK (length(code) <= 40) NOT VALID;");

            migrationBuilder.Sql("""
                ALTER TABLE radiology.study ADD CONSTRAINT ck_study_film_count
                    CHECK (film_count >= 0) NOT VALID;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE radiology.study ADD CONSTRAINT ck_study_state
                    CHECK (state IN ('awaiting','done','reported')) NOT VALID;
                """);

            migrationBuilder.CreateIndex(
                name: "ix_modality_test_modality_id",
                schema: "radiology",
                table: "modality_test",
                column: "modality_id");

            migrationBuilder.Sql(
                "ALTER TABLE radiology.modality_test ADD CONSTRAINT fk_modality_test_modality_modality_id "
                + "FOREIGN KEY (modality_id) REFERENCES radiology.modality (id) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE radiology.study ADD CONSTRAINT fk_study_modality_modality_id "
                + "FOREIGN KEY (modality_id) REFERENCES radiology.modality (id) NOT VALID;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_modality_test_modality_modality_id",
                schema: "radiology",
                table: "modality_test");

            migrationBuilder.DropForeignKey(
                name: "fk_study_modality_modality_id",
                schema: "radiology",
                table: "study");

            migrationBuilder.DropCheckConstraint(
                name: "ck_study_film_count",
                schema: "radiology",
                table: "study");

            migrationBuilder.DropCheckConstraint(
                name: "ck_study_state",
                schema: "radiology",
                table: "study");

            migrationBuilder.DropIndex(
                name: "ix_modality_test_modality_id",
                schema: "radiology",
                table: "modality_test");

            migrationBuilder.AlterColumn<string>(
                name: "technician_note",
                schema: "radiology",
                table: "study",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(4000)",
                oldMaxLength: 4000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "state",
                schema: "radiology",
                table: "study",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40);

            migrationBuilder.AlterColumn<string>(
                name: "film_size",
                schema: "radiology",
                table: "study",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "accession_no",
                schema: "radiology",
                table: "study",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40);

            migrationBuilder.AlterColumn<string>(
                name: "name",
                schema: "radiology",
                table: "modality",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "code",
                schema: "radiology",
                table: "modality",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40);
        }
    }
}

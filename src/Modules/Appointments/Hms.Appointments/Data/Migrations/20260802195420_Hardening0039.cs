using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Hms.Appointments.Data.Migrations
{
    /// <inheritdoc />
    public partial class Hardening0039 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE appt.doctor_schedule ADD CONSTRAINT ck_doctor_schedule_room_len "
                + "CHECK (length(room) <= 40) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE appt.doctor_schedule ADD CONSTRAINT ck_doctor_schedule_doctor_name_len "
                + "CHECK (length(doctor_name) <= 200) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE appt.appointment ADD CONSTRAINT ck_appointment_state_len "
                + "CHECK (length(state) <= 40) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE appt.appointment ADD CONSTRAINT ck_appointment_source_len "
                + "CHECK (length(source) <= 40) NOT VALID;");

            migrationBuilder.CreateTable(
                name: "doctor",
                schema: "appt",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_doctor", x => x.id);
                });

            // WP2.5 backfill: every doctor the schedule already knows becomes a master row with
            // the same id, so history keeps meaning what it meant. The identity sequence starts
            // past the highest adopted id. Rows in appt.appointment naming a doctor no schedule
            // ever had (the audit's 12 orphans) are retained as-is under hard rule 4 — the FK
            // below is NOT VALID: it constrains every new write and leaves history untouched.
            migrationBuilder.Sql("""
                INSERT INTO appt.doctor (id, name, active)
                SELECT DISTINCT ON (doctor_id) doctor_id, doctor_name, true
                FROM appt.doctor_schedule
                ORDER BY doctor_id, id;
                SELECT setval(pg_get_serial_sequence('appt.doctor', 'id'),
                              GREATEST((SELECT COALESCE(MAX(id), 0) FROM appt.doctor), 1),
                              (SELECT COUNT(*) > 0 FROM appt.doctor));
                """);


            migrationBuilder.CreateIndex(
                name: "ix_doctor_schedule_doctor_id",
                schema: "appt",
                table: "doctor_schedule",
                column: "doctor_id");

            migrationBuilder.Sql("""
                ALTER TABLE appt.doctor_schedule ADD CONSTRAINT ck_schedule_max_serials
                    CHECK (max_serials > 0) NOT VALID;
                """);

            migrationBuilder.CreateIndex(
                name: "ix_appointment_on_date",
                schema: "appt",
                table: "appointment",
                column: "on_date");

            migrationBuilder.Sql("""
                ALTER TABLE appt.appointment ADD CONSTRAINT ck_appointment_state
                    CHECK (state IN ('booked','arrived','in_chamber','done','cancelled','no_show')) NOT VALID;
                """);

            migrationBuilder.Sql(
                "ALTER TABLE appt.appointment ADD CONSTRAINT fk_appointment_doctor_doctor_id "
                + "FOREIGN KEY (doctor_id) REFERENCES appt.doctor (id) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE appt.doctor_schedule ADD CONSTRAINT fk_doctor_schedule_doctor_doctor_id "
                + "FOREIGN KEY (doctor_id) REFERENCES appt.doctor (id);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_appointment_doctor_doctor_id",
                schema: "appt",
                table: "appointment");

            migrationBuilder.DropForeignKey(
                name: "fk_doctor_schedule_doctor_doctor_id",
                schema: "appt",
                table: "doctor_schedule");

            migrationBuilder.DropTable(
                name: "doctor",
                schema: "appt");

            migrationBuilder.DropIndex(
                name: "ix_doctor_schedule_doctor_id",
                schema: "appt",
                table: "doctor_schedule");

            migrationBuilder.DropCheckConstraint(
                name: "ck_schedule_max_serials",
                schema: "appt",
                table: "doctor_schedule");

            migrationBuilder.DropIndex(
                name: "ix_appointment_on_date",
                schema: "appt",
                table: "appointment");

            migrationBuilder.DropCheckConstraint(
                name: "ck_appointment_state",
                schema: "appt",
                table: "appointment");

            migrationBuilder.AlterColumn<string>(
                name: "room",
                schema: "appt",
                table: "doctor_schedule",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "doctor_name",
                schema: "appt",
                table: "doctor_schedule",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "state",
                schema: "appt",
                table: "appointment",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40);

            migrationBuilder.AlterColumn<string>(
                name: "source",
                schema: "appt",
                table: "appointment",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40);
        }
    }
}

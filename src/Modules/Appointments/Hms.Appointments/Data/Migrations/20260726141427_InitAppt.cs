using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Hms.Appointments.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitAppt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "appt");

            migrationBuilder.CreateTable(
                name: "appointment",
                schema: "appt",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    patient_id = table.Column<long>(type: "bigint", nullable: false),
                    doctor_id = table.Column<long>(type: "bigint", nullable: false),
                    on_date = table.Column<DateOnly>(type: "date", nullable: false),
                    serial_no = table.Column<int>(type: "integer", nullable: false),
                    state = table.Column<string>(type: "text", nullable: false),
                    source = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_appointment", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "doctor_schedule",
                schema: "appt",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    doctor_id = table.Column<long>(type: "bigint", nullable: false),
                    doctor_name = table.Column<string>(type: "text", nullable: false),
                    weekday = table.Column<int>(type: "integer", nullable: false),
                    slot_from = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    slot_to = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    room = table.Column<string>(type: "text", nullable: true),
                    max_serials = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_doctor_schedule", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_appointment_doctor_id_on_date_serial_no",
                schema: "appt",
                table: "appointment",
                columns: new[] { "doctor_id", "on_date", "serial_no" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "appointment",
                schema: "appt");

            migrationBuilder.DropTable(
                name: "doctor_schedule",
                schema: "appt");
        }
    }
}

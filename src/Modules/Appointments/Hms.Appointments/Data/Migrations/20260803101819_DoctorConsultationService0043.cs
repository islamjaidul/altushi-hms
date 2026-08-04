using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hms.Appointments.Data.Migrations
{
    /// <inheritdoc />
    public partial class DoctorConsultationService0043 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "consultation_service_id",
                schema: "appt",
                table: "doctor",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "consultation_service_id",
                schema: "appt",
                table: "doctor");
        }
    }
}

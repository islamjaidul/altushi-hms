using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hms.Registration.Data.Migrations
{
    /// <inheritdoc />
    public partial class PhoneDigits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "phone_digits",
                schema: "reg",
                table: "patient",
                type: "text",
                nullable: true,
                computedColumnSql: "regexp_replace(coalesce(phone, ''), '\\D', '', 'g')",
                stored: true);

            migrationBuilder.CreateIndex(
                name: "ix_patient_phone_digits",
                schema: "reg",
                table: "patient",
                column: "phone_digits");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_patient_phone_digits",
                schema: "reg",
                table: "patient");

            migrationBuilder.DropColumn(
                name: "phone_digits",
                schema: "reg",
                table: "patient");
        }
    }
}

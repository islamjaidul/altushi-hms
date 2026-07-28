using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hms.Registration.Data.Migrations
{
    /// <inheritdoc />
    public partial class WidenIdentityCheckForAgeMonths : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_identity",
                schema: "reg",
                table: "patient");

            migrationBuilder.AddCheckConstraint(
                name: "ck_identity",
                schema: "reg",
                table: "patient",
                sql: "dob is not null or age_years is not null or age_months is not null or unknown_identity");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_identity",
                schema: "reg",
                table: "patient");

            migrationBuilder.AddCheckConstraint(
                name: "ck_identity",
                schema: "reg",
                table: "patient",
                sql: "dob is not null or age_years is not null or unknown_identity");
        }
    }
}

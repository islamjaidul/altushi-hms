using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Hms.Ipd.Data.Migrations
{
    /// <inheritdoc />
    public partial class ConsultantVisit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "consultant_visit",
                schema: "ipd",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    admission_id = table.Column<long>(type: "bigint", nullable: false),
                    doctor_id = table.Column<long>(type: "bigint", nullable: false),
                    on_date = table.Column<DateOnly>(type: "date", nullable: false),
                    note_id = table.Column<long>(type: "bigint", nullable: true),
                    charge_line_id = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_consultant_visit", x => x.id);
                    table.ForeignKey(
                        name: "fk_consultant_visit_admission_admission_id",
                        column: x => x.admission_id,
                        principalSchema: "ipd",
                        principalTable: "admission",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_consultant_visit_admission_id_doctor_id_on_date",
                schema: "ipd",
                table: "consultant_visit",
                columns: new[] { "admission_id", "doctor_id", "on_date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_consultant_visit_doctor_id_on_date",
                schema: "ipd",
                table: "consultant_visit",
                columns: new[] { "doctor_id", "on_date" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "consultant_visit",
                schema: "ipd");
        }
    }
}

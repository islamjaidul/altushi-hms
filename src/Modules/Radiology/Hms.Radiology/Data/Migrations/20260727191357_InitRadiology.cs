using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Hms.Radiology.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitRadiology : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "radiology");

            migrationBuilder.CreateTable(
                name: "modality",
                schema: "radiology",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    code = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_modality", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "modality_test",
                schema: "radiology",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    modality_id = table.Column<long>(type: "bigint", nullable: false),
                    test_catalog_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_modality_test", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "study",
                schema: "radiology",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    order_test_id = table.Column<long>(type: "bigint", nullable: false),
                    patient_id = table.Column<long>(type: "bigint", nullable: false),
                    modality_id = table.Column<long>(type: "bigint", nullable: false),
                    accession_no = table.Column<string>(type: "text", nullable: false),
                    state = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    performed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    performed_by = table.Column<long>(type: "bigint", nullable: true),
                    film_size = table.Column<string>(type: "text", nullable: true),
                    film_count = table.Column<int>(type: "integer", nullable: false),
                    technician_note = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_study", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_modality_branch_id_code",
                schema: "radiology",
                table: "modality",
                columns: new[] { "branch_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_modality_test_test_catalog_id",
                schema: "radiology",
                table: "modality_test",
                column: "test_catalog_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_study_accession_no",
                schema: "radiology",
                table: "study",
                column: "accession_no",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_study_modality_id_state",
                schema: "radiology",
                table: "study",
                columns: new[] { "modality_id", "state" });

            migrationBuilder.CreateIndex(
                name: "ix_study_order_test_id",
                schema: "radiology",
                table: "study",
                column: "order_test_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "modality",
                schema: "radiology");

            migrationBuilder.DropTable(
                name: "modality_test",
                schema: "radiology");

            migrationBuilder.DropTable(
                name: "study",
                schema: "radiology");
        }
    }
}

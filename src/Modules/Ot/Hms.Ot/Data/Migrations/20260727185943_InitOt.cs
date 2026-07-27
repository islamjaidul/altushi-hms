using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Hms.Ot.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitOt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "ot");

            migrationBuilder.CreateTable(
                name: "case_consumable",
                schema: "ot",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    case_id = table.Column<long>(type: "bigint", nullable: false),
                    product_id = table.Column<long>(type: "bigint", nullable: false),
                    product_name = table.Column<string>(type: "text", nullable: false),
                    qty = table.Column<int>(type: "integer", nullable: false),
                    batch_no = table.Column<string>(type: "text", nullable: false),
                    unit_price = table.Column<long>(type: "bigint", nullable: false),
                    charge_line_id = table.Column<long>(type: "bigint", nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    issued_by = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_case_consumable", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "case_team",
                schema: "ot",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    case_id = table.Column<long>(type: "bigint", nullable: false),
                    role = table.Column<string>(type: "text", nullable: false),
                    person_id = table.Column<long>(type: "bigint", nullable: false),
                    person_name = table.Column<string>(type: "text", nullable: false),
                    fee_service_id = table.Column<long>(type: "bigint", nullable: true),
                    amount_posted = table.Column<long>(type: "bigint", nullable: false),
                    charge_line_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_case_team", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "ot_case",
                schema: "ot",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    case_no = table.Column<string>(type: "text", nullable: false),
                    patient_id = table.Column<long>(type: "bigint", nullable: false),
                    folio_id = table.Column<long>(type: "bigint", nullable: true),
                    encounter_id = table.Column<long>(type: "bigint", nullable: true),
                    theatre_id = table.Column<long>(type: "bigint", nullable: false),
                    operation_service_id = table.Column<long>(type: "bigint", nullable: false),
                    operation_name = table.Column<string>(type: "text", nullable: false),
                    scheduled_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    scheduled_to = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    state = table.Column<string>(type: "text", nullable: false),
                    anaesthesia_type = table.Column<string>(type: "text", nullable: true),
                    findings = table.Column<string>(type: "text", nullable: true),
                    procedure_performed = table.Column<string>(type: "text", nullable: true),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancel_reason = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_by = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ot_case", x => x.id);
                    table.CheckConstraint("ck_case_parent", "num_nonnulls(folio_id, encounter_id) = 1");
                    table.CheckConstraint("ck_case_window", "scheduled_to > scheduled_from");
                });

            migrationBuilder.CreateTable(
                name: "theatre",
                schema: "ot",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_theatre", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_case_consumable_case_id",
                schema: "ot",
                table: "case_consumable",
                column: "case_id");

            migrationBuilder.CreateIndex(
                name: "ix_case_team_case_id_role_person_id",
                schema: "ot",
                table: "case_team",
                columns: new[] { "case_id", "role", "person_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ot_case_case_no",
                schema: "ot",
                table: "ot_case",
                column: "case_no",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ot_case_patient_id",
                schema: "ot",
                table: "ot_case",
                column: "patient_id");

            migrationBuilder.CreateIndex(
                name: "ix_ot_case_state",
                schema: "ot",
                table: "ot_case",
                column: "state");

            migrationBuilder.CreateIndex(
                name: "ix_ot_case_theatre_id_scheduled_from",
                schema: "ot",
                table: "ot_case",
                columns: new[] { "theatre_id", "scheduled_from" });

            migrationBuilder.CreateIndex(
                name: "ix_theatre_branch_id_name",
                schema: "ot",
                table: "theatre",
                columns: new[] { "branch_id", "name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "case_consumable",
                schema: "ot");

            migrationBuilder.DropTable(
                name: "case_team",
                schema: "ot");

            migrationBuilder.DropTable(
                name: "ot_case",
                schema: "ot");

            migrationBuilder.DropTable(
                name: "theatre",
                schema: "ot");
        }
    }
}

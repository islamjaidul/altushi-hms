using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Hms.Emr.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitEmr : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "emr");

            migrationBuilder.CreateTable(
                name: "favourite",
                schema: "emr",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    doctor_id = table.Column<long>(type: "bigint", nullable: false),
                    product_id = table.Column<long>(type: "bigint", nullable: false),
                    drug_name = table.Column<string>(type: "text", nullable: false),
                    dose = table.Column<string>(type: "text", nullable: true),
                    frequency = table.Column<string>(type: "text", nullable: true),
                    duration = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_favourite", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "glucose_reading",
                schema: "emr",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    admission_id = table.Column<long>(type: "bigint", nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    glucose_tenths = table.Column<short>(type: "smallint", nullable: false),
                    timing = table.Column<string>(type: "text", nullable: true),
                    insulin_units = table.Column<short>(type: "smallint", nullable: true),
                    insulin_route = table.Column<string>(type: "text", nullable: true),
                    recorded_by = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_glucose_reading", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "mar_dose",
                schema: "emr",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    admission_id = table.Column<long>(type: "bigint", nullable: false),
                    drug_name = table.Column<string>(type: "text", nullable: false),
                    dose = table.Column<string>(type: "text", nullable: true),
                    route = table.Column<string>(type: "text", nullable: true),
                    scheduled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    state = table.Column<string>(type: "text", nullable: false),
                    state_reason = table.Column<string>(type: "text", nullable: true),
                    administered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    administered_by = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mar_dose", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "note",
                schema: "emr",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    patient_id = table.Column<long>(type: "bigint", nullable: false),
                    encounter_id = table.Column<long>(type: "bigint", nullable: true),
                    admission_id = table.Column<long>(type: "bigint", nullable: true),
                    doctor_id = table.Column<long>(type: "bigint", nullable: false),
                    complaint = table.Column<string>(type: "text", nullable: true),
                    on_examination = table.Column<string>(type: "text", nullable: true),
                    diagnosis = table.Column<string>(type: "text", nullable: true),
                    advice = table.Column<string>(type: "text", nullable: true),
                    follow_up_on = table.Column<DateOnly>(type: "date", nullable: true),
                    state = table.Column<string>(type: "text", nullable: false),
                    supersedes_id = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false),
                    finalised_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    finalised_by = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_note", x => x.id);
                    table.CheckConstraint("ck_note_parent", "num_nonnulls(encounter_id, admission_id) = 1");
                });

            migrationBuilder.CreateTable(
                name: "note_drug",
                schema: "emr",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    note_id = table.Column<long>(type: "bigint", nullable: false),
                    product_id = table.Column<long>(type: "bigint", nullable: true),
                    drug_name = table.Column<string>(type: "text", nullable: false),
                    dose = table.Column<string>(type: "text", nullable: true),
                    frequency = table.Column<string>(type: "text", nullable: true),
                    duration = table.Column<string>(type: "text", nullable: true),
                    instruction = table.Column<string>(type: "text", nullable: true),
                    ordinal = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_note_drug", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "receive_note",
                schema: "emr",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    admission_id = table.Column<long>(type: "bigint", nullable: false),
                    received_from = table.Column<string>(type: "text", nullable: true),
                    condition = table.Column<string>(type: "text", nullable: true),
                    belongings = table.Column<string>(type: "text", nullable: true),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    received_by = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_receive_note", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "template",
                schema: "emr",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    doctor_id = table.Column<long>(type: "bigint", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    complaint = table.Column<string>(type: "text", nullable: true),
                    on_examination = table.Column<string>(type: "text", nullable: true),
                    diagnosis = table.Column<string>(type: "text", nullable: true),
                    advice = table.Column<string>(type: "text", nullable: true),
                    drugs = table.Column<string>(type: "jsonb", nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_template", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "vitals",
                schema: "emr",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    patient_id = table.Column<long>(type: "bigint", nullable: false),
                    encounter_id = table.Column<long>(type: "bigint", nullable: true),
                    admission_id = table.Column<long>(type: "bigint", nullable: true),
                    systolic = table.Column<short>(type: "smallint", nullable: true),
                    diastolic = table.Column<short>(type: "smallint", nullable: true),
                    pulse = table.Column<short>(type: "smallint", nullable: true),
                    temperature_tenths_c = table.Column<short>(type: "smallint", nullable: true),
                    weight_tenths_kg = table.Column<short>(type: "smallint", nullable: true),
                    sp_o2 = table.Column<short>(type: "smallint", nullable: true),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_by = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_vitals", x => x.id);
                    table.CheckConstraint("ck_vitals_parent", "num_nonnulls(encounter_id, admission_id) = 1");
                });

            migrationBuilder.CreateIndex(
                name: "ix_favourite_doctor_id_product_id",
                schema: "emr",
                table: "favourite",
                columns: new[] { "doctor_id", "product_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_glucose_reading_admission_id_at",
                schema: "emr",
                table: "glucose_reading",
                columns: new[] { "admission_id", "at" });

            migrationBuilder.CreateIndex(
                name: "ix_mar_dose_admission_id_scheduled_at",
                schema: "emr",
                table: "mar_dose",
                columns: new[] { "admission_id", "scheduled_at" });

            migrationBuilder.CreateIndex(
                name: "ix_mar_dose_state",
                schema: "emr",
                table: "mar_dose",
                column: "state");

            migrationBuilder.CreateIndex(
                name: "ix_note_admission_id",
                schema: "emr",
                table: "note",
                column: "admission_id");

            migrationBuilder.CreateIndex(
                name: "ix_note_encounter_id",
                schema: "emr",
                table: "note",
                column: "encounter_id");

            migrationBuilder.CreateIndex(
                name: "ix_note_patient_id",
                schema: "emr",
                table: "note",
                column: "patient_id");

            migrationBuilder.CreateIndex(
                name: "ix_note_state",
                schema: "emr",
                table: "note",
                column: "state");

            migrationBuilder.CreateIndex(
                name: "ix_note_supersedes_id",
                schema: "emr",
                table: "note",
                column: "supersedes_id",
                unique: true,
                filter: "supersedes_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_note_drug_note_id",
                schema: "emr",
                table: "note_drug",
                column: "note_id");

            migrationBuilder.CreateIndex(
                name: "ix_receive_note_admission_id",
                schema: "emr",
                table: "receive_note",
                column: "admission_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_template_doctor_id_name",
                schema: "emr",
                table: "template",
                columns: new[] { "doctor_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_vitals_encounter_id",
                schema: "emr",
                table: "vitals",
                column: "encounter_id");

            migrationBuilder.CreateIndex(
                name: "ix_vitals_patient_id",
                schema: "emr",
                table: "vitals",
                column: "patient_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "favourite",
                schema: "emr");

            migrationBuilder.DropTable(
                name: "glucose_reading",
                schema: "emr");

            migrationBuilder.DropTable(
                name: "mar_dose",
                schema: "emr");

            migrationBuilder.DropTable(
                name: "note",
                schema: "emr");

            migrationBuilder.DropTable(
                name: "note_drug",
                schema: "emr");

            migrationBuilder.DropTable(
                name: "receive_note",
                schema: "emr");

            migrationBuilder.DropTable(
                name: "template",
                schema: "emr");

            migrationBuilder.DropTable(
                name: "vitals",
                schema: "emr");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Hms.Emr.Data.Migrations
{
    /// <inheritdoc />
    public partial class NursingStation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "note_drug_id",
                schema: "emr",
                table: "mar_dose",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "care_task",
                schema: "emr",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    admission_id = table.Column<long>(type: "bigint", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    details = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    due_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    state = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    state_reason = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_by = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_care_task", x => x.id);
                    table.CheckConstraint("ck_care_task_cancelled", "state <> 'cancelled' OR state_reason IS NOT NULL");
                    table.CheckConstraint("ck_care_task_done", "state <> 'done' OR (completed_at IS NOT NULL AND completed_by IS NOT NULL)");
                    table.CheckConstraint("ck_care_task_state", "state IN ('open','done','cancelled')");
                });

            migrationBuilder.CreateIndex(
                name: "ix_mar_dose_note_drug_id_scheduled_at",
                schema: "emr",
                table: "mar_dose",
                columns: new[] { "note_drug_id", "scheduled_at" },
                unique: true,
                filter: "note_drug_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_care_task_admission_id_state",
                schema: "emr",
                table: "care_task",
                columns: new[] { "admission_id", "state" });

            migrationBuilder.CreateIndex(
                name: "ix_care_task_state",
                schema: "emr",
                table: "care_task",
                column: "state");

            // mar_dose pre-exists this migration, so the FK goes on NOT VALID — new writes are
            // checked, old rows are not re-scanned on a live box (spec 0039 posture).
            migrationBuilder.Sql(
                "ALTER TABLE emr.mar_dose ADD CONSTRAINT fk_mar_dose_note_drug_note_drug_id "
                + "FOREIGN KEY (note_drug_id) REFERENCES emr.note_drug (id) NOT VALID;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_mar_dose_note_drug_note_drug_id",
                schema: "emr",
                table: "mar_dose");

            migrationBuilder.DropTable(
                name: "care_task",
                schema: "emr");

            migrationBuilder.DropIndex(
                name: "ix_mar_dose_note_drug_id_scheduled_at",
                schema: "emr",
                table: "mar_dose");

            migrationBuilder.DropColumn(
                name: "note_drug_id",
                schema: "emr",
                table: "mar_dose");
        }
    }
}

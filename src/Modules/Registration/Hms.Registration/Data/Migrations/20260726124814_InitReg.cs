using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Hms.Registration.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitReg : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "reg");

            migrationBuilder.CreateTable(
                name: "patient",
                schema: "reg",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    uhid = table.Column<string>(type: "text", nullable: false),
                    full_name = table.Column<string>(type: "text", nullable: false),
                    sex = table.Column<char>(type: "char(1)", nullable: false),
                    dob = table.Column<DateOnly>(type: "date", nullable: true),
                    age_years = table.Column<short>(type: "smallint", nullable: true),
                    age_months = table.Column<short>(type: "smallint", nullable: true),
                    age_estimated = table.Column<bool>(type: "boolean", nullable: false),
                    age_as_of = table.Column<DateOnly>(type: "date", nullable: true),
                    phone = table.Column<string>(type: "text", nullable: true),
                    guardian = table.Column<string>(type: "text", nullable: true),
                    area = table.Column<string>(type: "text", nullable: true),
                    address = table.Column<string>(type: "text", nullable: true),
                    nid = table.Column<string>(type: "text", nullable: true),
                    blood_group = table.Column<string>(type: "text", nullable: true),
                    patient_type = table.Column<string>(type: "text", nullable: false),
                    unknown_identity = table.Column<bool>(type: "boolean", nullable: false),
                    provisional = table.Column<bool>(type: "boolean", nullable: false),
                    merged_into = table.Column<long>(type: "bigint", nullable: true),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_patient", x => x.id);
                    table.CheckConstraint("ck_identity", "dob is not null or age_years is not null or unknown_identity");
                });

            migrationBuilder.CreateTable(
                name: "patient_merge",
                schema: "reg",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    survivor_id = table.Column<long>(type: "bigint", nullable: false),
                    merged_id = table.Column<long>(type: "bigint", nullable: false),
                    approval_id = table.Column<long>(type: "bigint", nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    by = table.Column<long>(type: "bigint", nullable: false),
                    repointed = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_patient_merge", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_patient_phone",
                schema: "reg",
                table: "patient",
                column: "phone");

            migrationBuilder.CreateIndex(
                name: "ix_patient_uhid",
                schema: "reg",
                table: "patient",
                column: "uhid",
                unique: true);

            // Hand-written SQL (00 §2): search infrastructure + phonetic dup-warning column.
            // dmetaphone (fuzzystrmatch) is IMMUTABLE — required for the generated column;
            // proven by this migration applying in the integration fixture (spec 0005 flag 1).
            migrationBuilder.Sql("""
                CREATE EXTENSION IF NOT EXISTS pg_trgm;
                CREATE EXTENSION IF NOT EXISTS fuzzystrmatch;
                ALTER TABLE reg.patient
                  ADD COLUMN name_phonetic text GENERATED ALWAYS AS (dmetaphone(full_name)) STORED;
                CREATE INDEX ix_patient_name_trgm ON reg.patient USING gin (full_name gin_trgm_ops);
                CREATE INDEX ix_patient_phonetic ON reg.patient (name_phonetic);
                CREATE INDEX ix_patient_live ON reg.patient (branch_id, uhid) WHERE merged_into IS NULL;
                """);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "patient",
                schema: "reg");

            migrationBuilder.DropTable(
                name: "patient_merge",
                schema: "reg");
        }
    }
}

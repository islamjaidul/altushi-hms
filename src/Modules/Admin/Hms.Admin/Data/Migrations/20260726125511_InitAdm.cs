using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Hms.Admin.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitAdm : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "adm");

            migrationBuilder.CreateTable(
                name: "rate_version",
                schema: "adm",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    scope = table.Column<string>(type: "text", nullable: false),
                    catalog_kind = table.Column<string>(type: "text", nullable: false),
                    catalog_id = table.Column<long>(type: "bigint", nullable: false),
                    price = table.Column<long>(type: "bigint", nullable: false),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: false),
                    valid_to = table.Column<DateOnly>(type: "date", nullable: true),
                    author_id = table.Column<long>(type: "bigint", nullable: false),
                    approval_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rate_version", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "service",
                schema: "adm",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    code = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    dept = table.Column<string>(type: "text", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    provisional = table.Column<bool>(type: "boolean", nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_service", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "test_catalog",
                schema: "adm",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    code = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    dept = table.Column<string>(type: "text", nullable: false),
                    sample_types = table.Column<string[]>(type: "text[]", nullable: false),
                    tat_minutes = table.Column<int>(type: "integer", nullable: false),
                    template = table.Column<string>(type: "jsonb", nullable: true),
                    provisional = table.Column<bool>(type: "boolean", nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_test_catalog", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_service_code",
                schema: "adm",
                table: "service",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_test_catalog_code",
                schema: "adm",
                table: "test_catalog",
                column: "code",
                unique: true);

            // 03 §5 / ADR-0015 #1: one active rate version per (item, scope, timepoint) — by constraint.
            migrationBuilder.Sql("""
                CREATE EXTENSION IF NOT EXISTS btree_gist;
                ALTER TABLE adm.rate_version ADD CONSTRAINT ex_rate_version_no_overlap
                  EXCLUDE USING gist (
                    catalog_kind WITH =, catalog_id WITH =, scope WITH =, branch_id WITH =,
                    daterange(valid_from, valid_to) WITH &&);
                """);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "rate_version",
                schema: "adm");

            migrationBuilder.DropTable(
                name: "service",
                schema: "adm");

            migrationBuilder.DropTable(
                name: "test_catalog",
                schema: "adm");
        }
    }
}

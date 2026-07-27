using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Hms.Ipd.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitIpd : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "ipd");

            migrationBuilder.CreateTable(
                name: "admission",
                schema: "ipd",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    admission_no = table.Column<string>(type: "text", nullable: false),
                    patient_id = table.Column<long>(type: "bigint", nullable: false),
                    doctor_id = table.Column<long>(type: "bigint", nullable: true),
                    source = table.Column<string>(type: "text", nullable: false),
                    provisional_dx = table.Column<string>(type: "text", nullable: true),
                    package_id = table.Column<long>(type: "bigint", nullable: true),
                    service_charge_pct = table.Column<short>(type: "smallint", nullable: false),
                    state = table.Column<string>(type: "text", nullable: false),
                    blocked_from = table.Column<string>(type: "text", nullable: true),
                    block_approval_id = table.Column<long>(type: "bigint", nullable: true),
                    clinical_summary = table.Column<string>(type: "text", nullable: true),
                    admitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    discharged_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_admission", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "admission_package",
                schema: "ipd",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    service_catalog_id = table.Column<long>(type: "bigint", nullable: false),
                    default_service_charge_pct = table.Column<short>(type: "smallint", nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_admission_package", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "bed",
                schema: "ipd",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    ward_id = table.Column<long>(type: "bigint", nullable: false),
                    code = table.Column<string>(type: "text", nullable: false),
                    tariff_service_id = table.Column<long>(type: "bigint", nullable: false),
                    state = table.Column<string>(type: "text", nullable: false),
                    state_reason = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bed", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "bed_day",
                schema: "ipd",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    admission_id = table.Column<long>(type: "bigint", nullable: false),
                    on_date = table.Column<DateOnly>(type: "date", nullable: false),
                    bed_id = table.Column<long>(type: "bigint", nullable: false),
                    charge_line_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bed_day", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "bed_stay",
                schema: "ipd",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    admission_id = table.Column<long>(type: "bigint", nullable: false),
                    bed_id = table.Column<long>(type: "bigint", nullable: false),
                    from_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    to_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bed_stay", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "certificate",
                schema: "ipd",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    admission_id = table.Column<long>(type: "bigint", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    cert_no = table.Column<string>(type: "text", nullable: false),
                    body = table.Column<string>(type: "jsonb", nullable: false),
                    issued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    issued_by = table.Column<long>(type: "bigint", nullable: false),
                    print_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_certificate", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "folio",
                schema: "ipd",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    admission_id = table.Column<long>(type: "bigint", nullable: false),
                    patient_id = table.Column<long>(type: "bigint", nullable: false),
                    state = table.Column<string>(type: "text", nullable: false),
                    advance_applied = table.Column<long>(type: "bigint", nullable: false),
                    settlement_invoice_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_folio", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "indent",
                schema: "ipd",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    admission_id = table.Column<long>(type: "bigint", nullable: false),
                    folio_id = table.Column<long>(type: "bigint", nullable: false),
                    state = table.Column<string>(type: "text", nullable: false),
                    note = table.Column<string>(type: "text", nullable: true),
                    requested_by = table.Column<long>(type: "bigint", nullable: false),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    issued_by = table.Column<long>(type: "bigint", nullable: true),
                    issued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_indent", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "indent_item",
                schema: "ipd",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    indent_id = table.Column<long>(type: "bigint", nullable: false),
                    product_id = table.Column<long>(type: "bigint", nullable: false),
                    qty_requested = table.Column<int>(type: "integer", nullable: false),
                    qty_issued = table.Column<int>(type: "integer", nullable: false),
                    qty_returned = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_indent_item", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "ward",
                schema: "ipd",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    @class = table.Column<string>(name: "class", type: "text", nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ward", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_admission_admission_no",
                schema: "ipd",
                table: "admission",
                column: "admission_no",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_admission_patient_id",
                schema: "ipd",
                table: "admission",
                column: "patient_id");

            migrationBuilder.CreateIndex(
                name: "ix_admission_state",
                schema: "ipd",
                table: "admission",
                column: "state");

            migrationBuilder.CreateIndex(
                name: "ix_bed_ward_id_code",
                schema: "ipd",
                table: "bed",
                columns: new[] { "ward_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_bed_day_admission_id_on_date",
                schema: "ipd",
                table: "bed_day",
                columns: new[] { "admission_id", "on_date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_bed_stay_admission_id",
                schema: "ipd",
                table: "bed_stay",
                column: "admission_id");

            migrationBuilder.CreateIndex(
                name: "ix_bed_stay_bed_id",
                schema: "ipd",
                table: "bed_stay",
                column: "bed_id");

            migrationBuilder.CreateIndex(
                name: "ix_certificate_cert_no",
                schema: "ipd",
                table: "certificate",
                column: "cert_no",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_folio_admission_id",
                schema: "ipd",
                table: "folio",
                column: "admission_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_indent_state",
                schema: "ipd",
                table: "indent",
                column: "state");

            migrationBuilder.CreateIndex(
                name: "ix_indent_item_indent_id",
                schema: "ipd",
                table: "indent_item",
                column: "indent_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "admission",
                schema: "ipd");

            migrationBuilder.DropTable(
                name: "admission_package",
                schema: "ipd");

            migrationBuilder.DropTable(
                name: "bed",
                schema: "ipd");

            migrationBuilder.DropTable(
                name: "bed_day",
                schema: "ipd");

            migrationBuilder.DropTable(
                name: "bed_stay",
                schema: "ipd");

            migrationBuilder.DropTable(
                name: "certificate",
                schema: "ipd");

            migrationBuilder.DropTable(
                name: "folio",
                schema: "ipd");

            migrationBuilder.DropTable(
                name: "indent",
                schema: "ipd");

            migrationBuilder.DropTable(
                name: "indent_item",
                schema: "ipd");

            migrationBuilder.DropTable(
                name: "ward",
                schema: "ipd");
        }
    }
}

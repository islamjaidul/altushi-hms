using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Hms.Diagnostics.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitDiag : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "diag");

            migrationBuilder.CreateTable(
                name: "delivery_log",
                schema: "diag",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    test_order_id = table.Column<long>(type: "bigint", nullable: false),
                    report_version = table.Column<int>(type: "integer", nullable: false),
                    delivered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    collector_note = table.Column<string>(type: "text", nullable: true),
                    delivered_by = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_delivery_log", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "order_test",
                schema: "diag",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    test_order_id = table.Column<long>(type: "bigint", nullable: false),
                    test_catalog_id = table.Column<long>(type: "bigint", nullable: false),
                    charge_line_id = table.Column<long>(type: "bigint", nullable: false),
                    state = table.Column<string>(type: "text", nullable: false),
                    refunded = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order_test", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "test_order",
                schema: "diag",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    patient_id = table.Column<long>(type: "bigint", nullable: false),
                    encounter_id = table.Column<long>(type: "bigint", nullable: false),
                    ordering_doctor_id = table.Column<long>(type: "bigint", nullable: true),
                    referrer_id = table.Column<long>(type: "bigint", nullable: true),
                    state = table.Column<string>(type: "text", nullable: false),
                    promised_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    invoice_id = table.Column<long>(type: "bigint", nullable: true),
                    cancel_approval_id = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_test_order", x => x.id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "delivery_log",
                schema: "diag");

            migrationBuilder.DropTable(
                name: "order_test",
                schema: "diag");

            migrationBuilder.DropTable(
                name: "test_order",
                schema: "diag");
        }
    }
}

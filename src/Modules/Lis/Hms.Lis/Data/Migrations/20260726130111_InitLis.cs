using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Hms.Lis.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitLis : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "lis");

            migrationBuilder.CreateTable(
                name: "label_print",
                schema: "lis",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    sample_id = table.Column<long>(type: "bigint", nullable: false),
                    printed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    printed_by = table.Column<long>(type: "bigint", nullable: false),
                    reprint = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_label_print", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "result",
                schema: "lis",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    order_test_id = table.Column<long>(type: "bigint", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    values = table.Column<string>(type: "jsonb", nullable: false),
                    narrative = table.Column<string>(type: "text", nullable: true),
                    entered_by = table.Column<long>(type: "bigint", nullable: false),
                    entered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    verified_by = table.Column<long>(type: "bigint", nullable: true),
                    verified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    verifier_role = table.Column<string>(type: "text", nullable: true),
                    esign_hash = table.Column<string>(type: "text", nullable: true),
                    signature_image_ref = table.Column<string>(type: "text", nullable: true),
                    amend_approval_id = table.Column<long>(type: "bigint", nullable: true),
                    supersedes_version = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_result", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sample",
                schema: "lis",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    barcode = table.Column<string>(type: "text", nullable: false),
                    sample_type = table.Column<string>(type: "text", nullable: false),
                    state = table.Column<string>(type: "text", nullable: false),
                    collected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    collected_by = table.Column<long>(type: "bigint", nullable: true),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    received_by = table.Column<long>(type: "bigint", nullable: true),
                    rejected_reason = table.Column<string>(type: "text", nullable: true),
                    recollection_of = table.Column<long>(type: "bigint", nullable: true),
                    disposal_note = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sample", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sample_test",
                schema: "lis",
                columns: table => new
                {
                    sample_id = table.Column<long>(type: "bigint", nullable: false),
                    order_test_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sample_test", x => new { x.sample_id, x.order_test_id });
                });

            migrationBuilder.CreateIndex(
                name: "ix_result_order_test_id_version",
                schema: "lis",
                table: "result",
                columns: new[] { "order_test_id", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sample_barcode",
                schema: "lis",
                table: "sample",
                column: "barcode",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "label_print",
                schema: "lis");

            migrationBuilder.DropTable(
                name: "result",
                schema: "lis");

            migrationBuilder.DropTable(
                name: "sample",
                schema: "lis");

            migrationBuilder.DropTable(
                name: "sample_test",
                schema: "lis");
        }
    }
}

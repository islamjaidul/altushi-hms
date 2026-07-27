using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Hms.Admin.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddReferrerAndReportingConsultant : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "referrer",
                schema: "adm",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    code = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    area = table.Column<string>(type: "text", nullable: true),
                    phone = table.Column<string>(type: "text", nullable: true),
                    commission_percent = table.Column<short>(type: "smallint", nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_referrer", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "reporting_consultant",
                schema: "adm",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "text", nullable: false),
                    degrees = table.Column<string>(type: "text", nullable: false),
                    bmdc_no = table.Column<string>(type: "text", nullable: true),
                    departments = table.Column<string[]>(type: "text[]", nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reporting_consultant", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_referrer_code",
                schema: "adm",
                table: "referrer",
                column: "code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "referrer",
                schema: "adm");

            migrationBuilder.DropTable(
                name: "reporting_consultant",
                schema: "adm");
        }
    }
}

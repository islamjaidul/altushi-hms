using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Hms.Pharmacy.Data.Migrations
{
    /// <inheritdoc />
    public partial class IssueAllocation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "issue_allocation",
                schema: "pharm",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    indent_id = table.Column<long>(type: "bigint", nullable: false),
                    charge_line_id = table.Column<long>(type: "bigint", nullable: false),
                    batch_id = table.Column<long>(type: "bigint", nullable: false),
                    product_id = table.Column<long>(type: "bigint", nullable: false),
                    qty = table.Column<int>(type: "integer", nullable: false),
                    unit_mrp = table.Column<long>(type: "bigint", nullable: false),
                    unit_cost = table.Column<long>(type: "bigint", nullable: false),
                    returned_qty = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_issue_allocation", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_issue_allocation_indent_id",
                schema: "pharm",
                table: "issue_allocation",
                column: "indent_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "issue_allocation",
                schema: "pharm");
        }
    }
}

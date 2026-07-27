using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hms.Diagnostics.Data.Migrations
{
    /// <inheritdoc />
    public partial class FolioOrderSeam : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<long>(
                name: "encounter_id",
                schema: "diag",
                table: "test_order",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AddColumn<long>(
                name: "folio_id",
                schema: "diag",
                table: "test_order",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_test_order_parent",
                schema: "diag",
                table: "test_order",
                sql: "num_nonnulls(encounter_id, folio_id) = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_test_order_parent",
                schema: "diag",
                table: "test_order");

            migrationBuilder.DropColumn(
                name: "folio_id",
                schema: "diag",
                table: "test_order");

            migrationBuilder.AlterColumn<long>(
                name: "encounter_id",
                schema: "diag",
                table: "test_order",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);
        }
    }
}

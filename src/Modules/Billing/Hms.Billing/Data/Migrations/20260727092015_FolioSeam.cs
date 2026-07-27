using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hms.Billing.Data.Migrations
{
    /// <inheritdoc />
    public partial class FolioSeam : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<long>(
                name: "invoice_id",
                schema: "bill",
                table: "receipt",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AddColumn<long>(
                name: "folio_id",
                schema: "bill",
                table: "receipt",
                type: "bigint",
                nullable: true);

            migrationBuilder.AlterColumn<long>(
                name: "encounter_id",
                schema: "bill",
                table: "invoice",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AddColumn<long>(
                name: "folio_id",
                schema: "bill",
                table: "invoice",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "advances_taken",
                schema: "bill",
                table: "day_close_summary",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateIndex(
                name: "ix_receipt_folio_id",
                schema: "bill",
                table: "receipt",
                column: "folio_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_receipt_parent",
                schema: "bill",
                table: "receipt",
                sql: "num_nonnulls(invoice_id, folio_id) = 1");

            migrationBuilder.AddCheckConstraint(
                name: "ck_invoice_parent",
                schema: "bill",
                table: "invoice",
                sql: "num_nonnulls(encounter_id, folio_id) = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_receipt_folio_id",
                schema: "bill",
                table: "receipt");

            migrationBuilder.DropCheckConstraint(
                name: "ck_receipt_parent",
                schema: "bill",
                table: "receipt");

            migrationBuilder.DropCheckConstraint(
                name: "ck_invoice_parent",
                schema: "bill",
                table: "invoice");

            migrationBuilder.DropColumn(
                name: "folio_id",
                schema: "bill",
                table: "receipt");

            migrationBuilder.DropColumn(
                name: "folio_id",
                schema: "bill",
                table: "invoice");

            migrationBuilder.DropColumn(
                name: "advances_taken",
                schema: "bill",
                table: "day_close_summary");

            migrationBuilder.AlterColumn<long>(
                name: "invoice_id",
                schema: "bill",
                table: "receipt",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.AlterColumn<long>(
                name: "encounter_id",
                schema: "bill",
                table: "invoice",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);
        }
    }
}

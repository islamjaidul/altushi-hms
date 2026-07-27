using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hms.Billing.Data.Migrations
{
    /// <inheritdoc />
    public partial class SubmissionToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "submission_token",
                schema: "bill",
                table: "invoice",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_invoice_submission_token",
                schema: "bill",
                table: "invoice",
                column: "submission_token",
                unique: true,
                filter: "submission_token IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_invoice_submission_token",
                schema: "bill",
                table: "invoice");

            migrationBuilder.DropColumn(
                name: "submission_token",
                schema: "bill",
                table: "invoice");
        }
    }
}

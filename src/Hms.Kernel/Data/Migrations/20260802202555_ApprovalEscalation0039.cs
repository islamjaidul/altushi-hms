using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hms.Kernel.Data.Migrations
{
    /// <inheritdoc />
    public partial class ApprovalEscalation0039 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "escalated_at",
                schema: "kernel",
                table: "approval_request",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "escalated_tier",
                schema: "kernel",
                table: "approval_request",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "escalated_at",
                schema: "kernel",
                table: "approval_request");

            migrationBuilder.DropColumn(
                name: "escalated_tier",
                schema: "kernel",
                table: "approval_request");
        }
    }
}

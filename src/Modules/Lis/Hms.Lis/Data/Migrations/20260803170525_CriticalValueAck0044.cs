using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hms.Lis.Data.Migrations
{
    /// <inheritdoc />
    public partial class CriticalValueAck0044 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "critical_ack_at",
                schema: "lis",
                table: "result",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "critical_ack_by",
                schema: "lis",
                table: "result",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "critical_ack_note",
                schema: "lis",
                table: "result",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "critical_ack_at",
                schema: "lis",
                table: "result");

            migrationBuilder.DropColumn(
                name: "critical_ack_by",
                schema: "lis",
                table: "result");

            migrationBuilder.DropColumn(
                name: "critical_ack_note",
                schema: "lis",
                table: "result");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hms.Notifications.Data.Migrations
{
    /// <inheritdoc />
    public partial class SmsRetry0039 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "next_attempt_at",
                schema: "notif",
                table: "sms",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "retry_count",
                schema: "notif",
                table: "sms",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "ix_sms_state",
                schema: "notif",
                table: "sms",
                column: "state");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_sms_state",
                schema: "notif",
                table: "sms");

            migrationBuilder.DropColumn(
                name: "next_attempt_at",
                schema: "notif",
                table: "sms");

            migrationBuilder.DropColumn(
                name: "retry_count",
                schema: "notif",
                table: "sms");
        }
    }
}

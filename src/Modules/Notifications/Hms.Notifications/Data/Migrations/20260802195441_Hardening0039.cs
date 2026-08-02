using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hms.Notifications.Data.Migrations
{
    /// <inheritdoc />
    public partial class Hardening0039 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE notif.sms ADD CONSTRAINT ck_sms_state_len "
                + "CHECK (length(state) <= 40) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE notif.sms ADD CONSTRAINT ck_sms_recipient_len "
                + "CHECK (length(recipient) <= 20) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE notif.sms ADD CONSTRAINT ck_sms_fail_reason_len "
                + "CHECK (length(fail_reason) <= 4000) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE notif.sms ADD CONSTRAINT ck_sms_event_len "
                + "CHECK (length(event) <= 40) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE notif.sms ADD CONSTRAINT ck_sms_body_len "
                + "CHECK (length(body) <= 4000) NOT VALID;");

            migrationBuilder.Sql("""
                ALTER TABLE notif.sms ADD CONSTRAINT ck_sms_state
                    CHECK (state IN ('queued','sent','delivered','failed','skipped_no_phone')) NOT VALID;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_sms_state",
                schema: "notif",
                table: "sms");

            migrationBuilder.AlterColumn<string>(
                name: "state",
                schema: "notif",
                table: "sms",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40);

            migrationBuilder.AlterColumn<string>(
                name: "recipient",
                schema: "notif",
                table: "sms",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "fail_reason",
                schema: "notif",
                table: "sms",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(4000)",
                oldMaxLength: 4000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "event",
                schema: "notif",
                table: "sms",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40);

            migrationBuilder.AlterColumn<string>(
                name: "body",
                schema: "notif",
                table: "sms",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(4000)",
                oldMaxLength: 4000);
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hms.Kernel.Data.Migrations
{
    /// <inheritdoc />
    public partial class Hardening0039 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE kernel.approval_request ADD CONSTRAINT ck_approval_request_reason_len "
                + "CHECK (length(reason) <= 4000) NOT VALID;");

            migrationBuilder.Sql("""
                ALTER TABLE kernel.approval_delegation ADD CONSTRAINT ck_delegation_window
                    CHECK (valid_to > valid_from) NOT VALID;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_delegation_window",
                schema: "kernel",
                table: "approval_delegation");

            migrationBuilder.AlterColumn<string>(
                name: "reason",
                schema: "kernel",
                table: "approval_request",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(4000)",
                oldMaxLength: 4000);
        }
    }
}

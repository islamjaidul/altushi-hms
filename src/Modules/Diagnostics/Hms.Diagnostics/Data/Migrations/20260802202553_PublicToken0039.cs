using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hms.Diagnostics.Data.Migrations
{
    /// <inheritdoc />
    public partial class PublicToken0039 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "public_token",
                schema: "diag",
                table: "test_order",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            // AUD-PHI-01: every pre-existing order gets its own random token before the unique
            // index goes on — two empty strings would collide, and an empty token must never be
            // a valid lookup key. gen_random_uuid() is core PostgreSQL (13+); md5() folds it to
            // the same 32-hex shape PublicToken.New() mints.
            migrationBuilder.Sql(
                "UPDATE diag.test_order SET public_token = md5(gen_random_uuid()::text || id::text) "
                + "WHERE public_token = '' OR public_token IS NULL;");

            migrationBuilder.CreateIndex(
                name: "ix_test_order_public_token",
                schema: "diag",
                table: "test_order",
                column: "public_token",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_test_order_public_token",
                schema: "diag",
                table: "test_order");

            migrationBuilder.DropColumn(
                name: "public_token",
                schema: "diag",
                table: "test_order");
        }
    }
}

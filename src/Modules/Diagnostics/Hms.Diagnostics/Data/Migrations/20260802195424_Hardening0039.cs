using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hms.Diagnostics.Data.Migrations
{
    /// <inheritdoc />
    public partial class Hardening0039 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE diag.test_order ADD CONSTRAINT ck_test_order_state_len "
                + "CHECK (length(state) <= 40) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE diag.order_test ADD CONSTRAINT ck_order_test_state_len "
                + "CHECK (length(state) <= 40) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE diag.delivery_log ADD CONSTRAINT ck_delivery_log_collector_note_len "
                + "CHECK (length(collector_note) <= 4000) NOT VALID;");

            migrationBuilder.CreateIndex(
                name: "ix_test_order_encounter_id",
                schema: "diag",
                table: "test_order",
                column: "encounter_id");

            migrationBuilder.CreateIndex(
                name: "ix_test_order_invoice_id_state",
                schema: "diag",
                table: "test_order",
                columns: new[] { "invoice_id", "state" });

            migrationBuilder.CreateIndex(
                name: "ix_test_order_patient_id",
                schema: "diag",
                table: "test_order",
                column: "patient_id");

            migrationBuilder.CreateIndex(
                name: "ix_test_order_state",
                schema: "diag",
                table: "test_order",
                column: "state");

            migrationBuilder.Sql("""
                ALTER TABLE diag.test_order ADD CONSTRAINT ck_test_order_state
                    CHECK (state IN ('ordered','invoiced','in_progress','reported','delivered','cancelled')) NOT VALID;
                """);

            migrationBuilder.CreateIndex(
                name: "ix_order_test_test_order_id_test_catalog_id",
                schema: "diag",
                table: "order_test",
                columns: new[] { "test_order_id", "test_catalog_id" });

            migrationBuilder.Sql("""
                ALTER TABLE diag.order_test ADD CONSTRAINT ck_order_test_state
                    CHECK (state IN ('ordered','cancelled')) NOT VALID;
                """);

            migrationBuilder.CreateIndex(
                name: "ix_delivery_log_test_order_id",
                schema: "diag",
                table: "delivery_log",
                column: "test_order_id");

            migrationBuilder.Sql(
                "ALTER TABLE diag.delivery_log ADD CONSTRAINT fk_delivery_log_test_order_test_order_id "
                + "FOREIGN KEY (test_order_id) REFERENCES diag.test_order (id) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE diag.order_test ADD CONSTRAINT fk_order_test_test_order_test_order_id "
                + "FOREIGN KEY (test_order_id) REFERENCES diag.test_order (id) NOT VALID;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_delivery_log_test_order_test_order_id",
                schema: "diag",
                table: "delivery_log");

            migrationBuilder.DropForeignKey(
                name: "fk_order_test_test_order_test_order_id",
                schema: "diag",
                table: "order_test");

            migrationBuilder.DropIndex(
                name: "ix_test_order_encounter_id",
                schema: "diag",
                table: "test_order");

            migrationBuilder.DropIndex(
                name: "ix_test_order_invoice_id_state",
                schema: "diag",
                table: "test_order");

            migrationBuilder.DropIndex(
                name: "ix_test_order_patient_id",
                schema: "diag",
                table: "test_order");

            migrationBuilder.DropIndex(
                name: "ix_test_order_state",
                schema: "diag",
                table: "test_order");

            migrationBuilder.DropCheckConstraint(
                name: "ck_test_order_state",
                schema: "diag",
                table: "test_order");

            migrationBuilder.DropIndex(
                name: "ix_order_test_test_order_id_test_catalog_id",
                schema: "diag",
                table: "order_test");

            migrationBuilder.DropCheckConstraint(
                name: "ck_order_test_state",
                schema: "diag",
                table: "order_test");

            migrationBuilder.DropIndex(
                name: "ix_delivery_log_test_order_id",
                schema: "diag",
                table: "delivery_log");

            migrationBuilder.AlterColumn<string>(
                name: "state",
                schema: "diag",
                table: "test_order",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40);

            migrationBuilder.AlterColumn<string>(
                name: "state",
                schema: "diag",
                table: "order_test",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40);

            migrationBuilder.AlterColumn<string>(
                name: "collector_note",
                schema: "diag",
                table: "delivery_log",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(4000)",
                oldMaxLength: 4000,
                oldNullable: true);
        }
    }
}

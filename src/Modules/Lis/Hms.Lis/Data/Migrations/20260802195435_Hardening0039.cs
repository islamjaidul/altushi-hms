using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hms.Lis.Data.Migrations
{
    /// <inheritdoc />
    public partial class Hardening0039 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE lis.sample ADD CONSTRAINT ck_sample_state_len "
                + "CHECK (length(state) <= 40) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE lis.sample ADD CONSTRAINT ck_sample_sample_type_len "
                + "CHECK (length(sample_type) <= 40) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE lis.sample ADD CONSTRAINT ck_sample_rejected_reason_len "
                + "CHECK (length(rejected_reason) <= 4000) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE lis.sample ADD CONSTRAINT ck_sample_disposal_note_len "
                + "CHECK (length(disposal_note) <= 4000) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE lis.sample ADD CONSTRAINT ck_sample_barcode_len "
                + "CHECK (length(barcode) <= 40) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE lis.result ADD CONSTRAINT ck_result_verifier_role_len "
                + "CHECK (length(verifier_role) <= 40) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE lis.result ADD CONSTRAINT ck_result_signature_image_ref_len "
                + "CHECK (length(signature_image_ref) <= 200) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE lis.result ADD CONSTRAINT ck_result_narrative_len "
                + "CHECK (length(narrative) <= 10000) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE lis.result ADD CONSTRAINT ck_result_esign_hash_len "
                + "CHECK (length(esign_hash) <= 200) NOT VALID;");


            migrationBuilder.CreateIndex(
                name: "ix_sample_test_order_test_id",
                schema: "lis",
                table: "sample_test",
                column: "order_test_id");

            migrationBuilder.CreateIndex(
                name: "ix_sample_recollection_of",
                schema: "lis",
                table: "sample",
                column: "recollection_of");

            migrationBuilder.Sql("""
                ALTER TABLE lis.sample ADD CONSTRAINT ck_sample_collected
                    CHECK (state = 'pending_collection' OR (collected_at IS NOT NULL AND collected_by IS NOT NULL)) NOT VALID;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE lis.sample ADD CONSTRAINT ck_sample_received
                    CHECK (state IN ('pending_collection','collected') OR (received_at IS NOT NULL AND received_by IS NOT NULL)) NOT VALID;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE lis.sample ADD CONSTRAINT ck_sample_rejected
                    CHECK (state <> 'rejected' OR rejected_reason IS NOT NULL) NOT VALID;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE lis.sample ADD CONSTRAINT ck_sample_state
                    CHECK (state IN ('pending_collection','collected','received','rejected','resulted','verified','report_ready','delivered')) NOT VALID;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE lis.result ADD CONSTRAINT ck_result_verified
                    CHECK (verified_at IS NULL OR (verified_by IS NOT NULL AND verifier_role IS NOT NULL AND esign_hash IS NOT NULL)) NOT VALID;
                """);

            migrationBuilder.CreateIndex(
                name: "ix_label_print_sample_id",
                schema: "lis",
                table: "label_print",
                column: "sample_id");

            migrationBuilder.Sql(
                "ALTER TABLE lis.label_print ADD CONSTRAINT fk_label_print_sample_sample_id "
                + "FOREIGN KEY (sample_id) REFERENCES lis.sample (id) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE lis.sample ADD CONSTRAINT fk_sample_sample_recollection_of "
                + "FOREIGN KEY (recollection_of) REFERENCES lis.sample (id) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE lis.sample_test ADD CONSTRAINT fk_sample_test_sample_sample_id "
                + "FOREIGN KEY (sample_id) REFERENCES lis.sample (id) NOT VALID;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_label_print_sample_sample_id",
                schema: "lis",
                table: "label_print");

            migrationBuilder.DropForeignKey(
                name: "fk_sample_sample_recollection_of",
                schema: "lis",
                table: "sample");

            migrationBuilder.DropForeignKey(
                name: "fk_sample_test_sample_sample_id",
                schema: "lis",
                table: "sample_test");

            migrationBuilder.DropIndex(
                name: "ix_sample_test_order_test_id",
                schema: "lis",
                table: "sample_test");

            migrationBuilder.DropIndex(
                name: "ix_sample_recollection_of",
                schema: "lis",
                table: "sample");

            migrationBuilder.DropCheckConstraint(
                name: "ck_sample_collected",
                schema: "lis",
                table: "sample");

            migrationBuilder.DropCheckConstraint(
                name: "ck_sample_received",
                schema: "lis",
                table: "sample");

            migrationBuilder.DropCheckConstraint(
                name: "ck_sample_rejected",
                schema: "lis",
                table: "sample");

            migrationBuilder.DropCheckConstraint(
                name: "ck_sample_state",
                schema: "lis",
                table: "sample");

            migrationBuilder.DropCheckConstraint(
                name: "ck_result_verified",
                schema: "lis",
                table: "result");

            migrationBuilder.DropIndex(
                name: "ix_label_print_sample_id",
                schema: "lis",
                table: "label_print");

            migrationBuilder.DropColumn(
                name: "xmin",
                schema: "lis",
                table: "result");

            migrationBuilder.AlterColumn<string>(
                name: "state",
                schema: "lis",
                table: "sample",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40);

            migrationBuilder.AlterColumn<string>(
                name: "sample_type",
                schema: "lis",
                table: "sample",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40);

            migrationBuilder.AlterColumn<string>(
                name: "rejected_reason",
                schema: "lis",
                table: "sample",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(4000)",
                oldMaxLength: 4000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "disposal_note",
                schema: "lis",
                table: "sample",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(4000)",
                oldMaxLength: 4000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "barcode",
                schema: "lis",
                table: "sample",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40);

            migrationBuilder.AlterColumn<string>(
                name: "verifier_role",
                schema: "lis",
                table: "result",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "signature_image_ref",
                schema: "lis",
                table: "result",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "narrative",
                schema: "lis",
                table: "result",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(10000)",
                oldMaxLength: 10000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "esign_hash",
                schema: "lis",
                table: "result",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);
        }
    }
}

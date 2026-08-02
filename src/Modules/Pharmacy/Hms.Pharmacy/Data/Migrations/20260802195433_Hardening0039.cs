using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hms.Pharmacy.Data.Migrations
{
    /// <inheritdoc />
    public partial class Hardening0039 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE pharm.transfer_batch ADD CONSTRAINT ck_transfer_batch_batch_no_len "
                + "CHECK (length(batch_no) <= 40) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE pharm.transfer ADD CONSTRAINT ck_transfer_state_len "
                + "CHECK (length(state) <= 40) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE pharm.supplier_ledger ADD CONSTRAINT ck_supplier_ledger_ref_table_len "
                + "CHECK (length(ref_table) <= 40) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE pharm.supplier_ledger ADD CONSTRAINT ck_supplier_ledger_note_len "
                + "CHECK (length(note) <= 4000) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE pharm.supplier_ledger ADD CONSTRAINT ck_supplier_ledger_kind_len "
                + "CHECK (length(kind) <= 40) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE pharm.supplier ADD CONSTRAINT ck_supplier_phone_len "
                + "CHECK (length(phone) <= 20) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE pharm.supplier ADD CONSTRAINT ck_supplier_name_len "
                + "CHECK (length(name) <= 200) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE pharm.stock_move ADD CONSTRAINT ck_stock_move_ref_table_len "
                + "CHECK (length(ref_table) <= 40) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE pharm.stock_move ADD CONSTRAINT ck_stock_move_reason_len "
                + "CHECK (length(reason) <= 4000) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE pharm.stock_move ADD CONSTRAINT ck_stock_move_kind_len "
                + "CHECK (length(kind) <= 40) NOT VALID;");


            migrationBuilder.Sql(
                "ALTER TABLE pharm.stock_audit ADD CONSTRAINT ck_stock_audit_state_len "
                + "CHECK (length(state) <= 40) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE pharm.purchase_order ADD CONSTRAINT ck_purchase_order_state_len "
                + "CHECK (length(state) <= 40) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE pharm.product ADD CONSTRAINT ck_product_unit_len "
                + "CHECK (length(unit) <= 40) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE pharm.product ADD CONSTRAINT ck_product_strength_len "
                + "CHECK (length(strength) <= 40) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE pharm.product ADD CONSTRAINT ck_product_generic_len "
                + "CHECK (length(generic) <= 200) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE pharm.product ADD CONSTRAINT ck_product_form_len "
                + "CHECK (length(form) <= 40) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE pharm.product ADD CONSTRAINT ck_product_brand_len "
                + "CHECK (length(brand) <= 200) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE pharm.outlet ADD CONSTRAINT ck_outlet_name_len "
                + "CHECK (length(name) <= 200) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE pharm.outlet ADD CONSTRAINT ck_outlet_kind_len "
                + "CHECK (length(kind) <= 40) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE pharm.company ADD CONSTRAINT ck_company_name_len "
                + "CHECK (length(name) <= 200) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE pharm.batch ADD CONSTRAINT ck_batch_state_reason_len "
                + "CHECK (length(state_reason) <= 4000) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE pharm.batch ADD CONSTRAINT ck_batch_state_len "
                + "CHECK (length(state) <= 40) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE pharm.batch ADD CONSTRAINT ck_batch_batch_no_len "
                + "CHECK (length(batch_no) <= 40) NOT VALID;");


            migrationBuilder.CreateIndex(
                name: "ix_transfer_line_transfer_id",
                schema: "pharm",
                table: "transfer_line",
                column: "transfer_id");

            migrationBuilder.Sql("""
                ALTER TABLE pharm.transfer_line ADD CONSTRAINT ck_transfer_line_qty
                    CHECK (sent_qty BETWEEN 0 AND requested_qty) NOT VALID;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE pharm.transfer ADD CONSTRAINT ck_transfer_received
                    CHECK (state <> 'received' OR (received_at IS NOT NULL AND received_by IS NOT NULL)) NOT VALID;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE pharm.transfer ADD CONSTRAINT ck_transfer_sent
                    CHECK (state <> 'sent' OR sent_at IS NOT NULL) NOT VALID;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE pharm.transfer ADD CONSTRAINT ck_transfer_state
                    CHECK (state IN ('indent','sent','received','cancelled')) NOT VALID;
                """);

            migrationBuilder.CreateIndex(
                name: "ix_stock_audit_line_stock_audit_id",
                schema: "pharm",
                table: "stock_audit_line",
                column: "stock_audit_id");

            migrationBuilder.Sql("""
                ALTER TABLE pharm.stock_audit_line ADD CONSTRAINT ck_stock_audit_line_counted
                    CHECK (counted_qty >= 0) NOT VALID;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE pharm.stock_audit ADD CONSTRAINT ck_stock_audit_state
                    CHECK (state IN ('count_started','variance_listed','approved','posted')) NOT VALID;
                """);

            migrationBuilder.CreateIndex(
                name: "ix_sale_allocation_batch_id",
                schema: "pharm",
                table: "sale_allocation",
                column: "batch_id");

            migrationBuilder.Sql("""
                ALTER TABLE pharm.sale_allocation ADD CONSTRAINT ck_sale_allocation_money
                    CHECK (unit_mrp >= 0 AND unit_cost >= 0) NOT VALID;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE pharm.sale_allocation ADD CONSTRAINT ck_sale_allocation_qty
                    CHECK (qty > 0 AND refunded_qty BETWEEN 0 AND qty) NOT VALID;
                """);

            migrationBuilder.CreateIndex(
                name: "ix_purchase_order_line_purchase_order_id",
                schema: "pharm",
                table: "purchase_order_line",
                column: "purchase_order_id");

            migrationBuilder.Sql("""
                ALTER TABLE pharm.purchase_order_line ADD CONSTRAINT ck_po_line_qty
                    CHECK (qty > 0 AND expected_cost >= 0 AND received_qty BETWEEN 0 AND qty) NOT VALID;
                """);

            migrationBuilder.CreateIndex(
                name: "ix_purchase_order_supplier_id",
                schema: "pharm",
                table: "purchase_order",
                column: "supplier_id");

            migrationBuilder.Sql("""
                ALTER TABLE pharm.purchase_order ADD CONSTRAINT ck_purchase_order_state
                    CHECK (state IN ('requested','approved','ordered','partially_received','received','closed','cancelled')) NOT VALID;
                """);

            migrationBuilder.CreateIndex(
                name: "ix_product_company_id",
                schema: "pharm",
                table: "product",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_issue_allocation_batch_id",
                schema: "pharm",
                table: "issue_allocation",
                column: "batch_id");

            migrationBuilder.Sql("""
                ALTER TABLE pharm.issue_allocation ADD CONSTRAINT ck_issue_allocation_qty
                    CHECK (returned_qty BETWEEN 0 AND qty) NOT VALID;
                """);

            migrationBuilder.CreateIndex(
                name: "ix_batch_product_id_state_expiry",
                schema: "pharm",
                table: "batch",
                columns: new[] { "product_id", "state", "expiry" });

            migrationBuilder.CreateIndex(
                name: "ix_batch_purchase_order_id",
                schema: "pharm",
                table: "batch",
                column: "purchase_order_id");

            migrationBuilder.Sql("""
                ALTER TABLE pharm.batch ADD CONSTRAINT ck_batch_money
                    CHECK (cost >= 0 AND mrp >= 0) NOT VALID;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE pharm.batch ADD CONSTRAINT ck_batch_state
                    CHECK (state IN ('instock','quarantined','returned','disposed')) NOT VALID;
                """);

            migrationBuilder.Sql(
                "ALTER TABLE pharm.batch ADD CONSTRAINT fk_batch_outlet_outlet_id "
                + "FOREIGN KEY (outlet_id) REFERENCES pharm.outlet (id) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE pharm.batch ADD CONSTRAINT fk_batch_product_product_id "
                + "FOREIGN KEY (product_id) REFERENCES pharm.product (id) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE pharm.batch ADD CONSTRAINT fk_batch_purchase_order_purchase_order_id "
                + "FOREIGN KEY (purchase_order_id) REFERENCES pharm.purchase_order (id) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE pharm.issue_allocation ADD CONSTRAINT fk_issue_allocation_batch_batch_id "
                + "FOREIGN KEY (batch_id) REFERENCES pharm.batch (id) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE pharm.product ADD CONSTRAINT fk_product_company_company_id "
                + "FOREIGN KEY (company_id) REFERENCES pharm.company (id) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE pharm.purchase_order ADD CONSTRAINT fk_purchase_order_supplier_supplier_id "
                + "FOREIGN KEY (supplier_id) REFERENCES pharm.supplier (id) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE pharm.purchase_order_line ADD CONSTRAINT fk_purchase_order_line_purchase_order_purchase_order_id "
                + "FOREIGN KEY (purchase_order_id) REFERENCES pharm.purchase_order (id) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE pharm.sale_allocation ADD CONSTRAINT fk_sale_allocation_batch_batch_id "
                + "FOREIGN KEY (batch_id) REFERENCES pharm.batch (id) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE pharm.stock_audit_line ADD CONSTRAINT fk_stock_audit_line_stock_audit_stock_audit_id "
                + "FOREIGN KEY (stock_audit_id) REFERENCES pharm.stock_audit (id) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE pharm.stock_move ADD CONSTRAINT fk_stock_move_batch_batch_id "
                + "FOREIGN KEY (batch_id) REFERENCES pharm.batch (id) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE pharm.stock_move ADD CONSTRAINT fk_stock_move_product_product_id "
                + "FOREIGN KEY (product_id) REFERENCES pharm.product (id) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE pharm.supplier_ledger ADD CONSTRAINT fk_supplier_ledger_supplier_supplier_id "
                + "FOREIGN KEY (supplier_id) REFERENCES pharm.supplier (id) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE pharm.transfer_line ADD CONSTRAINT fk_transfer_line_transfer_transfer_id "
                + "FOREIGN KEY (transfer_id) REFERENCES pharm.transfer (id) NOT VALID;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_batch_outlet_outlet_id",
                schema: "pharm",
                table: "batch");

            migrationBuilder.DropForeignKey(
                name: "fk_batch_product_product_id",
                schema: "pharm",
                table: "batch");

            migrationBuilder.DropForeignKey(
                name: "fk_batch_purchase_order_purchase_order_id",
                schema: "pharm",
                table: "batch");

            migrationBuilder.DropForeignKey(
                name: "fk_issue_allocation_batch_batch_id",
                schema: "pharm",
                table: "issue_allocation");

            migrationBuilder.DropForeignKey(
                name: "fk_product_company_company_id",
                schema: "pharm",
                table: "product");

            migrationBuilder.DropForeignKey(
                name: "fk_purchase_order_supplier_supplier_id",
                schema: "pharm",
                table: "purchase_order");

            migrationBuilder.DropForeignKey(
                name: "fk_purchase_order_line_purchase_order_purchase_order_id",
                schema: "pharm",
                table: "purchase_order_line");

            migrationBuilder.DropForeignKey(
                name: "fk_sale_allocation_batch_batch_id",
                schema: "pharm",
                table: "sale_allocation");

            migrationBuilder.DropForeignKey(
                name: "fk_stock_audit_line_stock_audit_stock_audit_id",
                schema: "pharm",
                table: "stock_audit_line");

            migrationBuilder.DropForeignKey(
                name: "fk_stock_move_batch_batch_id",
                schema: "pharm",
                table: "stock_move");

            migrationBuilder.DropForeignKey(
                name: "fk_stock_move_product_product_id",
                schema: "pharm",
                table: "stock_move");

            migrationBuilder.DropForeignKey(
                name: "fk_supplier_ledger_supplier_supplier_id",
                schema: "pharm",
                table: "supplier_ledger");

            migrationBuilder.DropForeignKey(
                name: "fk_transfer_line_transfer_transfer_id",
                schema: "pharm",
                table: "transfer_line");

            migrationBuilder.DropIndex(
                name: "ix_transfer_line_transfer_id",
                schema: "pharm",
                table: "transfer_line");

            migrationBuilder.DropCheckConstraint(
                name: "ck_transfer_line_qty",
                schema: "pharm",
                table: "transfer_line");

            migrationBuilder.DropCheckConstraint(
                name: "ck_transfer_received",
                schema: "pharm",
                table: "transfer");

            migrationBuilder.DropCheckConstraint(
                name: "ck_transfer_sent",
                schema: "pharm",
                table: "transfer");

            migrationBuilder.DropCheckConstraint(
                name: "ck_transfer_state",
                schema: "pharm",
                table: "transfer");

            migrationBuilder.DropIndex(
                name: "ix_stock_audit_line_stock_audit_id",
                schema: "pharm",
                table: "stock_audit_line");

            migrationBuilder.DropCheckConstraint(
                name: "ck_stock_audit_line_counted",
                schema: "pharm",
                table: "stock_audit_line");

            migrationBuilder.DropCheckConstraint(
                name: "ck_stock_audit_state",
                schema: "pharm",
                table: "stock_audit");

            migrationBuilder.DropIndex(
                name: "ix_sale_allocation_batch_id",
                schema: "pharm",
                table: "sale_allocation");

            migrationBuilder.DropCheckConstraint(
                name: "ck_sale_allocation_money",
                schema: "pharm",
                table: "sale_allocation");

            migrationBuilder.DropCheckConstraint(
                name: "ck_sale_allocation_qty",
                schema: "pharm",
                table: "sale_allocation");

            migrationBuilder.DropIndex(
                name: "ix_purchase_order_line_purchase_order_id",
                schema: "pharm",
                table: "purchase_order_line");

            migrationBuilder.DropCheckConstraint(
                name: "ck_po_line_qty",
                schema: "pharm",
                table: "purchase_order_line");

            migrationBuilder.DropIndex(
                name: "ix_purchase_order_supplier_id",
                schema: "pharm",
                table: "purchase_order");

            migrationBuilder.DropCheckConstraint(
                name: "ck_purchase_order_state",
                schema: "pharm",
                table: "purchase_order");

            migrationBuilder.DropIndex(
                name: "ix_product_company_id",
                schema: "pharm",
                table: "product");

            migrationBuilder.DropIndex(
                name: "ix_issue_allocation_batch_id",
                schema: "pharm",
                table: "issue_allocation");

            migrationBuilder.DropCheckConstraint(
                name: "ck_issue_allocation_qty",
                schema: "pharm",
                table: "issue_allocation");

            migrationBuilder.DropIndex(
                name: "ix_batch_product_id_state_expiry",
                schema: "pharm",
                table: "batch");

            migrationBuilder.DropIndex(
                name: "ix_batch_purchase_order_id",
                schema: "pharm",
                table: "batch");

            migrationBuilder.DropCheckConstraint(
                name: "ck_batch_money",
                schema: "pharm",
                table: "batch");

            migrationBuilder.DropCheckConstraint(
                name: "ck_batch_state",
                schema: "pharm",
                table: "batch");

            migrationBuilder.DropColumn(
                name: "xmin",
                schema: "pharm",
                table: "stock_audit_line");

            migrationBuilder.DropColumn(
                name: "xmin",
                schema: "pharm",
                table: "batch");

            migrationBuilder.AlterColumn<string>(
                name: "batch_no",
                schema: "pharm",
                table: "transfer_batch",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40);

            migrationBuilder.AlterColumn<string>(
                name: "state",
                schema: "pharm",
                table: "transfer",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40);

            migrationBuilder.AlterColumn<string>(
                name: "ref_table",
                schema: "pharm",
                table: "supplier_ledger",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "note",
                schema: "pharm",
                table: "supplier_ledger",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(4000)",
                oldMaxLength: 4000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "kind",
                schema: "pharm",
                table: "supplier_ledger",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40);

            migrationBuilder.AlterColumn<string>(
                name: "phone",
                schema: "pharm",
                table: "supplier",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "name",
                schema: "pharm",
                table: "supplier",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "ref_table",
                schema: "pharm",
                table: "stock_move",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "reason",
                schema: "pharm",
                table: "stock_move",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(4000)",
                oldMaxLength: 4000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "kind",
                schema: "pharm",
                table: "stock_move",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40);

            migrationBuilder.AlterColumn<string>(
                name: "state",
                schema: "pharm",
                table: "stock_audit",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40);

            migrationBuilder.AlterColumn<string>(
                name: "state",
                schema: "pharm",
                table: "purchase_order",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40);

            migrationBuilder.AlterColumn<string>(
                name: "unit",
                schema: "pharm",
                table: "product",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40);

            migrationBuilder.AlterColumn<string>(
                name: "strength",
                schema: "pharm",
                table: "product",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40);

            migrationBuilder.AlterColumn<string>(
                name: "generic",
                schema: "pharm",
                table: "product",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "form",
                schema: "pharm",
                table: "product",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40);

            migrationBuilder.AlterColumn<string>(
                name: "brand",
                schema: "pharm",
                table: "product",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "name",
                schema: "pharm",
                table: "outlet",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "kind",
                schema: "pharm",
                table: "outlet",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40);

            migrationBuilder.AlterColumn<string>(
                name: "name",
                schema: "pharm",
                table: "company",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "state_reason",
                schema: "pharm",
                table: "batch",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(4000)",
                oldMaxLength: 4000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "state",
                schema: "pharm",
                table: "batch",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40);

            migrationBuilder.AlterColumn<string>(
                name: "batch_no",
                schema: "pharm",
                table: "batch",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40);
        }
    }
}

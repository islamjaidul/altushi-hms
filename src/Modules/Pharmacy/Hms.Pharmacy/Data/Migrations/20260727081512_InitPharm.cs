using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Hms.Pharmacy.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitPharm : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "pharm");

            migrationBuilder.CreateTable(
                name: "batch",
                schema: "pharm",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    outlet_id = table.Column<long>(type: "bigint", nullable: false),
                    product_id = table.Column<long>(type: "bigint", nullable: false),
                    batch_no = table.Column<string>(type: "text", nullable: false),
                    expiry = table.Column<DateOnly>(type: "date", nullable: false),
                    qty_on_hand = table.Column<int>(type: "integer", nullable: false),
                    cost = table.Column<long>(type: "bigint", nullable: false),
                    mrp = table.Column<long>(type: "bigint", nullable: false),
                    state = table.Column<string>(type: "text", nullable: false),
                    state_reason = table.Column<string>(type: "text", nullable: true),
                    purchase_order_id = table.Column<long>(type: "bigint", nullable: true),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    received_by = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_batch", x => x.id);
                    table.CheckConstraint("ck_batch_qty", "qty_on_hand >= 0");
                });

            migrationBuilder.CreateTable(
                name: "company",
                schema: "pharm",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "text", nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_company", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "outlet",
                schema: "pharm",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outlet", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "product",
                schema: "pharm",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    brand = table.Column<string>(type: "text", nullable: false),
                    generic = table.Column<string>(type: "text", nullable: false),
                    strength = table.Column<string>(type: "text", nullable: false),
                    form = table.Column<string>(type: "text", nullable: false),
                    unit = table.Column<string>(type: "text", nullable: false),
                    reorder_level = table.Column<int>(type: "integer", nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_product", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "purchase_order",
                schema: "pharm",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    supplier_id = table.Column<long>(type: "bigint", nullable: false),
                    outlet_id = table.Column<long>(type: "bigint", nullable: false),
                    state = table.Column<string>(type: "text", nullable: false),
                    approval_id = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_purchase_order", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "purchase_order_line",
                schema: "pharm",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    purchase_order_id = table.Column<long>(type: "bigint", nullable: false),
                    product_id = table.Column<long>(type: "bigint", nullable: false),
                    qty = table.Column<int>(type: "integer", nullable: false),
                    received_qty = table.Column<int>(type: "integer", nullable: false),
                    expected_cost = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_purchase_order_line", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sale_allocation",
                schema: "pharm",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    invoice_id = table.Column<long>(type: "bigint", nullable: false),
                    charge_line_id = table.Column<long>(type: "bigint", nullable: false),
                    batch_id = table.Column<long>(type: "bigint", nullable: false),
                    product_id = table.Column<long>(type: "bigint", nullable: false),
                    qty = table.Column<int>(type: "integer", nullable: false),
                    unit_mrp = table.Column<long>(type: "bigint", nullable: false),
                    unit_cost = table.Column<long>(type: "bigint", nullable: false),
                    refunded_qty = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sale_allocation", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "stock_audit",
                schema: "pharm",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    outlet_id = table.Column<long>(type: "bigint", nullable: false),
                    state = table.Column<string>(type: "text", nullable: false),
                    approval_id = table.Column<long>(type: "bigint", nullable: true),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    started_by = table.Column<long>(type: "bigint", nullable: false),
                    posted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_audit", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "stock_audit_line",
                schema: "pharm",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    stock_audit_id = table.Column<long>(type: "bigint", nullable: false),
                    batch_id = table.Column<long>(type: "bigint", nullable: false),
                    product_id = table.Column<long>(type: "bigint", nullable: false),
                    system_qty = table.Column<int>(type: "integer", nullable: false),
                    counted_qty = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_audit_line", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "stock_move",
                schema: "pharm",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    outlet_id = table.Column<long>(type: "bigint", nullable: false),
                    batch_id = table.Column<long>(type: "bigint", nullable: false),
                    product_id = table.Column<long>(type: "bigint", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    qty = table.Column<int>(type: "integer", nullable: false),
                    ref_table = table.Column<string>(type: "text", nullable: true),
                    ref_id = table.Column<long>(type: "bigint", nullable: true),
                    reason = table.Column<string>(type: "text", nullable: true),
                    actor_id = table.Column<long>(type: "bigint", nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_move", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "supplier",
                schema: "pharm",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "text", nullable: false),
                    phone = table.Column<string>(type: "text", nullable: true),
                    active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_supplier", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "supplier_ledger",
                schema: "pharm",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    supplier_id = table.Column<long>(type: "bigint", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    amount = table.Column<long>(type: "bigint", nullable: false),
                    ref_table = table.Column<string>(type: "text", nullable: true),
                    ref_id = table.Column<long>(type: "bigint", nullable: true),
                    note = table.Column<string>(type: "text", nullable: true),
                    actor_id = table.Column<long>(type: "bigint", nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_supplier_ledger", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "transfer",
                schema: "pharm",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    from_outlet_id = table.Column<long>(type: "bigint", nullable: false),
                    to_outlet_id = table.Column<long>(type: "bigint", nullable: false),
                    state = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    received_by = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_transfer", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "transfer_batch",
                schema: "pharm",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    transfer_id = table.Column<long>(type: "bigint", nullable: false),
                    source_batch_id = table.Column<long>(type: "bigint", nullable: false),
                    product_id = table.Column<long>(type: "bigint", nullable: false),
                    batch_no = table.Column<string>(type: "text", nullable: false),
                    expiry = table.Column<DateOnly>(type: "date", nullable: false),
                    qty = table.Column<int>(type: "integer", nullable: false),
                    cost = table.Column<long>(type: "bigint", nullable: false),
                    mrp = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_transfer_batch", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "transfer_line",
                schema: "pharm",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    transfer_id = table.Column<long>(type: "bigint", nullable: false),
                    product_id = table.Column<long>(type: "bigint", nullable: false),
                    requested_qty = table.Column<int>(type: "integer", nullable: false),
                    sent_qty = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_transfer_line", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_batch_outlet_id_product_id_state_expiry",
                schema: "pharm",
                table: "batch",
                columns: new[] { "outlet_id", "product_id", "state", "expiry" });

            migrationBuilder.CreateIndex(
                name: "ix_product_brand_generic",
                schema: "pharm",
                table: "product",
                columns: new[] { "brand", "generic" });

            migrationBuilder.CreateIndex(
                name: "ix_sale_allocation_invoice_id",
                schema: "pharm",
                table: "sale_allocation",
                column: "invoice_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_move_batch_id_at",
                schema: "pharm",
                table: "stock_move",
                columns: new[] { "batch_id", "at" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_move_product_id_at",
                schema: "pharm",
                table: "stock_move",
                columns: new[] { "product_id", "at" });

            migrationBuilder.CreateIndex(
                name: "ix_supplier_ledger_supplier_id_at",
                schema: "pharm",
                table: "supplier_ledger",
                columns: new[] { "supplier_id", "at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "batch",
                schema: "pharm");

            migrationBuilder.DropTable(
                name: "company",
                schema: "pharm");

            migrationBuilder.DropTable(
                name: "outlet",
                schema: "pharm");

            migrationBuilder.DropTable(
                name: "product",
                schema: "pharm");

            migrationBuilder.DropTable(
                name: "purchase_order",
                schema: "pharm");

            migrationBuilder.DropTable(
                name: "purchase_order_line",
                schema: "pharm");

            migrationBuilder.DropTable(
                name: "sale_allocation",
                schema: "pharm");

            migrationBuilder.DropTable(
                name: "stock_audit",
                schema: "pharm");

            migrationBuilder.DropTable(
                name: "stock_audit_line",
                schema: "pharm");

            migrationBuilder.DropTable(
                name: "stock_move",
                schema: "pharm");

            migrationBuilder.DropTable(
                name: "supplier",
                schema: "pharm");

            migrationBuilder.DropTable(
                name: "supplier_ledger",
                schema: "pharm");

            migrationBuilder.DropTable(
                name: "transfer",
                schema: "pharm");

            migrationBuilder.DropTable(
                name: "transfer_batch",
                schema: "pharm");

            migrationBuilder.DropTable(
                name: "transfer_line",
                schema: "pharm");
        }
    }
}

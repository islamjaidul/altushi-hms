using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hms.Admin.Data.Migrations
{
    /// <inheritdoc />
    public partial class Hardening0039 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE adm.test_catalog ADD CONSTRAINT ck_test_catalog_name_len "
                + "CHECK (length(name) <= 200) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE adm.test_catalog ADD CONSTRAINT ck_test_catalog_dept_len "
                + "CHECK (length(dept) <= 40) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE adm.test_catalog ADD CONSTRAINT ck_test_catalog_code_len "
                + "CHECK (length(code) <= 40) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE adm.service ADD CONSTRAINT ck_service_name_len "
                + "CHECK (length(name) <= 200) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE adm.service ADD CONSTRAINT ck_service_kind_len "
                + "CHECK (length(kind) <= 40) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE adm.service ADD CONSTRAINT ck_service_dept_len "
                + "CHECK (length(dept) <= 40) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE adm.service ADD CONSTRAINT ck_service_code_len "
                + "CHECK (length(code) <= 40) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE adm.reporting_consultant ADD CONSTRAINT ck_reporting_consultant_name_len "
                + "CHECK (length(name) <= 200) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE adm.reporting_consultant ADD CONSTRAINT ck_reporting_consultant_degrees_len "
                + "CHECK (length(degrees) <= 200) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE adm.reporting_consultant ADD CONSTRAINT ck_reporting_consultant_bmdc_no_len "
                + "CHECK (length(bmdc_no) <= 40) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE adm.referrer ADD CONSTRAINT ck_referrer_phone_len "
                + "CHECK (length(phone) <= 20) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE adm.referrer ADD CONSTRAINT ck_referrer_name_len "
                + "CHECK (length(name) <= 200) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE adm.referrer ADD CONSTRAINT ck_referrer_kind_len "
                + "CHECK (length(kind) <= 40) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE adm.referrer ADD CONSTRAINT ck_referrer_code_len "
                + "CHECK (length(code) <= 40) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE adm.referrer ADD CONSTRAINT ck_referrer_area_len "
                + "CHECK (length(area) <= 200) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE adm.rate_version ADD CONSTRAINT ck_rate_version_scope_len "
                + "CHECK (length(scope) <= 40) NOT VALID;");

            migrationBuilder.Sql(
                "ALTER TABLE adm.rate_version ADD CONSTRAINT ck_rate_version_catalog_kind_len "
                + "CHECK (length(catalog_kind) <= 40) NOT VALID;");

            migrationBuilder.Sql("""
                ALTER TABLE adm.test_catalog ADD CONSTRAINT ck_test_catalog_tat
                    CHECK (tat_minutes >= 0) NOT VALID;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE adm.referrer ADD CONSTRAINT ck_referrer_commission
                    CHECK (commission_percent BETWEEN 0 AND 100) NOT VALID;
                """);

            migrationBuilder.CreateIndex(
                name: "ix_rate_version_catalog_kind_catalog_id_scope_branch_id_valid_",
                schema: "adm",
                table: "rate_version",
                columns: new[] { "catalog_kind", "catalog_id", "scope", "branch_id", "valid_from" });

            migrationBuilder.Sql("""
                ALTER TABLE adm.rate_version ADD CONSTRAINT ck_rate_version_effective_order
                    CHECK (valid_to IS NULL OR valid_to >= valid_from) NOT VALID;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE adm.rate_version ADD CONSTRAINT ck_rate_version_price
                    CHECK (price >= 0) NOT VALID;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE adm.rate_version ADD CONSTRAINT ck_rate_version_window
                    CHECK (valid_from BETWEEN '2000-01-01' AND '2100-01-01') NOT VALID;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_test_catalog_tat",
                schema: "adm",
                table: "test_catalog");

            migrationBuilder.DropCheckConstraint(
                name: "ck_referrer_commission",
                schema: "adm",
                table: "referrer");

            migrationBuilder.DropIndex(
                name: "ix_rate_version_catalog_kind_catalog_id_scope_branch_id_valid_",
                schema: "adm",
                table: "rate_version");

            migrationBuilder.DropCheckConstraint(
                name: "ck_rate_version_effective_order",
                schema: "adm",
                table: "rate_version");

            migrationBuilder.DropCheckConstraint(
                name: "ck_rate_version_price",
                schema: "adm",
                table: "rate_version");

            migrationBuilder.DropCheckConstraint(
                name: "ck_rate_version_window",
                schema: "adm",
                table: "rate_version");

            migrationBuilder.AlterColumn<string>(
                name: "name",
                schema: "adm",
                table: "test_catalog",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "dept",
                schema: "adm",
                table: "test_catalog",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40);

            migrationBuilder.AlterColumn<string>(
                name: "code",
                schema: "adm",
                table: "test_catalog",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40);

            migrationBuilder.AlterColumn<string>(
                name: "name",
                schema: "adm",
                table: "service",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "kind",
                schema: "adm",
                table: "service",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40);

            migrationBuilder.AlterColumn<string>(
                name: "dept",
                schema: "adm",
                table: "service",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40);

            migrationBuilder.AlterColumn<string>(
                name: "code",
                schema: "adm",
                table: "service",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40);

            migrationBuilder.AlterColumn<string>(
                name: "name",
                schema: "adm",
                table: "reporting_consultant",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "degrees",
                schema: "adm",
                table: "reporting_consultant",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "bmdc_no",
                schema: "adm",
                table: "reporting_consultant",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "phone",
                schema: "adm",
                table: "referrer",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "name",
                schema: "adm",
                table: "referrer",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "kind",
                schema: "adm",
                table: "referrer",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40);

            migrationBuilder.AlterColumn<string>(
                name: "code",
                schema: "adm",
                table: "referrer",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40);

            migrationBuilder.AlterColumn<string>(
                name: "area",
                schema: "adm",
                table: "referrer",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "scope",
                schema: "adm",
                table: "rate_version",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40);

            migrationBuilder.AlterColumn<string>(
                name: "catalog_kind",
                schema: "adm",
                table: "rate_version",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40);
        }
    }
}

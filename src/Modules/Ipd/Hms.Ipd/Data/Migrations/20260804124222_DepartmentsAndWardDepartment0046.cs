using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Hms.Ipd.Data.Migrations
{
    /// <inheritdoc />
    public partial class DepartmentsAndWardDepartment0046 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "department_id",
                schema: "ipd",
                table: "ward",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "department",
                schema: "ipd",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_department", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "department_staff",
                schema: "ipd",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    department_id = table.Column<long>(type: "bigint", nullable: false),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    staff_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_department_staff", x => x.id);
                    table.ForeignKey(
                        name: "fk_department_staff_department_department_id",
                        column: x => x.department_id,
                        principalSchema: "ipd",
                        principalTable: "department",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_ward_department_id",
                schema: "ipd",
                table: "ward",
                column: "department_id");

            migrationBuilder.CreateIndex(
                name: "ix_department_branch_id_name",
                schema: "ipd",
                table: "department",
                columns: new[] { "branch_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_department_staff_department_id_user_id",
                schema: "ipd",
                table: "department_staff",
                columns: new[] { "department_id", "user_id" },
                unique: true,
                filter: "active");

            migrationBuilder.CreateIndex(
                name: "ix_department_staff_user_id",
                schema: "ipd",
                table: "department_staff",
                column: "user_id");

            migrationBuilder.AddForeignKey(
                name: "fk_ward_department_department_id",
                schema: "ipd",
                table: "ward",
                column: "department_id",
                principalSchema: "ipd",
                principalTable: "department",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_ward_department_department_id",
                schema: "ipd",
                table: "ward");

            migrationBuilder.DropTable(
                name: "department_staff",
                schema: "ipd");

            migrationBuilder.DropTable(
                name: "department",
                schema: "ipd");

            migrationBuilder.DropIndex(
                name: "ix_ward_department_id",
                schema: "ipd",
                table: "ward");

            migrationBuilder.DropColumn(
                name: "department_id",
                schema: "ipd",
                table: "ward");
        }
    }
}

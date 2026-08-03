using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Hms.Ipd.Data.Migrations
{
    /// <inheritdoc />
    public partial class DutyAssignment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "duty_assignment",
                schema: "ipd",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    ward_id = table.Column<long>(type: "bigint", nullable: false),
                    on_date = table.Column<DateOnly>(type: "date", nullable: false),
                    shift_label = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    staff_role = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    employee_id = table.Column<long>(type: "bigint", nullable: true),
                    staff_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    ended_reason = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    ended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ended_by = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_duty_assignment", x => x.id);
                    table.CheckConstraint("ck_duty_ended", "active OR (ended_reason IS NOT NULL AND ended_at IS NOT NULL AND ended_by IS NOT NULL)");
                    table.CheckConstraint("ck_duty_role", "staff_role IN ('nurse','ward-boy','aya')");
                    table.CheckConstraint("ck_duty_shift", "shift_label IN ('morning','evening','night')");
                    table.ForeignKey(
                        name: "fk_duty_assignment_ward_ward_id",
                        column: x => x.ward_id,
                        principalSchema: "ipd",
                        principalTable: "ward",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_duty_assignment_on_date_ward_id",
                schema: "ipd",
                table: "duty_assignment",
                columns: new[] { "on_date", "ward_id" });

            migrationBuilder.CreateIndex(
                name: "ix_duty_assignment_ward_id_on_date_shift_label_staff_name",
                schema: "ipd",
                table: "duty_assignment",
                columns: new[] { "ward_id", "on_date", "shift_label", "staff_name" },
                unique: true,
                filter: "active");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "duty_assignment",
                schema: "ipd");
        }
    }
}

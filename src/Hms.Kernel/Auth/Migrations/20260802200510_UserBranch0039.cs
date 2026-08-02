using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hms.Kernel.Auth.Migrations
{
    /// <inheritdoc />
    public partial class UserBranch0039 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "branch_id",
                schema: "adm",
                table: "app_user",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "branch_id",
                schema: "adm",
                table: "app_user");
        }
    }
}

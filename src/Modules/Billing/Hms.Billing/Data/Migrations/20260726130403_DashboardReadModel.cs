using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hms.Billing.Data.Migrations
{
    /// <inheritdoc />
    public partial class DashboardReadModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {


            // 03 §10: the dashboard reads ONLY this view; M15 consumes the same summary rows,
            // so numbers never shift when Accounts arrives (§6.6).
            migrationBuilder.Sql("""
                CREATE VIEW bill.v_dashboard_day AS
                SELECT business_day,
                       sum(gross)         AS gross,
                       sum(discount)      AS discount,
                       sum(net)           AS net,
                       sum(due_created)   AS due_created,
                       sum(due_collected) AS due_collected,
                       sum(refunds)       AS refunds,
                       sum(variance)      AS variance,
                       count(*)           AS sessions
                FROM (SELECT DISTINCT ON (counter_session_id) *
                      FROM bill.day_close_summary
                      ORDER BY counter_session_id, version DESC) latest
                GROUP BY business_day;
                """);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}

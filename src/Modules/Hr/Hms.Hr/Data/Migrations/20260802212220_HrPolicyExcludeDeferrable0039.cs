using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hms.Hr.Data.Migrations
{
    /// <summary>
    /// Spec 0039: the effective-dating write path (<c>PolicyResolver.OpenOrNewAsync</c>) closes
    /// the open rule and inserts its successor in one SaveChanges, and EF batches the INSERT
    /// before the UPDATE — so a statement-time GiST exclusion refuses every legitimate policy
    /// re-dating with 23P01. Deferring the check to COMMIT keeps the invariant exactly as
    /// strong (no transaction can leave overlapping rules behind) while permitting the
    /// transient overlap inside one. Same decision as ipd.bed_stay's constraints (WP2).
    ///
    /// Additive under 03 §12: each constraint is dropped only to be re-created with the
    /// identical predicate plus deferrability.
    /// </summary>
    public partial class HrPolicyExcludeDeferrable0039 : Migration
    {
        private static readonly (string Table, string Scope)[] Rules =
        [
            ("employee_pay_structure", "employee_id"),
            ("employee_assignment", "employee_id"),
            ("payroll_policy", "branch_id"),
            ("pf_policy", "branch_id"),
            ("gratuity_rule", "branch_id"),
            ("grace_time_rule", "branch_id"),
            ("holiday_pay_policy", "branch_id"),
            ("deduction_rule", "branch_id"),
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var (table, scope) in Rules)
            {
                migrationBuilder.Sql($"""
                    ALTER TABLE hr.{table}
                        DROP CONSTRAINT ex_{table}_no_overlap;
                    ALTER TABLE hr.{table}
                        ADD CONSTRAINT ex_{table}_no_overlap
                        EXCLUDE USING gist (
                            {scope} WITH =,
                            daterange(effective_from, COALESCE(effective_to, 'infinity'::date), '[]') WITH &&
                        ) DEFERRABLE INITIALLY DEFERRED;
                    """);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var (table, scope) in Rules)
            {
                migrationBuilder.Sql($"""
                    ALTER TABLE hr.{table}
                        DROP CONSTRAINT ex_{table}_no_overlap;
                    ALTER TABLE hr.{table}
                        ADD CONSTRAINT ex_{table}_no_overlap
                        EXCLUDE USING gist (
                            {scope} WITH =,
                            daterange(effective_from, COALESCE(effective_to, 'infinity'::date), '[]') WITH &&
                        );
                    """);
            }
        }
    }
}

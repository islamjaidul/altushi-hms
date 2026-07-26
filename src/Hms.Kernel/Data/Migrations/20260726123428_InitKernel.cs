using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Hms.Kernel.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitKernel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "kernel");

            migrationBuilder.CreateTable(
                name: "approval_delegation",
                schema: "kernel",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    from_user = table.Column<long>(type: "bigint", nullable: false),
                    to_user = table.Column<long>(type: "bigint", nullable: false),
                    valid_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    valid_to = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    types = table.Column<string[]>(type: "text[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_approval_delegation", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "approval_policy",
                schema: "kernel",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    type = table.Column<string>(type: "text", nullable: false),
                    tier = table.Column<int>(type: "integer", nullable: false),
                    role = table.Column<string>(type: "text", nullable: false),
                    threshold_min = table.Column<long>(type: "bigint", nullable: true),
                    escalation_minutes = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_approval_policy", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "approval_request",
                schema: "kernel",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    type = table.Column<string>(type: "text", nullable: false),
                    source_table = table.Column<string>(type: "text", nullable: false),
                    source_id = table.Column<long>(type: "bigint", nullable: false),
                    requested_by = table.Column<long>(type: "bigint", nullable: false),
                    reason = table.Column<string>(type: "text", nullable: false),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    state = table.Column<string>(type: "text", nullable: false),
                    decided_by = table.Column<long>(type: "bigint", nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    decision_note = table.Column<string>(type: "text", nullable: true),
                    amount = table.Column<long>(type: "bigint", nullable: true),
                    threshold_snapshot = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_approval_request", x => x.id);
                    table.CheckConstraint("ck_approval_state", "state in ('pending','approved','rejected','expired')");
                });

            migrationBuilder.CreateTable(
                name: "audit_event",
                schema: "kernel",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actor_id = table.Column<long>(type: "bigint", nullable: false),
                    actor_name_snapshot = table.Column<string>(type: "text", nullable: false),
                    action = table.Column<string>(type: "text", nullable: false),
                    entity = table.Column<string>(type: "text", nullable: false),
                    entity_id = table.Column<long>(type: "bigint", nullable: false),
                    before = table.Column<string>(type: "jsonb", nullable: true),
                    after = table.Column<string>(type: "jsonb", nullable: true),
                    correlation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tier = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_event", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "branch",
                schema: "kernel",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    code = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_branch", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "job",
                schema: "kernel",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    type = table.Column<string>(type: "text", nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    run_after = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    locked_by = table.Column<string>(type: "text", nullable: true),
                    locked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    done_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    error = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_job", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "number_series",
                schema: "kernel",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    doc_type = table.Column<string>(type: "text", nullable: false),
                    fiscal_year = table.Column<string>(type: "text", nullable: false),
                    next_value = table.Column<long>(type: "bigint", nullable: false),
                    display_format = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_number_series", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "outbox",
                schema: "kernel",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    event_type = table.Column<string>(type: "text", nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    dispatched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "setting",
                schema: "kernel",
                columns: table => new
                {
                    key = table.Column<string>(type: "text", nullable: false),
                    value = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_setting", x => x.key);
                });

            migrationBuilder.CreateIndex(
                name: "ix_approval_request_state_type",
                schema: "kernel",
                table: "approval_request",
                columns: new[] { "state", "type" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_event_at",
                schema: "kernel",
                table: "audit_event",
                column: "at");

            migrationBuilder.CreateIndex(
                name: "ix_audit_event_entity_entity_id",
                schema: "kernel",
                table: "audit_event",
                columns: new[] { "entity", "entity_id" });

            migrationBuilder.CreateIndex(
                name: "ix_branch_code",
                schema: "kernel",
                table: "branch",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_job_run_after_done_at",
                schema: "kernel",
                table: "job",
                columns: new[] { "run_after", "done_at" });

            migrationBuilder.CreateIndex(
                name: "ix_number_series_branch_id_doc_type_fiscal_year",
                schema: "kernel",
                table: "number_series",
                columns: new[] { "branch_id", "doc_type", "fiscal_year" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_dispatched_at",
                schema: "kernel",
                table: "outbox",
                column: "dispatched_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "approval_delegation",
                schema: "kernel");

            migrationBuilder.DropTable(
                name: "approval_policy",
                schema: "kernel");

            migrationBuilder.DropTable(
                name: "approval_request",
                schema: "kernel");

            migrationBuilder.DropTable(
                name: "audit_event",
                schema: "kernel");

            migrationBuilder.DropTable(
                name: "branch",
                schema: "kernel");

            migrationBuilder.DropTable(
                name: "job",
                schema: "kernel");

            migrationBuilder.DropTable(
                name: "number_series",
                schema: "kernel");

            migrationBuilder.DropTable(
                name: "outbox",
                schema: "kernel");

            migrationBuilder.DropTable(
                name: "setting",
                schema: "kernel");
        }
    }
}

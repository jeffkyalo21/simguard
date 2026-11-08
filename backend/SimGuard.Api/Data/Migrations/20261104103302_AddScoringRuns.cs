using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SimGuard.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddScoringRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "scoring_runs",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SerialColumn),
                    trigger = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    force = table.Column<bool>(type: "boolean", nullable: false),
                    scope_sim_ids = table.Column<int[]>(type: "integer[]", nullable: true),
                    scope_from = table.Column<DateOnly>(type: "date", nullable: true),
                    scope_to = table.Column<DateOnly>(type: "date", nullable: true),
                    merged_requests = table.Column<int>(type: "integer", nullable: false),
                    requested_by_user_id = table.Column<int>(type: "integer", nullable: true),
                    requested_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    requested_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    finished_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    first_date = table.Column<DateOnly>(type: "date", nullable: true),
                    last_date = table.Column<DateOnly>(type: "date", nullable: true),
                    dates_scored = table.Column<int>(type: "integer", nullable: false),
                    sim_days_scored = table.Column<int>(type: "integer", nullable: false),
                    sim_days_skipped = table.Column<int>(type: "integer", nullable: false),
                    failures = table.Column<int>(type: "integer", nullable: false),
                    alerts_created = table.Column<int>(type: "integer", nullable: false),
                    alerts_updated = table.Column<int>(type: "integer", nullable: false),
                    error_summary = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    model_version = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_scoring_runs", x => x.id);
                    table.CheckConstraint("chk_scoring_runs_status", "status IN ('queued', 'running', 'succeeded', 'partial', 'failed')");
                    table.CheckConstraint("chk_scoring_runs_trigger", "trigger IN ('schedule', 'manual', 'import')");
                    table.ForeignKey(
                        name: "FK_scoring_runs_app_users_requested_by_user_id",
                        column: x => x.requested_by_user_id,
                        principalTable: "app_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "idx_scoring_runs_requested_at",
                table: "scoring_runs",
                column: "requested_at");

            migrationBuilder.CreateIndex(
                name: "idx_scoring_runs_status",
                table: "scoring_runs",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_scoring_runs_requested_by_user_id",
                table: "scoring_runs",
                column: "requested_by_user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "scoring_runs");
        }
    }
}

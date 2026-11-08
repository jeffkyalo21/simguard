using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SimGuard.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTierRecommendations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tier_recommendation_runs",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SerialColumn),
                    trigger = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    requested_by_user_id = table.Column<int>(type: "integer", nullable: true),
                    requested_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    finished_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    billing_cycle = table.Column<int>(type: "integer", nullable: true),
                    sims_evaluated = table.Column<int>(type: "integer", nullable: false),
                    recommendations_created = table.Column<int>(type: "integer", nullable: false),
                    error_summary = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tier_recommendation_runs", x => x.id);
                    table.CheckConstraint("chk_tier_rec_runs_status", "status IN ('running', 'succeeded', 'partial', 'failed')");
                    table.CheckConstraint("chk_tier_rec_runs_trigger", "trigger IN ('manual', 'automatic')");
                    table.ForeignKey(
                        name: "FK_tier_recommendation_runs_app_users_requested_by_user_id",
                        column: x => x.requested_by_user_id,
                        principalTable: "app_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "tier_recommendation_settings",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    interval_days = table.Column<int>(type: "integer", nullable: false),
                    updated_by_user_id = table.Column<int>(type: "integer", nullable: true),
                    updated_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tier_recommendation_settings", x => x.id);
                    table.CheckConstraint("chk_tier_rec_settings_interval", "interval_days BETWEEN 90 AND 365");
                    table.CheckConstraint("chk_tier_rec_settings_single_row", "id = 1");
                    table.ForeignKey(
                        name: "FK_tier_recommendation_settings_app_users_updated_by_user_id",
                        column: x => x.updated_by_user_id,
                        principalTable: "app_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "tier_recommendations",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SerialColumn),
                    sim_id = table.Column<int>(type: "integer", nullable: false),
                    run_id = table.Column<int>(type: "integer", nullable: false),
                    current_tier = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    recommended_tier = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    recommendation = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    billing_cycle = table.Column<int>(type: "integer", nullable: false),
                    projected_overage_mb = table.Column<double>(type: "double precision", nullable: true),
                    days_to_depletion = table.Column<int>(type: "integer", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    dismissed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    dismissed_by_user_id = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tier_recommendations", x => x.id);
                    table.CheckConstraint("chk_tier_recs_current_tier", "current_tier IN ('field', 'manager', 'professional', 'support')");
                    table.CheckConstraint("chk_tier_recs_recommendation", "recommendation IN ('upgrade', 'downgrade')");
                    table.CheckConstraint("chk_tier_recs_recommended_tier", "recommended_tier IN ('field', 'manager', 'professional', 'support')");
                    table.CheckConstraint("chk_tier_recs_status", "status IN ('open', 'dismissed')");
                    table.ForeignKey(
                        name: "FK_tier_recommendations_app_users_dismissed_by_user_id",
                        column: x => x.dismissed_by_user_id,
                        principalTable: "app_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_tier_recommendations_sims_sim_id",
                        column: x => x.sim_id,
                        principalTable: "sims",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_tier_recommendations_tier_recommendation_runs_run_id",
                        column: x => x.run_id,
                        principalTable: "tier_recommendation_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "tier_recommendation_settings",
                columns: new[] { "id", "interval_days", "updated_at", "updated_by", "updated_by_user_id" },
                values: new object[] { 1, 90, null, null, null });

            migrationBuilder.CreateIndex(
                name: "idx_tier_rec_runs_started_at",
                table: "tier_recommendation_runs",
                column: "started_at");

            migrationBuilder.CreateIndex(
                name: "IX_tier_recommendation_runs_requested_by_user_id",
                table: "tier_recommendation_runs",
                column: "requested_by_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_tier_recommendation_settings_updated_by_user_id",
                table: "tier_recommendation_settings",
                column: "updated_by_user_id");

            migrationBuilder.CreateIndex(
                name: "idx_tier_recs_one_open_per_sim",
                table: "tier_recommendations",
                column: "sim_id",
                unique: true,
                filter: "status = 'open'");

            migrationBuilder.CreateIndex(
                name: "idx_tier_recs_sim_status",
                table: "tier_recommendations",
                columns: new[] { "sim_id", "status" });

            migrationBuilder.CreateIndex(
                name: "idx_tier_recs_status",
                table: "tier_recommendations",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_tier_recommendations_dismissed_by_user_id",
                table: "tier_recommendations",
                column: "dismissed_by_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_tier_recommendations_run_id",
                table: "tier_recommendations",
                column: "run_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tier_recommendation_settings");

            migrationBuilder.DropTable(
                name: "tier_recommendations");

            migrationBuilder.DropTable(
                name: "tier_recommendation_runs");
        }
    }
}

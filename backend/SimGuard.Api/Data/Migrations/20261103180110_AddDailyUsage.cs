using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SimGuard.Api.Data.Migrations
{
    /// <summary>
    /// Daily usage per SIM, one row per SIM per day, with the columns of the
    /// ML daily usage contract (ml-service/docs/daily_usage_schema.md). The
    /// ml_daily_usage view is what the ML Postgres reader selects from: the
    /// contract columns only, in contract order.
    /// </summary>
    public partial class AddDailyUsage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "daily_usage",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SerialColumn),
                    sim_id = table.Column<int>(type: "integer", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    total_download_mb = table.Column<double>(type: "double precision", nullable: false),
                    total_upload_mb = table.Column<double>(type: "double precision", nullable: false),
                    data_session_count = table.Column<long>(type: "bigint", nullable: false),
                    data_duration_s = table.Column<long>(type: "bigint", nullable: false),
                    hotspot_sessions = table.Column<long>(type: "bigint", nullable: false),
                    public_apn_sessions = table.Column<long>(type: "bigint", nullable: false),
                    unique_towers = table.Column<long>(type: "bigint", nullable: false),
                    geo_flag_count = table.Column<long>(type: "bigint", nullable: false),
                    peak_mb = table.Column<double>(type: "double precision", nullable: false),
                    offpeak_mb = table.Column<double>(type: "double precision", nullable: false),
                    total_mb = table.Column<double>(type: "double precision", nullable: false),
                    roaming_sessions = table.Column<long>(type: "bigint", nullable: false),
                    billed_mb = table.Column<double>(type: "double precision", nullable: false),
                    tier = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    voice_minutes_used = table.Column<double>(type: "double precision", nullable: false),
                    sms_count = table.Column<long>(type: "bigint", nullable: false),
                    voice_minutes_remaining = table.Column<double>(type: "double precision", nullable: false),
                    sms_remaining = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_daily_usage", x => x.id);
                    table.CheckConstraint("CK_daily_usage_tier", "tier IN ('field', 'manager', 'professional', 'support')");
                    table.ForeignKey(
                        name: "FK_daily_usage_sims_sim_id",
                        column: x => x.sim_id,
                        principalTable: "sims",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "idx_daily_usage_date",
                table: "daily_usage",
                column: "date");

            migrationBuilder.CreateIndex(
                name: "idx_daily_usage_sim_date",
                table: "daily_usage",
                columns: new[] { "sim_id", "date" },
                unique: true);

            migrationBuilder.Sql("""
                CREATE VIEW ml_daily_usage AS
                SELECT sim_id, date,
                       total_download_mb, total_upload_mb,
                       data_session_count, data_duration_s,
                       hotspot_sessions, public_apn_sessions,
                       unique_towers, geo_flag_count,
                       peak_mb, offpeak_mb, total_mb,
                       roaming_sessions, billed_mb, tier,
                       voice_minutes_used, sms_count,
                       voice_minutes_remaining, sms_remaining
                FROM daily_usage;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP VIEW IF EXISTS ml_daily_usage;");

            migrationBuilder.DropTable(
                name: "daily_usage");
        }
    }
}

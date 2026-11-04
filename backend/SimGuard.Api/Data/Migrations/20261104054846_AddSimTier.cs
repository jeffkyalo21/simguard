using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SimGuard.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSimTier : Migration
    {
        private const string OriginalViewSql = """
            CREATE VIEW ml_daily_usage AS
            SELECT sim_id, date, total_download_mb, total_upload_mb, data_session_count,
                   data_duration_s, hotspot_sessions, public_apn_sessions, unique_towers,
                   geo_flag_count, peak_mb, offpeak_mb, total_mb, roaming_sessions, billed_mb,
                   tier, voice_minutes_used, sms_count, voice_minutes_remaining, sms_remaining
            FROM daily_usage;
            """;

        // sims.tier, when set, overrides the tier stored on each usage row,
        // so a tier change moves the SIM to its new ML cohort for every
        // score computed afterwards.
        private const string TierAwareViewSql = """
            CREATE VIEW ml_daily_usage AS
            SELECT u.sim_id, u.date, u.total_download_mb, u.total_upload_mb, u.data_session_count,
                   u.data_duration_s, u.hotspot_sessions, u.public_apn_sessions, u.unique_towers,
                   u.geo_flag_count, u.peak_mb, u.offpeak_mb, u.total_mb, u.roaming_sessions,
                   u.billed_mb, COALESCE(s.tier, u.tier) AS tier, u.voice_minutes_used,
                   u.sms_count, u.voice_minutes_remaining, u.sms_remaining
            FROM daily_usage u
            JOIN sims s ON s.id = u.sim_id;
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "tier",
                table: "sims",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_sims_tier",
                table: "sims",
                sql: "tier IS NULL OR tier IN ('field', 'manager', 'professional', 'support')");

            // Backfill from each SIM's most recent usage row.
            migrationBuilder.Sql("""
                UPDATE sims s
                SET tier = latest.tier
                FROM (
                    SELECT DISTINCT ON (sim_id) sim_id, lower(tier) AS tier
                    FROM daily_usage
                    WHERE lower(tier) IN ('field', 'manager', 'professional', 'support')
                    ORDER BY sim_id, date DESC
                ) latest
                WHERE latest.sim_id = s.id;
                """);

            migrationBuilder.Sql("DROP VIEW IF EXISTS ml_daily_usage;");
            migrationBuilder.Sql(TierAwareViewSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP VIEW IF EXISTS ml_daily_usage;");
            migrationBuilder.Sql(OriginalViewSql);

            migrationBuilder.DropCheckConstraint(
                name: "CK_sims_tier",
                table: "sims");

            migrationBuilder.DropColumn(
                name: "tier",
                table: "sims");
        }
    }
}

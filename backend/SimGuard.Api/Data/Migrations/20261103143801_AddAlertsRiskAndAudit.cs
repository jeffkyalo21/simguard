using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SimGuard.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAlertsRiskAndAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_audit_logs_sims_affected_sim_id",
                table: "audit_logs");

            migrationBuilder.DropCheckConstraint(
                name: "CK_alerts_risk_level",
                table: "alerts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_alerts_status",
                table: "alerts");

            migrationBuilder.RenameIndex(
                name: "IX_audit_logs_affected_sim_id",
                table: "audit_logs",
                newName: "idx_audit_logs_sim");

            migrationBuilder.RenameColumn(
                name: "detected_at",
                table: "alerts",
                newName: "created_at");

            migrationBuilder.RenameColumn(
                name: "alert_type",
                table: "alerts",
                newName: "type");

            migrationBuilder.AlterColumn<int>(
                name: "user_id",
                table: "audit_logs",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<string>(
                name: "actor_email",
                table: "audit_logs",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "actor_user_id",
                table: "audit_logs",
                type: "integer",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "status",
                table: "alerts",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "flagged",
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldDefaultValue: "open");

            migrationBuilder.AddColumn<string>(
                name: "model_version",
                table: "alerts",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<JsonDocument>(
                name: "reasons",
                table: "alerts",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'[]'::jsonb");

            migrationBuilder.AddColumn<string>(
                name: "resolution",
                table: "alerts",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "resolution_note",
                table: "alerts",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "resolved_at",
                table: "alerts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "resolved_by_user_id",
                table: "alerts",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "risk_scores",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SerialColumn),
                    sim_id = table.Column<int>(type: "integer", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    probability = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: false),
                    bucket = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reasons = table.Column<JsonDocument>(type: "jsonb", nullable: false, defaultValueSql: "'[]'::jsonb"),
                    model_version = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_risk_scores", x => x.id);
                    table.CheckConstraint("CK_risk_scores_bucket", "bucket IN ('clear', 'escalated', 'flag')");
                    table.CheckConstraint("CK_risk_scores_probability", "probability >= 0 AND probability <= 1");
                    table.ForeignKey(
                        name: "FK_risk_scores_sims_sim_id",
                        column: x => x.sim_id,
                        principalTable: "sims",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "idx_audit_logs_action",
                table: "audit_logs",
                column: "action");

            migrationBuilder.CreateIndex(
                name: "idx_audit_logs_actor",
                table: "audit_logs",
                column: "actor_user_id");

            migrationBuilder.CreateIndex(
                name: "idx_alerts_created_at",
                table: "alerts",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "idx_alerts_model_version",
                table: "alerts",
                column: "model_version");

            migrationBuilder.CreateIndex(
                name: "IX_alerts_resolved_by_user_id",
                table: "alerts",
                column: "resolved_by_user_id");

            // Move any rows written with the old vocabulary onto the new one
            // before the new check constraints are added. Scores stored on a
            // 0 to 100 scale are brought down to 0 to 1.
            migrationBuilder.Sql("""
                UPDATE alerts SET status = CASE status
                    WHEN 'open' THEN 'flagged'
                    WHEN 'investigating' THEN 'escalated'
                    WHEN 'resolved' THEN 'cleared'
                    ELSE status END;
                UPDATE alerts SET risk_level = 'high' WHERE risk_level = 'critical';
                UPDATE alerts SET type = 'other'
                    WHERE type NOT IN ('inactiveBilling', 'sharing', 'reselling', 'ghostUsage', 'other');
                UPDATE alerts SET risk_score = round(risk_score / 100, 2) WHERE risk_score > 1;
                UPDATE alerts SET resolved_at = created_at WHERE status = 'cleared' AND resolved_at IS NULL;
                """);

            migrationBuilder.AddCheckConstraint(
                name: "CK_alerts_resolution",
                table: "alerts",
                sql: "resolution IS NULL OR resolution IN ('confirmedMisuse', 'falsePositive')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_alerts_risk_level",
                table: "alerts",
                sql: "risk_level IN ('high', 'medium', 'low')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_alerts_risk_score",
                table: "alerts",
                sql: "risk_score >= 0 AND risk_score <= 1");

            migrationBuilder.AddCheckConstraint(
                name: "CK_alerts_status",
                table: "alerts",
                sql: "status IN ('flagged', 'escalated', 'cleared')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_alerts_type",
                table: "alerts",
                sql: "type IN ('inactiveBilling', 'sharing', 'reselling', 'ghostUsage', 'other')");

            migrationBuilder.CreateIndex(
                name: "idx_risk_scores_date",
                table: "risk_scores",
                column: "date");

            migrationBuilder.CreateIndex(
                name: "idx_risk_scores_model_version",
                table: "risk_scores",
                column: "model_version");

            migrationBuilder.CreateIndex(
                name: "idx_risk_scores_sim_date",
                table: "risk_scores",
                columns: new[] { "sim_id", "date" },
                unique: true,
                descending: new[] { false, true });

            migrationBuilder.AddForeignKey(
                name: "FK_alerts_app_users_resolved_by_user_id",
                table: "alerts",
                column: "resolved_by_user_id",
                principalTable: "app_users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_audit_logs_app_users_actor_user_id",
                table: "audit_logs",
                column: "actor_user_id",
                principalTable: "app_users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_audit_logs_sims_affected_sim_id",
                table: "audit_logs",
                column: "affected_sim_id",
                principalTable: "sims",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_alerts_app_users_resolved_by_user_id",
                table: "alerts");

            migrationBuilder.DropForeignKey(
                name: "FK_audit_logs_app_users_actor_user_id",
                table: "audit_logs");

            migrationBuilder.DropForeignKey(
                name: "FK_audit_logs_sims_affected_sim_id",
                table: "audit_logs");

            migrationBuilder.DropTable(
                name: "risk_scores");

            migrationBuilder.DropIndex(
                name: "idx_audit_logs_action",
                table: "audit_logs");

            migrationBuilder.DropIndex(
                name: "idx_audit_logs_actor",
                table: "audit_logs");

            migrationBuilder.DropIndex(
                name: "idx_alerts_created_at",
                table: "alerts");

            migrationBuilder.DropIndex(
                name: "idx_alerts_model_version",
                table: "alerts");

            migrationBuilder.DropIndex(
                name: "IX_alerts_resolved_by_user_id",
                table: "alerts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_alerts_resolution",
                table: "alerts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_alerts_risk_level",
                table: "alerts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_alerts_risk_score",
                table: "alerts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_alerts_status",
                table: "alerts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_alerts_type",
                table: "alerts");

            migrationBuilder.DropColumn(
                name: "actor_email",
                table: "audit_logs");

            migrationBuilder.DropColumn(
                name: "actor_user_id",
                table: "audit_logs");

            migrationBuilder.DropColumn(
                name: "model_version",
                table: "alerts");

            migrationBuilder.DropColumn(
                name: "reasons",
                table: "alerts");

            migrationBuilder.DropColumn(
                name: "resolution",
                table: "alerts");

            migrationBuilder.DropColumn(
                name: "resolution_note",
                table: "alerts");

            migrationBuilder.DropColumn(
                name: "resolved_at",
                table: "alerts");

            migrationBuilder.DropColumn(
                name: "resolved_by_user_id",
                table: "alerts");

            migrationBuilder.RenameIndex(
                name: "idx_audit_logs_sim",
                table: "audit_logs",
                newName: "IX_audit_logs_affected_sim_id");

            migrationBuilder.RenameColumn(
                name: "type",
                table: "alerts",
                newName: "alert_type");

            migrationBuilder.RenameColumn(
                name: "created_at",
                table: "alerts",
                newName: "detected_at");

            migrationBuilder.AlterColumn<int>(
                name: "user_id",
                table: "audit_logs",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "status",
                table: "alerts",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "open",
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldDefaultValue: "flagged");

            migrationBuilder.Sql("""
                UPDATE alerts SET status = CASE status
                    WHEN 'flagged' THEN 'open'
                    WHEN 'escalated' THEN 'investigating'
                    WHEN 'cleared' THEN 'resolved'
                    ELSE status END;
                """);

            migrationBuilder.AddCheckConstraint(
                name: "CK_alerts_risk_level",
                table: "alerts",
                sql: "risk_level IN ('low', 'medium', 'high', 'critical')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_alerts_status",
                table: "alerts",
                sql: "status IN ('open', 'investigating', 'resolved')");

            migrationBuilder.AddForeignKey(
                name: "FK_audit_logs_sims_affected_sim_id",
                table: "audit_logs",
                column: "affected_sim_id",
                principalTable: "sims",
                principalColumn: "id");
        }
    }
}

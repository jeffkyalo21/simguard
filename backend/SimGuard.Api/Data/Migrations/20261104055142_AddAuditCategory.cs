using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SimGuard.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditCategory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "actor_role",
                table: "audit_logs",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "category",
                table: "audit_logs",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "organisation");

            // Backfill: user, auth and role actions are system, the rest
            // organisation. actor_role stays null on existing rows because
            // the actor's role at the time was not recorded.
            migrationBuilder.Sql("""
                UPDATE audit_logs
                SET category = CASE
                    WHEN action LIKE 'user%' OR action LIKE 'auth%' OR action LIKE 'role%'
                        THEN 'system'
                    ELSE 'organisation'
                END;
                """);

            // The writer always sets the category; no database default.
            migrationBuilder.Sql("ALTER TABLE audit_logs ALTER COLUMN category DROP DEFAULT;");

            migrationBuilder.CreateIndex(
                name: "idx_audit_logs_category",
                table: "audit_logs",
                column: "category");

            migrationBuilder.AddCheckConstraint(
                name: "CK_audit_logs_category",
                table: "audit_logs",
                sql: "category IN ('system', 'organisation')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "idx_audit_logs_category",
                table: "audit_logs");

            migrationBuilder.DropCheckConstraint(
                name: "CK_audit_logs_category",
                table: "audit_logs");

            migrationBuilder.DropColumn(
                name: "actor_role",
                table: "audit_logs");

            migrationBuilder.DropColumn(
                name: "category",
                table: "audit_logs");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SimGuard.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddImportRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "import_runs",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SerialColumn),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    uploaded_by_user_id = table.Column<int>(type: "integer", nullable: true),
                    uploaded_by_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    finished_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    skip_invalid = table.Column<bool>(type: "boolean", nullable: false),
                    total_rows = table.Column<int>(type: "integer", nullable: false),
                    valid_rows = table.Column<int>(type: "integer", nullable: false),
                    invalid_rows = table.Column<int>(type: "integer", nullable: false),
                    inserted = table.Column<int>(type: "integer", nullable: false),
                    updated = table.Column<int>(type: "integer", nullable: false),
                    unchanged = table.Column<int>(type: "integer", nullable: false),
                    error_count = table.Column<int>(type: "integer", nullable: false),
                    warning_count = table.Column<int>(type: "integer", nullable: false),
                    message = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_import_runs", x => x.id);
                    table.CheckConstraint("chk_import_runs_status", "status IN ('committed', 'rejected', 'failed')");
                    table.CheckConstraint("chk_import_runs_type", "type IN ('departments', 'employees', 'sims', 'usage')");
                    table.ForeignKey(
                        name: "FK_import_runs_app_users_uploaded_by_user_id",
                        column: x => x.uploaded_by_user_id,
                        principalTable: "app_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "idx_import_runs_started_at",
                table: "import_runs",
                column: "started_at");

            migrationBuilder.CreateIndex(
                name: "idx_import_runs_type",
                table: "import_runs",
                column: "type");

            migrationBuilder.CreateIndex(
                name: "IX_import_runs_uploaded_by_user_id",
                table: "import_runs",
                column: "uploaded_by_user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "import_runs");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SimGuard.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AllowAllocationsImportType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "chk_import_runs_type",
                table: "import_runs");

            migrationBuilder.AddCheckConstraint(
                name: "chk_import_runs_type",
                table: "import_runs",
                sql: "type IN ('departments', 'employees', 'sims', 'allocations', 'usage')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "chk_import_runs_type",
                table: "import_runs");

            migrationBuilder.AddCheckConstraint(
                name: "chk_import_runs_type",
                table: "import_runs",
                sql: "type IN ('departments', 'employees', 'sims', 'usage')");
        }
    }
}

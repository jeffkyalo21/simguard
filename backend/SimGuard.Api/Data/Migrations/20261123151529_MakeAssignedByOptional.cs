using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SimGuard.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class MakeAssignedByOptional : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "assigned_by",
                table: "allocations",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Rows without a recorded assigner take the holder, as the old import did.
            migrationBuilder.Sql("UPDATE allocations SET assigned_by = employee_id WHERE assigned_by IS NULL;");

            migrationBuilder.AlterColumn<int>(
                name: "assigned_by",
                table: "allocations",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);
        }
    }
}

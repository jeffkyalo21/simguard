using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace SimGuard.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTierAllowances : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tier_allowances",
                columns: table => new
                {
                    tier = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    data_mb = table.Column<int>(type: "integer", nullable: false),
                    voice_minutes = table.Column<int>(type: "integer", nullable: false),
                    sms = table.Column<int>(type: "integer", nullable: false),
                    monthly_cost = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tier_allowances", x => x.tier);
                    table.CheckConstraint("CK_tier_allowances_tier", "tier IN ('field', 'manager', 'professional', 'support')");
                    table.CheckConstraint("CK_tier_allowances_values", "data_mb > 0 AND voice_minutes >= 0 AND sms >= 0 AND monthly_cost >= 0");
                });

            migrationBuilder.InsertData(
                table: "tier_allowances",
                columns: new[] { "tier", "data_mb", "monthly_cost", "sms", "voice_minutes" },
                values: new object[,]
                {
                    { "field", 102400, 5000m, 340, 500 },
                    { "manager", 61440, 3500m, 205, 300 },
                    { "professional", 40960, 2500m, 135, 200 },
                    { "support", 20480, 1500m, 70, 100 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_sims_tier",
                table: "sims",
                column: "tier");

            migrationBuilder.AddForeignKey(
                name: "FK_sims_tier_allowances_tier",
                table: "sims",
                column: "tier",
                principalTable: "tier_allowances",
                principalColumn: "tier",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_sims_tier_allowances_tier",
                table: "sims");

            migrationBuilder.DropTable(
                name: "tier_allowances");

            migrationBuilder.DropIndex(
                name: "IX_sims_tier",
                table: "sims");
        }
    }
}

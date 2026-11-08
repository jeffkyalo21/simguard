using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SimGuard.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTierRecommendationApplied : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "chk_tier_recs_status",
                table: "tier_recommendations");

            migrationBuilder.AddColumn<DateTime>(
                name: "applied_at",
                table: "tier_recommendations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "applied_by_user_id",
                table: "tier_recommendations",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_tier_recommendations_applied_by_user_id",
                table: "tier_recommendations",
                column: "applied_by_user_id");

            migrationBuilder.AddCheckConstraint(
                name: "chk_tier_recs_status",
                table: "tier_recommendations",
                sql: "status IN ('open', 'dismissed', 'applied')");

            migrationBuilder.AddForeignKey(
                name: "FK_tier_recommendations_app_users_applied_by_user_id",
                table: "tier_recommendations",
                column: "applied_by_user_id",
                principalTable: "app_users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_tier_recommendations_app_users_applied_by_user_id",
                table: "tier_recommendations");

            migrationBuilder.DropIndex(
                name: "IX_tier_recommendations_applied_by_user_id",
                table: "tier_recommendations");

            migrationBuilder.DropCheckConstraint(
                name: "chk_tier_recs_status",
                table: "tier_recommendations");

            migrationBuilder.DropColumn(
                name: "applied_at",
                table: "tier_recommendations");

            migrationBuilder.DropColumn(
                name: "applied_by_user_id",
                table: "tier_recommendations");

            migrationBuilder.AddCheckConstraint(
                name: "chk_tier_recs_status",
                table: "tier_recommendations",
                sql: "status IN ('open', 'dismissed')");
        }
    }
}

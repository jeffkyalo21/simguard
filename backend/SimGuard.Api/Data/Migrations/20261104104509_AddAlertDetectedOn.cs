using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SimGuard.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAlertDetectedOn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "detected_on",
                table: "alerts",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "last_detected_on",
                table: "alerts",
                type: "date",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "idx_alerts_detected_on",
                table: "alerts",
                column: "detected_on");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "idx_alerts_detected_on",
                table: "alerts");

            migrationBuilder.DropColumn(
                name: "detected_on",
                table: "alerts");

            migrationBuilder.DropColumn(
                name: "last_detected_on",
                table: "alerts");
        }
    }
}

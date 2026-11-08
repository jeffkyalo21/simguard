using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SimGuard.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCarrierFeed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "carrier_name",
                table: "import_runs",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "source",
                table: "import_runs",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "csv");

            migrationBuilder.AddColumn<string>(
                name: "feed_base_url",
                table: "carriers",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "feed_enabled",
                table: "carriers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "feed_key_set",
                table: "carriers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateOnly>(
                name: "feed_start_date",
                table: "carriers",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "last_sync_at",
                table: "carriers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "last_sync_date",
                table: "carriers",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "last_sync_message",
                table: "carriers",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "last_sync_status",
                table: "carriers",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "carrier_secrets",
                columns: table => new
                {
                    carrier_id = table.Column<int>(type: "integer", nullable: false),
                    protected_value = table.Column<string>(type: "text", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_carrier_secrets", x => x.carrier_id);
                    table.ForeignKey(
                        name: "FK_carrier_secrets_carriers_carrier_id",
                        column: x => x.carrier_id,
                        principalTable: "carriers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "carrier_secrets");

            migrationBuilder.DropColumn(
                name: "carrier_name",
                table: "import_runs");

            migrationBuilder.DropColumn(
                name: "source",
                table: "import_runs");

            migrationBuilder.DropColumn(
                name: "feed_base_url",
                table: "carriers");

            migrationBuilder.DropColumn(
                name: "feed_enabled",
                table: "carriers");

            migrationBuilder.DropColumn(
                name: "feed_key_set",
                table: "carriers");

            migrationBuilder.DropColumn(
                name: "feed_start_date",
                table: "carriers");

            migrationBuilder.DropColumn(
                name: "last_sync_at",
                table: "carriers");

            migrationBuilder.DropColumn(
                name: "last_sync_date",
                table: "carriers");

            migrationBuilder.DropColumn(
                name: "last_sync_message",
                table: "carriers");

            migrationBuilder.DropColumn(
                name: "last_sync_status",
                table: "carriers");
        }
    }
}

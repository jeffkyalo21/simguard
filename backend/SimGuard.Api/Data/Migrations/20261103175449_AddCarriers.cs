using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SimGuard.Api.Data.Migrations
{
    /// <summary>
    /// Moves the SIM operator text into a carriers table without losing data:
    /// one carrier per distinct operator value (trimmed, compared ignoring
    /// case, cut to 40 characters, empty values become "Unknown"), plus
    /// Safaricom, Airtel and Telkom when missing. Every SIM then points to
    /// its carrier and the operator column is dropped. Down puts the carrier
    /// name back into sims.operator.
    /// </summary>
    public partial class AddCarriers : Migration
    {
        // The operator value as a carrier name, for the copy in Up.
        private const string OperatorAsName =
            "left(COALESCE(NULLIF(btrim(operator), ''), 'Unknown'), 40)";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "carriers",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SerialColumn),
                    name = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_carriers", x => x.id);
                });

            // Names are unique ignoring case. Not in the EF model (it cannot
            // describe an expression index), so it lives only here.
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX idx_carriers_name_lower ON carriers (lower(name));");

            // One carrier per operator value. The spelling of the oldest SIM
            // wins; created_at is the date of that carrier's first SIM.
            migrationBuilder.Sql($"""
                INSERT INTO carriers (name, created_at)
                SELECT DISTINCT ON (lower(name)) name, min(created_at) OVER (PARTITION BY lower(name))
                FROM (SELECT {OperatorAsName} AS name, created_at, id FROM sims) AS existing
                ORDER BY lower(name), created_at, id;
                """);

            migrationBuilder.Sql("""
                INSERT INTO carriers (name)
                VALUES ('Safaricom'), ('Airtel'), ('Telkom')
                ON CONFLICT ((lower(name))) DO NOTHING;
                """);

            migrationBuilder.AddColumn<int>(
                name: "carrier_id",
                table: "sims",
                type: "integer",
                nullable: true);

            migrationBuilder.Sql($"""
                UPDATE sims
                SET carrier_id = carriers.id
                FROM carriers
                WHERE lower(carriers.name) = lower({OperatorAsName.Replace("operator", "sims.operator")});
                """);

            migrationBuilder.AlterColumn<int>(
                name: "carrier_id",
                table: "sims",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "idx_sims_carrier",
                table: "sims",
                column: "carrier_id");

            migrationBuilder.AddForeignKey(
                name: "FK_sims_carriers_carrier_id",
                table: "sims",
                column: "carrier_id",
                principalTable: "carriers",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.DropColumn(
                name: "operator",
                table: "sims");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "operator",
                table: "sims",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE sims
                SET operator = carriers.name
                FROM carriers
                WHERE carriers.id = sims.carrier_id;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "operator",
                table: "sims",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldNullable: true);

            migrationBuilder.DropForeignKey(
                name: "FK_sims_carriers_carrier_id",
                table: "sims");

            migrationBuilder.DropIndex(
                name: "idx_sims_carrier",
                table: "sims");

            migrationBuilder.DropColumn(
                name: "carrier_id",
                table: "sims");

            migrationBuilder.DropTable(
                name: "carriers");
        }
    }
}

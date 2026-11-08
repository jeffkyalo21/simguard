using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SimGuard.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAccessRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "access_requests",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SerialColumn),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    organisation = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    reviewed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    reviewed_by_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_access_requests", x => x.id);
                    table.CheckConstraint("chk_access_requests_role", "role IN ('SecurityOfficer', 'Auditor')");
                    table.CheckConstraint("chk_access_requests_status", "status IN ('pending', 'approved', 'declined')");
                });

            migrationBuilder.CreateIndex(
                name: "idx_access_requests_created_at",
                table: "access_requests",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "idx_access_requests_pending_email",
                table: "access_requests",
                column: "email",
                unique: true,
                filter: "status = 'pending'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "access_requests");
        }
    }
}

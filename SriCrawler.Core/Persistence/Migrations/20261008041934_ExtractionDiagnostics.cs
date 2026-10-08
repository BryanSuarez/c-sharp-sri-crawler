using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SriCrawler.Core.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ExtractionDiagnostics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DiagnosticsJson",
                schema: "crawler",
                table: "attempts",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "DiagnosticsSequence",
                schema: "crawler",
                table: "attempts",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DiagnosticsUpdatedAt",
                schema: "crawler",
                table: "attempts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DiagnosticsVersion",
                schema: "crawler",
                table: "attempts",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DiagnosticsJson",
                schema: "crawler",
                table: "attempts");

            migrationBuilder.DropColumn(
                name: "DiagnosticsSequence",
                schema: "crawler",
                table: "attempts");

            migrationBuilder.DropColumn(
                name: "DiagnosticsUpdatedAt",
                schema: "crawler",
                table: "attempts");

            migrationBuilder.DropColumn(
                name: "DiagnosticsVersion",
                schema: "crawler",
                table: "attempts");
        }
    }
}

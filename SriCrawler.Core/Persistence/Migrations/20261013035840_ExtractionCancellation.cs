using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SriCrawler.Core.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ExtractionCancellation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CancellationRequestedAt",
                schema: "crawler",
                table: "extractions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CancellationRequestedBy",
                schema: "crawler",
                table: "extractions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CancelledAt",
                schema: "crawler",
                table: "extractions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_extractions_pending_cancellation",
                schema: "crawler",
                table: "extractions",
                columns: new[] { "CancellationRequestedAt", "Id" },
                filter: "\"JobStatus\" = 5");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_extractions_pending_cancellation",
                schema: "crawler",
                table: "extractions");

            migrationBuilder.DropColumn(
                name: "CancellationRequestedAt",
                schema: "crawler",
                table: "extractions");

            migrationBuilder.DropColumn(
                name: "CancellationRequestedBy",
                schema: "crawler",
                table: "extractions");

            migrationBuilder.DropColumn(
                name: "CancelledAt",
                schema: "crawler",
                table: "extractions");
        }
    }
}

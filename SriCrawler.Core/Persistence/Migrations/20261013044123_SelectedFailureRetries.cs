using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SriCrawler.Core.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SelectedFailureRetries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RetryMode",
                schema: "crawler",
                table: "extractions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RetrySelectionJson",
                schema: "crawler",
                table: "extractions",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SourceExtractionId",
                schema: "crawler",
                table: "extractions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_extractions_SourceExtractionId",
                schema: "crawler",
                table: "extractions",
                column: "SourceExtractionId");

            migrationBuilder.AddForeignKey(
                name: "FK_extractions_extractions_SourceExtractionId",
                schema: "crawler",
                table: "extractions",
                column: "SourceExtractionId",
                principalSchema: "crawler",
                principalTable: "extractions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_extractions_extractions_SourceExtractionId",
                schema: "crawler",
                table: "extractions");

            migrationBuilder.DropIndex(
                name: "IX_extractions_SourceExtractionId",
                schema: "crawler",
                table: "extractions");

            migrationBuilder.DropColumn(
                name: "RetryMode",
                schema: "crawler",
                table: "extractions");

            migrationBuilder.DropColumn(
                name: "RetrySelectionJson",
                schema: "crawler",
                table: "extractions");

            migrationBuilder.DropColumn(
                name: "SourceExtractionId",
                schema: "crawler",
                table: "extractions");
        }
    }
}

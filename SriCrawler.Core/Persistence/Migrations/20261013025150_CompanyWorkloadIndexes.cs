using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SriCrawler.Core.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CompanyWorkloadIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_extractions_CompanyId_CreatedAt",
                schema: "crawler",
                table: "extractions",
                columns: new[] { "CompanyId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_extractions_CompanyId_JobStatus_ExpiresAt",
                schema: "crawler",
                table: "extractions",
                columns: new[] { "CompanyId", "JobStatus", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_dispatches_DispatchedAt_ExtractionId",
                schema: "crawler",
                table: "dispatches",
                columns: new[] { "DispatchedAt", "ExtractionId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_extractions_CompanyId_CreatedAt",
                schema: "crawler",
                table: "extractions");

            migrationBuilder.DropIndex(
                name: "IX_extractions_CompanyId_JobStatus_ExpiresAt",
                schema: "crawler",
                table: "extractions");

            migrationBuilder.DropIndex(
                name: "IX_dispatches_DispatchedAt_ExtractionId",
                schema: "crawler",
                table: "dispatches");
        }
    }
}

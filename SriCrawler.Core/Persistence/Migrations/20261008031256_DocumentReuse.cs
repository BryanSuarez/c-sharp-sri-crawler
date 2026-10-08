using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SriCrawler.Core.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DocumentReuse : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AcquisitionSource",
                schema: "crawler",
                table: "results",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "StorageRevision",
                schema: "crawler",
                table: "files",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "StoredAt",
                schema: "crawler",
                table: "files",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ValidationProfile",
                schema: "crawler",
                table: "files",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DownloadPolicy",
                schema: "crawler",
                table: "extractions",
                type: "integer",
                nullable: false,
                defaultValue: 1);
            // Historical downloaded results originated from an SRI download within their own job.
            migrationBuilder.Sql("UPDATE crawler.results SET \"AcquisitionSource\" = 1 WHERE \"DownloadStatus\" = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AcquisitionSource",
                schema: "crawler",
                table: "results");

            migrationBuilder.DropColumn(
                name: "StorageRevision",
                schema: "crawler",
                table: "files");

            migrationBuilder.DropColumn(
                name: "StoredAt",
                schema: "crawler",
                table: "files");

            migrationBuilder.DropColumn(
                name: "ValidationProfile",
                schema: "crawler",
                table: "files");

            migrationBuilder.DropColumn(
                name: "DownloadPolicy",
                schema: "crawler",
                table: "extractions");
        }
    }
}

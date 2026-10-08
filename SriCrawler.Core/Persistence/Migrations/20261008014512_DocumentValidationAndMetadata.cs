using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SriCrawler.Core.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DocumentValidationAndMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "Amount",
                schema: "crawler",
                table: "results",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AuthorizedAt",
                schema: "crawler",
                table: "results",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FileHash",
                schema: "crawler",
                table: "results",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "IssuedDate",
                schema: "crawler",
                table: "results",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MetadataParseStatus",
                schema: "crawler",
                table: "results",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "SourceValuesJson",
                schema: "crawler",
                table: "results",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StorageStatus",
                schema: "crawler",
                table: "results",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "Taxes",
                schema: "crawler",
                table: "results",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Total",
                schema: "crawler",
                table: "results",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ValidationJson",
                schema: "crawler",
                table: "results",
                type: "jsonb",
                nullable: false,
                defaultValue: "{}");

            migrationBuilder.AddColumn<int>(
                name: "ValidationStatus",
                schema: "crawler",
                table: "results",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Sha256",
                schema: "crawler",
                table: "files",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "SizeBytes",
                schema: "crawler",
                table: "files",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ValidationJson",
                schema: "crawler",
                table: "files",
                type: "jsonb",
                nullable: false,
                defaultValue: "{}");

            migrationBuilder.AddColumn<int>(
                name: "ValidationStatus",
                schema: "crawler",
                table: "files",
                type: "integer",
                nullable: false,
                defaultValue: 0);
            // Existing successful results confirm storage, but do not prove file or metadata validity.
            migrationBuilder.Sql("UPDATE crawler.results SET \"StorageStatus\" = 1 WHERE \"DownloadStatus\" = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Amount",
                schema: "crawler",
                table: "results");

            migrationBuilder.DropColumn(
                name: "AuthorizedAt",
                schema: "crawler",
                table: "results");

            migrationBuilder.DropColumn(
                name: "FileHash",
                schema: "crawler",
                table: "results");

            migrationBuilder.DropColumn(
                name: "IssuedDate",
                schema: "crawler",
                table: "results");

            migrationBuilder.DropColumn(
                name: "MetadataParseStatus",
                schema: "crawler",
                table: "results");

            migrationBuilder.DropColumn(
                name: "SourceValuesJson",
                schema: "crawler",
                table: "results");

            migrationBuilder.DropColumn(
                name: "StorageStatus",
                schema: "crawler",
                table: "results");

            migrationBuilder.DropColumn(
                name: "Taxes",
                schema: "crawler",
                table: "results");

            migrationBuilder.DropColumn(
                name: "Total",
                schema: "crawler",
                table: "results");

            migrationBuilder.DropColumn(
                name: "ValidationJson",
                schema: "crawler",
                table: "results");

            migrationBuilder.DropColumn(
                name: "ValidationStatus",
                schema: "crawler",
                table: "results");

            migrationBuilder.DropColumn(
                name: "Sha256",
                schema: "crawler",
                table: "files");

            migrationBuilder.DropColumn(
                name: "SizeBytes",
                schema: "crawler",
                table: "files");

            migrationBuilder.DropColumn(
                name: "ValidationJson",
                schema: "crawler",
                table: "files");

            migrationBuilder.DropColumn(
                name: "ValidationStatus",
                schema: "crawler",
                table: "files");
        }
    }
}

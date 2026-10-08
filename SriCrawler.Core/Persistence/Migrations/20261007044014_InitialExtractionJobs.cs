using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace DescagaCompronanteSRI.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialExtractionJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "crawler");

            migrationBuilder.CreateTable(
                name: "documents",
                schema: "crawler",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TaxpayerId = table.Column<string>(type: "character varying(13)", maxLength: 13, nullable: false),
                    Direction = table.Column<int>(type: "integer", nullable: false),
                    DocumentType = table.Column<int>(type: "integer", nullable: false),
                    AccessKey = table.Column<string>(type: "character varying(49)", maxLength: 49, nullable: false),
                    IssuerTaxpayerId = table.Column<string>(type: "character varying(13)", maxLength: 13, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_documents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "extractions",
                schema: "crawler",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TaxpayerId = table.Column<string>(type: "character varying(13)", maxLength: 13, nullable: false),
                    ClientRequestId = table.Column<Guid>(type: "uuid", nullable: true),
                    Fingerprint = table.Column<string>(type: "text", nullable: false),
                    QueryJson = table.Column<string>(type: "jsonb", nullable: false),
                    EncryptedPassword = table.Column<string>(type: "text", nullable: true),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    JobStatus = table.Column<int>(type: "integer", nullable: false),
                    Stage = table.Column<int>(type: "integer", nullable: false),
                    ExtractionStatus = table.Column<int>(type: "integer", nullable: true),
                    QuerySucceeded = table.Column<bool>(type: "boolean", nullable: false),
                    BusinessName = table.Column<string>(type: "text", nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    ActiveAttemptId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FinishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastActivityAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PaginationJson = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_extractions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "conversions",
                schema: "crawler",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DocumentId = table.Column<long>(type: "bigint", nullable: false),
                    XmlHash = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    DocumentJson = table.Column<string>(type: "jsonb", nullable: true),
                    ParserName = table.Column<string>(type: "text", nullable: true),
                    ParserVersion = table.Column<string>(type: "text", nullable: true),
                    SchemaVersion = table.Column<int>(type: "integer", nullable: false),
                    ErrorCode = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_conversions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_conversions_documents_DocumentId",
                        column: x => x.DocumentId,
                        principalSchema: "crawler",
                        principalTable: "documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "files",
                schema: "crawler",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DocumentId = table.Column<long>(type: "bigint", nullable: false),
                    Format = table.Column<int>(type: "integer", nullable: false),
                    Year = table.Column<int>(type: "integer", nullable: false),
                    Month = table.Column<int>(type: "integer", nullable: false),
                    StorageJson = table.Column<string>(type: "jsonb", nullable: false),
                    LocalPath = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_files", x => x.Id);
                    table.ForeignKey(
                        name: "FK_files_documents_DocumentId",
                        column: x => x.DocumentId,
                        principalSchema: "crawler",
                        principalTable: "documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "attempts",
                schema: "crawler",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ExtractionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<int>(type: "integer", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FinishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ErrorCode = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_attempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_attempts_extractions_ExtractionId",
                        column: x => x.ExtractionId,
                        principalSchema: "crawler",
                        principalTable: "extractions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "dispatches",
                schema: "crawler",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ExtractionId = table.Column<Guid>(type: "uuid", nullable: false),
                    HangfireJobId = table.Column<string>(type: "text", nullable: true),
                    DispatchedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_dispatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_dispatches_extractions_ExtractionId",
                        column: x => x.ExtractionId,
                        principalSchema: "crawler",
                        principalTable: "extractions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "failures",
                schema: "crawler",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ExtractionId = table.Column<Guid>(type: "uuid", nullable: false),
                    AttemptId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ErrorJson = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_failures", x => x.Id);
                    table.ForeignKey(
                        name: "FK_failures_extractions_ExtractionId",
                        column: x => x.ExtractionId,
                        principalSchema: "crawler",
                        principalTable: "extractions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "results",
                schema: "crawler",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ExtractionId = table.Column<Guid>(type: "uuid", nullable: false),
                    SeenAttemptId = table.Column<Guid>(type: "uuid", nullable: false),
                    Identity = table.Column<string>(type: "text", nullable: false),
                    DocumentId = table.Column<long>(type: "bigint", nullable: true),
                    FileId = table.Column<long>(type: "bigint", nullable: true),
                    ConversionId = table.Column<long>(type: "bigint", nullable: true),
                    DownloadStatus = table.Column<int>(type: "integer", nullable: false),
                    JsonStatus = table.Column<int>(type: "integer", nullable: false),
                    ResponseJson = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_results", x => x.Id);
                    table.ForeignKey(
                        name: "FK_results_conversions_ConversionId",
                        column: x => x.ConversionId,
                        principalSchema: "crawler",
                        principalTable: "conversions",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_results_documents_DocumentId",
                        column: x => x.DocumentId,
                        principalSchema: "crawler",
                        principalTable: "documents",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_results_extractions_ExtractionId",
                        column: x => x.ExtractionId,
                        principalSchema: "crawler",
                        principalTable: "extractions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_results_files_FileId",
                        column: x => x.FileId,
                        principalSchema: "crawler",
                        principalTable: "files",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_attempts_ExtractionId_Number",
                schema: "crawler",
                table: "attempts",
                columns: new[] { "ExtractionId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_conversions_DocumentId_XmlHash_ParserVersion",
                schema: "crawler",
                table: "conversions",
                columns: new[] { "DocumentId", "XmlHash", "ParserVersion" });

            migrationBuilder.CreateIndex(
                name: "IX_dispatches_ExtractionId",
                schema: "crawler",
                table: "dispatches",
                column: "ExtractionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_documents_CompanyId_TaxpayerId_Direction_DocumentType_Acces~",
                schema: "crawler",
                table: "documents",
                columns: new[] { "CompanyId", "TaxpayerId", "Direction", "DocumentType", "AccessKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_extractions_CompanyId_ClientRequestId",
                schema: "crawler",
                table: "extractions",
                columns: new[] { "CompanyId", "ClientRequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_extractions_CompanyId_TaxpayerId_CreatedAt",
                schema: "crawler",
                table: "extractions",
                columns: new[] { "CompanyId", "TaxpayerId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_failures_ExtractionId_Id",
                schema: "crawler",
                table: "failures",
                columns: new[] { "ExtractionId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_files_DocumentId_Format_Year_Month",
                schema: "crawler",
                table: "files",
                columns: new[] { "DocumentId", "Format", "Year", "Month" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_files_Year_Month",
                schema: "crawler",
                table: "files",
                columns: new[] { "Year", "Month" });

            migrationBuilder.CreateIndex(
                name: "IX_results_ConversionId",
                schema: "crawler",
                table: "results",
                column: "ConversionId");

            migrationBuilder.CreateIndex(
                name: "IX_results_DocumentId",
                schema: "crawler",
                table: "results",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_results_ExtractionId_Identity",
                schema: "crawler",
                table: "results",
                columns: new[] { "ExtractionId", "Identity" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_results_FileId",
                schema: "crawler",
                table: "results",
                column: "FileId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "attempts",
                schema: "crawler");

            migrationBuilder.DropTable(
                name: "dispatches",
                schema: "crawler");

            migrationBuilder.DropTable(
                name: "failures",
                schema: "crawler");

            migrationBuilder.DropTable(
                name: "results",
                schema: "crawler");

            migrationBuilder.DropTable(
                name: "conversions",
                schema: "crawler");

            migrationBuilder.DropTable(
                name: "extractions",
                schema: "crawler");

            migrationBuilder.DropTable(
                name: "files",
                schema: "crawler");

            migrationBuilder.DropTable(
                name: "documents",
                schema: "crawler");
        }
    }
}

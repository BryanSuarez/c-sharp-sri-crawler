using DescagaCompronanteSRI.Jobs;
using DescagaCompronanteSRI.Models.Enums;
using DescagaCompronanteSRI.Models.Responses;
using DescagaCompronanteSRI.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace DescagaCompronanteSRI.Tests.Jobs;

[Collection("HangfireHost")]
public sealed class MigrationCompatibilityTests
{
    [PostgresFact]
    public async Task AdditiveMigration_KeepsOldPoliciesAndSnapshotsWithoutInventingValidation()
    {
        var source = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("SRI_TEST_POSTGRES")!);
        var name = "reuse_migration_" + Guid.NewGuid().ToString("N");
        await using var administration = new NpgsqlConnection(source.ConnectionString);
        await administration.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE DATABASE {name}", administration)) await create.ExecuteNonQueryAsync();
        try
        {
            var target = new NpgsqlConnectionStringBuilder(source.ConnectionString) { Database = name, Pooling = false };
            await using var db = new CrawlerDbContext(new DbContextOptionsBuilder<CrawlerDbContext>().UseNpgsql(target.ConnectionString).Options);
            await db.GetService<IMigrator>().MigrateAsync("20261008014512_DocumentValidationAndMetadata");
            var id = Guid.NewGuid(); var attempt = Guid.NewGuid();
            const string empty = "{}";
            const string snapshot = "{\"downloadStatus\":\"downloaded\"}";
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO crawler.extractions ("Id", "CompanyId", "TaxpayerId", "Fingerprint", "QueryJson", "ExpiresAt",
                    "JobStatus", "Stage", "BusinessName", "AttemptCount", "CreatedAt", "PaginationJson", "QuerySucceeded", "ActiveAttemptId")
                VALUES ({id}, 'legacy', '1790012345001', 'original-fingerprint', {empty}::jsonb, NOW(), 3, 5, '', 1, NOW(), {empty}::jsonb, true, {attempt})
                """);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO crawler.results ("ExtractionId", "SeenAttemptId", "Identity", "DownloadStatus", "JsonStatus", "ResponseJson",
                    "ValidationStatus", "StorageStatus", "MetadataParseStatus", "ValidationJson")
                VALUES ({id}, {attempt}, 'legacy-key', 0, 3, {snapshot}::jsonb, 0, 0, 0, {empty}::jsonb)
                """);
            await db.Database.MigrateAsync();
            var extraction = await db.Extractions.SingleAsync(); var result = await db.Results.SingleAsync();
            Assert.Equal(DownloadPolicy.Refresh, extraction.DownloadPolicy);
            Assert.Equal("original-fingerprint", extraction.Fingerprint);
            Assert.Equal(DocumentAcquisitionSource.Downloaded, result.AcquisitionSource);
            Assert.Equal(DocumentValidationStatus.NotChecked, result.ValidationStatus);
            Assert.Equal(MetadataParseStatus.NotChecked, result.MetadataParseStatus);
            var old = ExtractionJobs.Deserialize<ReceivedDocumentResponse>(result.ResponseJson);
            Assert.Equal(DocumentAcquisitionSource.Downloaded, old.AcquisitionSource);
            Assert.Equal(DocumentValidationStatus.NotChecked, old.Validation.Status);
            Assert.DoesNotContain("acquisitionSource", result.ResponseJson);
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP DATABASE {name} WITH (FORCE)", administration);
            await drop.ExecuteNonQueryAsync();
        }
    }
}

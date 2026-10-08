using Microsoft.EntityFrameworkCore;

namespace DescagaCompronanteSRI.Persistence;

public sealed class CrawlerDbContext(DbContextOptions<CrawlerDbContext> options) : DbContext(options)
{
    public DbSet<ExtractionRecord> Extractions => Set<ExtractionRecord>();
    public DbSet<ExtractionAttempt> Attempts => Set<ExtractionAttempt>();
    public DbSet<DocumentRecord> Documents => Set<DocumentRecord>();
    public DbSet<DocumentFile> Files => Set<DocumentFile>();
    public DbSet<DocumentConversion> Conversions => Set<DocumentConversion>();
    public DbSet<ExtractionDocument> Results => Set<ExtractionDocument>();
    public DbSet<ExtractionFailure> Failures => Set<ExtractionFailure>();
    public DbSet<JobDispatch> Dispatches => Set<JobDispatch>();
    protected override void OnModelCreating(ModelBuilder model)
    {
        model.HasDefaultSchema("crawler");
        model.Entity<ExtractionRecord>().ToTable("extractions");
        model.Entity<ExtractionRecord>().HasIndex(e => new { e.CompanyId, e.ClientRequestId }).IsUnique();
        model.Entity<ExtractionRecord>().HasIndex(e => new { e.CompanyId, e.TaxpayerId, e.CreatedAt });
        model.Entity<ExtractionAttempt>().ToTable("attempts");
        model.Entity<ExtractionAttempt>().HasIndex(e => new { e.ExtractionId, e.Number }).IsUnique();
        model.Entity<DocumentRecord>().ToTable("documents");
        model.Entity<DocumentRecord>().HasIndex(d => new { d.CompanyId, d.TaxpayerId, d.Direction, d.DocumentType, d.AccessKey }).IsUnique();
        model.Entity<DocumentFile>().ToTable("files");
        model.Entity<DocumentFile>().HasIndex(d => new { d.DocumentId, d.Format, d.Year, d.Month }).IsUnique();
        model.Entity<DocumentFile>().HasIndex(d => new { d.Year, d.Month });
        model.Entity<DocumentConversion>().ToTable("conversions");
        model.Entity<DocumentConversion>().HasIndex(d => new { d.DocumentId, d.XmlHash, d.ParserVersion });
        model.Entity<ExtractionDocument>().ToTable("results");
        model.Entity<ExtractionDocument>().HasIndex(d => new { d.ExtractionId, d.Identity }).IsUnique();
        model.Entity<ExtractionFailure>().ToTable("failures");
        model.Entity<ExtractionFailure>().HasIndex(d => new { d.ExtractionId, d.Id });
        model.Entity<JobDispatch>().ToTable("dispatches");
        model.Entity<JobDispatch>().HasIndex(d => d.ExtractionId).IsUnique();
        model.Entity<ExtractionAttempt>().HasOne<ExtractionRecord>().WithMany().HasForeignKey(x => x.ExtractionId);
        model.Entity<ExtractionDocument>().HasOne<ExtractionRecord>().WithMany().HasForeignKey(x => x.ExtractionId);
        model.Entity<ExtractionFailure>().HasOne<ExtractionRecord>().WithMany().HasForeignKey(x => x.ExtractionId);
        model.Entity<JobDispatch>().HasOne<ExtractionRecord>().WithMany().HasForeignKey(x => x.ExtractionId);
        model.Entity<DocumentFile>().HasOne<DocumentRecord>().WithMany().HasForeignKey(x => x.DocumentId);
        model.Entity<DocumentConversion>().HasOne<DocumentRecord>().WithMany().HasForeignKey(x => x.DocumentId);
        model.Entity<ExtractionDocument>().HasOne<DocumentRecord>().WithMany().HasForeignKey(x => x.DocumentId);
        model.Entity<ExtractionDocument>().HasOne<DocumentFile>().WithMany().HasForeignKey(x => x.FileId);
        model.Entity<ExtractionDocument>().HasOne<DocumentConversion>().WithMany().HasForeignKey(x => x.ConversionId);
        foreach (var entity in model.Model.GetEntityTypes())
            foreach (var property in entity.GetProperties())
            {
                if (property.Name.EndsWith("Json")) property.SetColumnType("jsonb");
                if (property.Name is "CompanyId") property.SetMaxLength(64);
                if (property.Name is "TaxpayerId" or "IssuerTaxpayerId") property.SetMaxLength(13);
                if (property.Name is "AccessKey") property.SetMaxLength(49);
            }
    }
}

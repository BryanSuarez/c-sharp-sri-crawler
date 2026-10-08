using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DescagaCompronanteSRI.Persistence;

public sealed class CrawlerDbContextFactory : IDesignTimeDbContextFactory<CrawlerDbContext>
{
    public CrawlerDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("ExtractionJobs__ConnectionString")
            ?? throw new InvalidOperationException("Set ExtractionJobs__ConnectionString before running database migrations.");
        return new(new DbContextOptionsBuilder<CrawlerDbContext>().UseNpgsql(connection).Options);
    }
}

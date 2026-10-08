using DescagaCompronanteSRI.Diagnostics;
using Microsoft.Extensions.Logging;
using DescagaCompronanteSRI.Jobs;
using DescagaCompronanteSRI.Services.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Npgsql;

var builder = Host.CreateApplicationBuilder(args);
StorageConfiguration.AddStorageEnvironmentFile(builder.Configuration, builder.Environment);
JobRegistration.AddJobEnvironmentFile(builder.Configuration, builder.Environment);
builder.Logging.AddExtractionJsonConsole();
builder.Services.AddReceivedCrawler();
builder.Services.AddExtractionJobs(builder.Configuration, worker: true);
builder.Services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(45));
using var host = builder.Build();
if (args.Contains("--healthcheck"))
{
    try
    {
        var connectionString = host.Services.GetRequiredService<IOptions<ExtractionJobOptions>>().Value.ConnectionString;
        await using var connection = new NpgsqlConnection(connectionString);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        await connection.OpenAsync(timeout.Token);
        await using var command = new NpgsqlCommand("SELECT EXISTS (SELECT 1 FROM hangfire.server WHERE id LIKE @server AND lastheartbeat > NOW() - INTERVAL '2 minutes')", connection);
        command.Parameters.AddWithValue("server", Environment.MachineName.ToLowerInvariant() + ":%");
        Environment.ExitCode = Equals(await command.ExecuteScalarAsync(timeout.Token), true) ? 0 : 1;
    }
    catch { Environment.ExitCode = 1; }
    return;
}
await host.RunAsync();

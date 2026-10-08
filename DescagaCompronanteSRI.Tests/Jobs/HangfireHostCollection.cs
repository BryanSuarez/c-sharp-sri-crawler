namespace DescagaCompronanteSRI.Tests.Jobs;

// Hangfire configures process-global activation/logging; isolate test host lifetimes.
[CollectionDefinition("HangfireHost", DisableParallelization = true)]
public sealed class HangfireHostCollection;

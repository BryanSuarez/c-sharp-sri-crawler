using System.Reflection;
using DescagaCompronanteSRI.Contracts;
using DescagaCompronanteSRI.Helpers;
using DescagaCompronanteSRI.Models.Dtos;
using DescagaCompronanteSRI.Services.ReceivedDocuments;
using Microsoft.Playwright;

namespace DescagaCompronanteSRI.Tests.ReceivedDocuments;

public sealed class ReceivedDocumentsSessionCancellationTests
{
    [Fact]
    public async Task ClosingDuringStartup_WaitsForLateBrowserAndNeverLogsIn()
    {
        var factory = new DelayedFactory(); var login = new Login();
        var session = new ReceivedDocumentsSession(factory, login, null!);
        var started = session.LoginAsync(Query());
        var closing = session.DisposeAsync().AsTask();
        Assert.False(closing.IsCompleted);
        var page = DispatchProxy.Create<IPage, PageProxy>();
        var browser = new PlaywrightSession();
        typeof(PlaywrightSession).GetProperty(nameof(PlaywrightSession.Page))!.SetValue(browser, page);
        factory.Created.SetResult(browser);
        await closing.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => started);
        await session.DisposeAsync();
        Assert.Equal(1, ((PageProxy)(object)page).CloseCount); Assert.Equal(0, login.Calls);
    }

    [Fact]
    public async Task FailedStartup_DoesNotMaskTheOriginalFailureDuringCleanup()
    {
        var factory = new DelayedFactory(); var session = new ReceivedDocumentsSession(factory, new Login(), null!);
        var started = session.LoginAsync(Query()); var closing = session.DisposeAsync().AsTask();
        factory.Created.SetException(new IOException("Synthetic startup failure."));
        await Assert.ThrowsAsync<IOException>(() => started); await closing;
    }

    [Fact]
    public async Task ClosingBeforeLogin_DoesNotStartBrowser()
    {
        var factory = new DelayedFactory(); var session = new ReceivedDocumentsSession(factory, new Login(), null!);
        await session.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => session.LoginAsync(Query()));
        Assert.Equal(0, factory.Calls);
    }

    private static ReceivedDocumentsQuery Query() => new() { CompanyId = "fixture", User = "1790012345001", Password = "fixture-only" };
    private sealed class DelayedFactory : IPlaywrightSessionFactory
    {
        public int Calls { get; private set; }
        public TaskCompletionSource<PlaywrightSession> Created { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<PlaywrightSession> CreateAsync() { Calls++; return Created.Task; }
    }
    private sealed class Login : ISriLoginService
    {
        public int Calls { get; private set; }
        public Task<SriUserProfile?> LoginAsync(PlaywrightSession session, string user, string password, string? additionalUser)
        { Calls++; return Task.FromResult<SriUserProfile?>(null); }
    }
    public class PageProxy : DispatchProxy
    {
        public int CloseCount { get; private set; }
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod!.Name == nameof(IPage.CloseAsync)) { CloseCount++; return Task.CompletedTask; }
            throw new NotSupportedException();
        }
    }
}

using DescagaCompronanteSRI.Diagnostics;
using DescagaCompronanteSRI.Contracts;
using DescagaCompronanteSRI.Helpers;
using DescagaCompronanteSRI.Models.Dtos;
using Microsoft.Playwright;

namespace DescagaCompronanteSRI.Services.ReceivedDocuments;

public sealed class ReceivedDocumentsSessionFactory(
    IPlaywrightSessionFactory browserFactory, ISriLoginService loginService,
    ISriPortalSessionService portalService) : IReceivedDocumentsSessionFactory
{
    public IReceivedDocumentsSession Create() =>
        new ReceivedDocumentsSession(browserFactory, loginService, portalService);
}

internal sealed class ReceivedDocumentsSession(
    IPlaywrightSessionFactory browserFactory, ISriLoginService loginService,
    ISriPortalSessionService portalService) : IReceivedDocumentsSession
{
    private readonly object lifecycle = new();
    private Task<PlaywrightSession>? creation;
    private Task? disposal;
    private bool disposed;
    private PlaywrightSession? _session;
    public IPage Page => Session.Page;
    private PlaywrightSession Session => _session ?? throw new InvalidOperationException("Session is not initialized.");

    public async Task<SriUserProfile?> LoginAsync(ReceivedDocumentsQuery query)
    {
        Task<PlaywrightSession> pending;
        lock (lifecycle)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            pending = creation ??= browserFactory.CreateAsync();
        }
        var created = await pending;
        lock (lifecycle)
        {
            if (disposed) throw new OperationCanceledException("The session was closed during browser startup.");
            _session = created;
        }
        return await ExtractionDiagnostics.MeasureAsync(DiagnosticOperation.Authentication,
            () => loginService.LoginAsync(Session, query.User, query.Password, query.AdditionalUser),
            value => value is null ? DiagnosticOutcome.Failed : DiagnosticOutcome.Succeeded);
    }

    public Task<string> GetPortalBodyAsync() => portalService.GetPortalBodyAsync(
        Session,
        "https://srienlinea.sri.gob.ec/tuportal-internet/accederAplicacion.jspa?redireccion=57&idGrupo=55",
        "https://srienlinea.sri.gob.ec/tuportal-internet/menusFavoritos.jspa?redireccion=57&idGrupo=55",
        "[ReceivedPortal]");

    public Task SubmitPortalFormAsync(string body) => portalService.SubmitJSecurityCheckAsync(Session, body);
    public Task CloseModalAsync() => Session.CerrarModalAsync();
    public ValueTask DisposeAsync()
    {
        lock (lifecycle)
        {
            disposed = true;
            return new(disposal ??= DisposeCreatedSessionAsync());
        }
    }

    private async Task DisposeCreatedSessionAsync()
    {
        // Wait for startup to settle so a late browser cannot escape cancellation cleanup.
        if (creation is null) return;
        PlaywrightSession created;
        try { created = await creation; }
        catch { return; /* The factory cleans up failed startup. */ }
        await ExtractionDiagnostics.MeasureAsync(DiagnosticOperation.SessionCleanup, () => created.DisposeAsync().AsTask());
    }
}

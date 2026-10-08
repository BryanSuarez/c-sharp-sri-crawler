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
    private PlaywrightSession? _session;
    public IPage Page => Session.Page;
    private PlaywrightSession Session => _session ?? throw new InvalidOperationException("Session is not initialized.");

    public async Task<SriUserProfile?> LoginAsync(ReceivedDocumentsQuery query)
    {
        _session = await browserFactory.CreateAsync();
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
    public async ValueTask DisposeAsync()
    {
        if (_session is not null) await ExtractionDiagnostics.MeasureAsync(DiagnosticOperation.SessionCleanup, () => _session.DisposeAsync().AsTask());
    }
}

using System.Collections.Concurrent;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Playwright;

namespace DescagaCompronanteSRI.Services.ReceivedDocuments;

// Observe only the JSF request caused by this action, never document download requests.
internal sealed class ReceivedDocumentsPortalAction : IDisposable
{
    private readonly IPage page;
    private readonly string source;
    private readonly ConcurrentDictionary<IRequest, byte> pending = new();
    private readonly ConcurrentQueue<IRequest> finished = new();
    public bool ResponseCompleted { get; private set; }
    public bool HasPendingRequest => !pending.IsEmpty;
    public bool ResponseFailed { get; private set; }

    public ReceivedDocumentsPortalAction(IPage page, string source)
    {
        this.page = page;
        this.source = source;
        page.Request += OnRequest;
        page.RequestFinished += OnFinished;
        page.RequestFailed += OnFailed;
    }

    private void OnRequest(object? sender, IRequest request)
    {
        if (request.Method != "POST" || request.Frame != page.MainFrame ||
            !Uri.TryCreate(request.Url, UriKind.Absolute, out var uri) ||
            uri.GetLeftPart(UriPartial.Path) != ReceivedDocumentsPage.Url) return;
        var form = QueryHelpers.ParseQuery(request.PostData ?? "");
        if ((form.TryGetValue("javax.faces.source", out var value) && value == source) ||
            form.ContainsKey(source) || form.ContainsKey(source + "_pagination"))
            pending.TryAdd(request, 0);
    }

    private void OnFinished(object? sender, IRequest request)
    {
        if (pending.TryRemove(request, out _)) finished.Enqueue(request);
    }

    private void OnFailed(object? sender, IRequest request)
    {
        if (pending.TryRemove(request, out _)) ResponseFailed = true;
    }

    public async Task RefreshAsync()
    {
        while (finished.TryDequeue(out var request))
        {
            var response = await request.ResponseAsync();
            if (response is not { Status: >= 200 and < 300 })
            {
                ResponseFailed = true;
                continue;
            }
            var body = await response.TextAsync();
            if (System.Text.RegularExpressions.Regex.IsMatch(body, @"<(?:error|redirect)(?:\s|>)", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                ResponseFailed = true;
            else ResponseCompleted = true;
        }
    }

    public void Reset()
    {
        ResponseCompleted = false;
        ResponseFailed = false;
    }

    public void Dispose()
    {
        page.Request -= OnRequest;
        page.RequestFinished -= OnFinished;
        page.RequestFailed -= OnFailed;
    }
}

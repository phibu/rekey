using System.Net;

namespace PassReset.Tests.Windows.Fakes;

/// <summary>
/// Deterministic <see cref="HttpMessageHandler"/> used to drive <see cref="HttpClient"/>
/// in unit tests without touching the network.
/// </summary>
public sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
    public List<HttpRequestMessage> Requests { get; } = new();

    public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) =>
        _responder = responder;

    public FakeHttpMessageHandler(HttpStatusCode status, string body)
        : this(_ => new HttpResponseMessage(status) { Content = new StringContent(body) }) { }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Requests.Add(request);
        return Task.FromResult(_responder(request));
    }
}

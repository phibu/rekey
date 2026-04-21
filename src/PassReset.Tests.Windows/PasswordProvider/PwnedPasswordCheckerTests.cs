using System.Net;
using PassReset.PasswordProvider;
using PassReset.Tests.Windows.Fakes;

namespace PassReset.Tests.Windows.PasswordProvider;

/// <summary>
/// Exercises the HIBP k-anonymity client end-to-end against a <see cref="FakeHttpMessageHandler"/>
/// so the parsing, URL shape, and failure modes are covered without network I/O.
/// </summary>
public class PwnedPasswordCheckerTests
{
    private const string Password = "hunter2";
    // SHA-1 of "hunter2" = F3BBBD66A63D4BF1747940578EC3D0103530E21D
    private const string Sha1Prefix = "F3BBB";
    private const string Sha1Suffix = "D66A63D4BF1747940578EC3D0103530E21D";

    private static PwnedPasswordChecker BuildChecker(FakeHttpMessageHandler handler)
    {
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.pwnedpasswords.com/"),
        };
        return new PwnedPasswordChecker(client);
    }

    [Fact]
    public async Task KnownPwned_ReturnsTrue()
    {
        var body = $"{Sha1Suffix}:42\nOTHERSUFFIX:3";
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, body);
        var checker = BuildChecker(handler);

        var result = await checker.IsPwnedPasswordAsync(Password);

        Assert.True(result);
    }

    [Fact]
    public async Task NotPwned_ReturnsFalse()
    {
        var body = "OTHERSUFFIX:3\nANOTHER:99";
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, body);
        var checker = BuildChecker(handler);

        var result = await checker.IsPwnedPasswordAsync(Password);

        Assert.False(result);
    }

    [Fact]
    public async Task EmptyBody_ReturnsFalse()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, string.Empty);
        var checker = BuildChecker(handler);

        Assert.False(await checker.IsPwnedPasswordAsync(Password));
    }

    [Fact]
    public async Task ServerError_ReturnsNullToSurfaceDistinctError()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.InternalServerError, string.Empty);
        var checker = BuildChecker(handler);

        var result = await checker.IsPwnedPasswordAsync(Password);

        Assert.Null(result);
    }

    [Fact]
    public async Task NetworkFailure_ReturnsNull()
    {
        var handler = new FakeHttpMessageHandler(_ => throw new HttpRequestException("DNS fail"));
        var checker = BuildChecker(handler);

        var result = await checker.IsPwnedPasswordAsync(Password);

        Assert.Null(result);
    }

    [Fact]
    public async Task RequestUsesKAnonymityPrefix()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, string.Empty);
        var checker = BuildChecker(handler);

        await checker.IsPwnedPasswordAsync(Password);

        Assert.Single(handler.Requests);
        var url = handler.Requests[0].RequestUri!.ToString();
        Assert.Contains($"range/{Sha1Prefix}", url, StringComparison.Ordinal);
        // Suffix must NOT be leaked to the upstream API.
        Assert.DoesNotContain(Sha1Suffix, url, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SuffixMatch_IsCaseInsensitive()
    {
        var body = $"{Sha1Suffix.ToLowerInvariant()}:7";
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, body);
        var checker = BuildChecker(handler);

        var result = await checker.IsPwnedPasswordAsync(Password);

        Assert.True(result);
    }

    [Fact]
    public async Task Disabled_ReturnsFalseWithoutHttpCall()
    {
        // When constructed with disabled: true, IsPwnedPasswordAsync must short-circuit
        // to false (not pwned) without issuing any HTTP request.
        using var handler = new RecordingHandler();
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.pwnedpasswords.com/") };
        var sut = new PwnedPasswordChecker(client, disabled: true);

        var result = await sut.IsPwnedPasswordAsync("irrelevant");

        Assert.Equal(false, result);
        Assert.Equal(0, handler.CallCount);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("") });
        }
    }
}

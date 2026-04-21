using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using PassReset.Common;
using PassReset.Common.LocalPolicy;
using Xunit;

namespace PassReset.Tests.LocalPolicy;

public sealed class LocalPolicyPasswordChangeProviderTests : IDisposable
{
    private readonly string _tempDir;
    private readonly IPasswordChangeProvider _inner = Substitute.For<IPasswordChangeProvider>();

    public LocalPolicyPasswordChangeProviderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "passreset-policy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
    }

    private LocalPolicyOptions MakeOptions(string? bannedPath = null, string? pwnedPath = null) =>
        new() { BannedWordsPath = bannedPath, LocalPwnedPasswordsPath = pwnedPath };

    private LocalPolicyPasswordChangeProvider BuildSut(LocalPolicyOptions opts)
    {
        var banned = new BannedWordsChecker(opts, NullLogger<BannedWordsChecker>.Instance);
        var pwned = new LocalPwnedPasswordsChecker(opts, NullLogger<LocalPwnedPasswordsChecker>.Instance);
        return new LocalPolicyPasswordChangeProvider(_inner, banned, pwned,
            NullLogger<LocalPolicyPasswordChangeProvider>.Instance);
    }

    [Fact]
    public async Task BannedWord_ShortCircuitsWithBannedWordError()
    {
        var bannedFile = Path.Combine(_tempDir, "banned.txt");
        File.WriteAllText(bannedFile, "acme\n");
        var sut = BuildSut(MakeOptions(bannedPath: bannedFile));

        var result = await sut.PerformPasswordChangeAsync("alice", "old", "AcmeRocks42!");

        Assert.NotNull(result);
        Assert.Equal(ApiErrorCode.BannedWord, result!.ErrorCode);
        await _inner.DidNotReceive().PerformPasswordChangeAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task LocalPwned_ShortCircuitsWithLocallyKnownPwnedError()
    {
        // SHA1("hunter2!") = 97716E46EA8B045B52147CC9C2D32566055C7660 (40 hex uppercase)
        // prefix 5 = "97716", suffix = "E46EA8B045B52147CC9C2D32566055C7660"
        var corpus = Path.Combine(_tempDir, "corpus");
        Directory.CreateDirectory(corpus);
        File.WriteAllText(Path.Combine(corpus, "97716.txt"),
            "E46EA8B045B52147CC9C2D32566055C7660:99\n");
        var sut = BuildSut(MakeOptions(pwnedPath: corpus));

        var result = await sut.PerformPasswordChangeAsync("alice", "old", "hunter2!");

        Assert.NotNull(result);
        Assert.Equal(ApiErrorCode.LocallyKnownPwned, result!.ErrorCode);
        await _inner.DidNotReceive().PerformPasswordChangeAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task NoMatch_DelegatesToInnerAndReturnsInnerResult()
    {
        var sut = BuildSut(MakeOptions()); // both disabled
        _inner.PerformPasswordChangeAsync("alice", "old", "Ok!Password1").Returns((ApiErrorItem?)null);

        var result = await sut.PerformPasswordChangeAsync("alice", "old", "Ok!Password1");

        Assert.Null(result);
        await _inner.Received(1).PerformPasswordChangeAsync("alice", "old", "Ok!Password1");
    }

    [Fact]
    public async Task InnerError_IsPropagated()
    {
        var sut = BuildSut(MakeOptions());
        var innerError = new ApiErrorItem(ApiErrorCode.InvalidCredentials, "bad creds");
        _inner.PerformPasswordChangeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(innerError);

        var result = await sut.PerformPasswordChangeAsync("alice", "old", "Ok!Password1");

        Assert.Same(innerError, result);
    }

    [Fact]
    public async Task BannedCheckRunsBeforePwnedCheck()
    {
        // Both seeded to match "acme-hunter2!" — SHA1 of which won't match our pwned seed.
        // So we seed banned with "acme" and set pwned to a corpus that WOULD match if called.
        // Expect BannedWord (not LocallyKnownPwned).
        var bannedFile = Path.Combine(_tempDir, "banned.txt");
        File.WriteAllText(bannedFile, "acme\n");

        var corpus = Path.Combine(_tempDir, "corpus");
        Directory.CreateDirectory(corpus);
        // Seed a valid-looking file so pwned checker is "enabled"
        File.WriteAllText(Path.Combine(corpus, "00000.txt"), "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA:1\n");

        var sut = BuildSut(MakeOptions(bannedPath: bannedFile, pwnedPath: corpus));

        var result = await sut.PerformPasswordChangeAsync("alice", "old", "acme-whatever");

        Assert.Equal(ApiErrorCode.BannedWord, result!.ErrorCode);
    }
}

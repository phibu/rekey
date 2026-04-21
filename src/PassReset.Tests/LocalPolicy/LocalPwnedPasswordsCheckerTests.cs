using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using PassReset.Common.LocalPolicy;
using Xunit;

namespace PassReset.Tests.LocalPolicy;

public sealed class LocalPwnedPasswordsCheckerTests : IDisposable
{
    private readonly string _corpusDir;

    public LocalPwnedPasswordsCheckerTests()
    {
        _corpusDir = Path.Combine(Path.GetTempPath(), "passreset-pwned-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_corpusDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_corpusDir, recursive: true); } catch { /* best effort */ }
    }

    private static string Sha1Hex(string s)
    {
        var bytes = SHA1.HashData(Encoding.UTF8.GetBytes(s));
        var sb = new StringBuilder(40);
        foreach (var b in bytes) sb.Append(b.ToString("X2"));
        return sb.ToString();
    }

    private void SeedCorpus(string password, int count = 42)
    {
        var hash = Sha1Hex(password);
        var prefix = hash.Substring(0, 5);
        var suffix = hash.Substring(5);
        File.WriteAllText(Path.Combine(_corpusDir, $"{prefix}.txt"), $"{suffix}:{count}\n");
    }

    private void SeedEmptyPrefix(string prefix)
    {
        File.WriteAllText(Path.Combine(_corpusDir, $"{prefix}.txt"), string.Empty);
    }

    private LocalPwnedPasswordsChecker Make(string? path) =>
        new(new LocalPolicyOptions { LocalPwnedPasswordsPath = path },
            NullLogger<LocalPwnedPasswordsChecker>.Instance);

    [Fact]
    public async Task Disabled_WhenPathIsNull_ContainsAsyncReturnsFalse()
    {
        var sut = Make(path: null);
        Assert.False(await sut.ContainsAsync("anything"));
    }

    [Fact]
    public async Task Contains_KnownPasswordReturnsTrue()
    {
        SeedCorpus("P@ssw0rd!");
        var sut = Make(_corpusDir);
        Assert.True(await sut.ContainsAsync("P@ssw0rd!"));
    }

    [Fact]
    public async Task Contains_UnknownPasswordReturnsFalse()
    {
        SeedCorpus("known");
        var sut = Make(_corpusDir);
        Assert.False(await sut.ContainsAsync("unknown-different-password"));
    }

    [Fact]
    public async Task Contains_MissingPrefixFileReturnsFalse()
    {
        var sut = Make(_corpusDir); // directory exists but no files
        Assert.False(await sut.ContainsAsync("whatever"));
    }

    [Fact]
    public async Task Contains_RepeatedLookupUsesCache()
    {
        SeedCorpus("cachetest");
        var sut = Make(_corpusDir);
        Assert.True(await sut.ContainsAsync("cachetest"));
        // Delete the file; cached result should still hit
        foreach (var f in Directory.GetFiles(_corpusDir)) File.Delete(f);
        Assert.True(await sut.ContainsAsync("cachetest"));
    }

    [Fact]
    public void Ctor_ThrowsWhenPathSetButDirMissing()
    {
        var missing = Path.Combine(_corpusDir, "does-not-exist");
        var opts = new LocalPolicyOptions { LocalPwnedPasswordsPath = missing };
        Assert.Throws<DirectoryNotFoundException>(() =>
            new LocalPwnedPasswordsChecker(opts, NullLogger<LocalPwnedPasswordsChecker>.Instance));
    }
}

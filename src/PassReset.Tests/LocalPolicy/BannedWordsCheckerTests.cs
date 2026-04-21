using Microsoft.Extensions.Logging.Abstractions;
using PassReset.Common.LocalPolicy;
using Xunit;

namespace PassReset.Tests.LocalPolicy;

public sealed class BannedWordsCheckerTests : IDisposable
{
    private readonly string _tempDir;

    public BannedWordsCheckerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "passreset-banned-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
    }

    private string WriteFile(string contents)
    {
        var path = Path.Combine(_tempDir, "banned.txt");
        File.WriteAllText(path, contents);
        return path;
    }

    private BannedWordsChecker Make(string? path, int minLen = 4)
    {
        var opts = new LocalPolicyOptions { BannedWordsPath = path, MinBannedTermLength = minLen };
        return new BannedWordsChecker(opts, NullLogger<BannedWordsChecker>.Instance);
    }

    [Fact]
    public void Disabled_WhenPathIsNull_MatchesAlwaysReturnsFalse()
    {
        var sut = Make(path: null);
        Assert.False(sut.Matches("anything"));
    }

    [Fact]
    public void Disabled_WhenPathIsEmpty_MatchesAlwaysReturnsFalse()
    {
        var sut = Make(path: "");
        Assert.False(sut.Matches("anything"));
    }

    [Fact]
    public void Matches_CaseInsensitiveSubstring()
    {
        var path = WriteFile("acme\n");
        var sut = Make(path);
        Assert.True(sut.Matches("AcmeRocks123"));
        Assert.True(sut.Matches("prefixACMEsuffix"));
        Assert.False(sut.Matches("nothere"));
    }

    [Fact]
    public void Matches_IgnoresCommentsAndBlankLines()
    {
        var path = WriteFile("# a comment\n\n   \nacme\n# trailing\n");
        var sut = Make(path);
        Assert.True(sut.Matches("acme"));
        Assert.False(sut.Matches("comment"));
    }

    [Fact]
    public void Load_TrimsWhitespace()
    {
        var path = WriteFile("  acme  \n");
        var sut = Make(path);
        Assert.True(sut.Matches("acme"));
    }

    [Fact]
    public void Load_SkipsTermsShorterThanMin()
    {
        var path = WriteFile("ab\nacme\n");
        var sut = Make(path, minLen: 4);
        Assert.False(sut.Matches("ab"));
        Assert.True(sut.Matches("acme"));
    }

    [Fact]
    public void Ctor_ThrowsWhenPathSetButFileMissing()
    {
        var missing = Path.Combine(_tempDir, "does-not-exist.txt");
        var opts = new LocalPolicyOptions { BannedWordsPath = missing };
        Assert.Throws<FileNotFoundException>(() =>
            new BannedWordsChecker(opts, NullLogger<BannedWordsChecker>.Instance));
    }

    [Fact]
    public void Matches_ReturnsFalseWhenFileEmpty()
    {
        var path = WriteFile("");
        var sut = Make(path);
        Assert.False(sut.Matches("anything"));
    }
}

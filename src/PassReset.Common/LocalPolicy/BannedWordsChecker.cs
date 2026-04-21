using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PassReset.Common.LocalPolicy;

/// <summary>
/// Loads a plaintext banned-words list at construction and offers case-insensitive
/// substring matching against candidate passwords. Null-object when disabled.
/// Thread-safe after construction (read-only list).
/// </summary>
public sealed class BannedWordsChecker
{
    private readonly List<string> _terms;
    private readonly ILogger<BannedWordsChecker> _logger;
    private readonly bool _enabled;

    public BannedWordsChecker(IOptions<PasswordChangeOptions> options, ILogger<BannedWordsChecker> logger)
        : this(options.Value.LocalPolicy, logger) { }

    public BannedWordsChecker(LocalPolicyOptions options, ILogger<BannedWordsChecker> logger)
    {
        _logger = logger;
        var path = options.BannedWordsPath;

        if (string.IsNullOrWhiteSpace(path))
        {
            _enabled = false;
            _terms = [];
            _logger.LogInformation("BannedWordsChecker disabled (no BannedWordsPath configured)");
            return;
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"BannedWordsPath configured but file not found: {path}", path);
        }

        var minLen = Math.Max(1, options.MinBannedTermLength);
        _terms = File.ReadAllLines(path)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .Where(line => line.Length >= minLen)
            .Select(line => line.ToLowerInvariant())
            .ToList();
        _enabled = true;
        _logger.LogInformation(
            "BannedWordsChecker loaded {Count} terms from {Path} (min length {MinLen})",
            _terms.Count, path, minLen);
    }

    /// <summary>
    /// Returns true if <paramref name="password"/> contains any loaded term
    /// (case-insensitive substring). Returns false when disabled or list empty.
    /// </summary>
    public bool Matches(string password)
    {
        if (!_enabled || _terms.Count == 0) return false;
        var needle = password.ToLowerInvariant();
        for (var i = 0; i < _terms.Count; i++)
        {
            if (needle.Contains(_terms[i], StringComparison.Ordinal)) return true;
        }
        return false;
    }
}

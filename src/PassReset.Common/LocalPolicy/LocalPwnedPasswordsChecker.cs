using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PassReset.Common.LocalPolicy;

/// <summary>
/// Offline HIBP k-anonymity lookup against an on-disk corpus laid out as per-prefix
/// files: <c>{LocalPwnedPasswordsPath}/{PREFIX5}.txt</c>, each line
/// <c>&lt;SUFFIX35&gt;:&lt;count&gt;</c>. Files are read lazily on first lookup per
/// prefix; per-prefix suffix sets are cached in an LRU with capacity 256.
/// </summary>
public sealed class LocalPwnedPasswordsChecker
{
    private const int CacheCapacity = 256;

    private readonly string? _root;
    private readonly bool _enabled;
    private readonly ILogger<LocalPwnedPasswordsChecker> _logger;
    private readonly LinkedList<string> _lruOrder = new();
    private readonly Dictionary<string, (LinkedListNode<string> Node, HashSet<string> Suffixes)> _cache =
        new(StringComparer.Ordinal);
    private readonly HashSet<string> _warnedMissing = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public LocalPwnedPasswordsChecker(IOptions<PasswordChangeOptions> options,
        ILogger<LocalPwnedPasswordsChecker> logger)
        : this(options.Value.LocalPolicy, logger) { }

    public LocalPwnedPasswordsChecker(LocalPolicyOptions options,
        ILogger<LocalPwnedPasswordsChecker> logger)
    {
        _logger = logger;
        var path = options.LocalPwnedPasswordsPath;

        if (string.IsNullOrWhiteSpace(path))
        {
            _enabled = false;
            _logger.LogInformation("LocalPwnedPasswordsChecker disabled (no path configured)");
            return;
        }

        if (!Directory.Exists(path))
        {
            throw new DirectoryNotFoundException(
                $"LocalPwnedPasswordsPath configured but directory not found: {path}");
        }

        _root = path;
        _enabled = true;
        _logger.LogInformation("LocalPwnedPasswordsChecker enabled (root={Root})", path);
    }

    public Task<bool> ContainsAsync(string password)
    {
        if (!_enabled) return Task.FromResult(false);

        var hash = Sha1UpperHex(password);
        var prefix = hash.Substring(0, 5);
        var suffix = hash.Substring(5);

        HashSet<string> suffixes;
        lock (_gate)
        {
            if (_cache.TryGetValue(prefix, out var entry))
            {
                _lruOrder.Remove(entry.Node);
                _lruOrder.AddFirst(entry.Node);
                suffixes = entry.Suffixes;
            }
            else
            {
                suffixes = LoadPrefix(prefix);
                var node = _lruOrder.AddFirst(prefix);
                _cache[prefix] = (node, suffixes);
                Evict();
            }
        }

        return Task.FromResult(suffixes.Contains(suffix));
    }

    private HashSet<string> LoadPrefix(string prefix)
    {
        var file = Path.Combine(_root!, $"{prefix}.txt");
        if (!File.Exists(file))
        {
            if (_warnedMissing.Add(prefix))
            {
                _logger.LogWarning(
                    "Local HIBP prefix file missing: {File}. Treating as empty (no match).", file);
            }
            return new HashSet<string>(StringComparer.Ordinal);
        }

        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in File.ReadLines(file))
        {
            var colon = line.IndexOf(':');
            var suffix = colon >= 0 ? line[..colon] : line;
            suffix = suffix.Trim();
            if (suffix.Length == 35) set.Add(suffix);
        }
        return set;
    }

    private void Evict()
    {
        while (_cache.Count > CacheCapacity)
        {
            var last = _lruOrder.Last!;
            _lruOrder.RemoveLast();
            _cache.Remove(last.Value);
        }
    }

    private static string Sha1UpperHex(string s)
    {
        var bytes = SHA1.HashData(Encoding.UTF8.GetBytes(s));
        var sb = new StringBuilder(40);
        foreach (var b in bytes) sb.Append(b.ToString("X2"));
        return sb.ToString();
    }
}

using System.DirectoryServices.Protocols;
using PassReset.PasswordProvider.Ldap;

namespace PassReset.Tests.Fakes;

/// <summary>
/// Scripted <see cref="ILdapSession"/> fake for unit + contract tests.
/// Callers register <see cref="SearchResponse"/>/<see cref="ModifyResponse"/> values
/// (or exceptions to throw) keyed by operation type + filter/DN substring.
/// Rules are matched in registration order; first match wins. Not thread-safe —
/// single-threaded tests only.
/// </summary>
/// <remarks>
/// Call counts increment on entry (before rule matching), so they include calls
/// that ultimately throw. <see cref="RootDse"/> is a settable property; assign
/// <c>null</c> to simulate a silent root-DSE failure (the real <see cref="LdapSession"/>
/// catches <see cref="LdapException"/>/<see cref="DirectoryOperationException"/>
/// internally).
/// </remarks>
public sealed class FakeLdapSession : ILdapSession
{
    private readonly List<SearchRule> _searchRules = new();
    private readonly List<ModifyRule> _modifyRules = new();
    private SearchResponse? _defaultSearchResponse;

    public SearchResultEntry? RootDse { get; set; }

    public int SearchCallCount { get; private set; }
    public int ModifyCallCount { get; private set; }
    public int BindCallCount { get; private set; }

    /// <summary>
    /// The most recent <see cref="ModifyRequest"/> passed to <see cref="Modify"/>.
    /// Lets tests assert on the structure (operations, attribute names, byte values)
    /// of the AD atomic-change-password protocol payload.
    /// </summary>
    public ModifyRequest? LastModifyRequest { get; private set; }

    public Exception? BindThrows { get; set; }

    public void Bind()
    {
        BindCallCount++;
        if (BindThrows is not null) throw BindThrows;
    }

    public FakeLdapSession OnSearch(string filterContains, SearchResponse response)
    {
        _searchRules.Add(new SearchRule(filterContains, response, null));
        return this;
    }

    public FakeLdapSession OnSearchThrow(string filterContains, Exception ex)
    {
        ArgumentNullException.ThrowIfNull(ex);
        _searchRules.Add(new SearchRule(filterContains, null, ex));
        return this;
    }

    /// <summary>
    /// Sets a catch-all <see cref="SearchResponse"/> used when no registered rule matches
    /// the incoming filter. Without a default, <see cref="Search"/> throws —
    /// which is the right behavior for targeted unit tests but not for the
    /// shared contract suite, where an "unknown user" scenario needs all three
    /// AllowedUsernameAttributes filters to miss without forcing each caller
    /// to register the exact same "empty" rule three times.
    /// </summary>
    public FakeLdapSession DefaultSearchResponse(SearchResponse response)
    {
        _defaultSearchResponse = response;
        return this;
    }

    public FakeLdapSession OnModify(string dnContains, ModifyResponse response)
    {
        _modifyRules.Add(new ModifyRule(dnContains, response, null, null));
        return this;
    }

    public FakeLdapSession OnModifyThrow(string dnContains, Exception ex)
    {
        ArgumentNullException.ThrowIfNull(ex);
        _modifyRules.Add(new ModifyRule(dnContains, null, ex, null));
        return this;
    }

    /// <summary>
    /// Registers a credential-aware Modify rule: the callback is invoked with the
    /// incoming <see cref="ModifyRequest"/> and returns the <see cref="ModifyResponse"/>
    /// to emit. Used by contract tests to distinguish right-vs-wrong current-password
    /// by inspecting the Delete(unicodePwd) bytes of the AD atomic change-password
    /// payload. First matching rule wins, same as <see cref="OnModify"/>.
    /// </summary>
    public FakeLdapSession OnModifyIf(string dnContains, Func<ModifyRequest, ModifyResponse> responder)
    {
        ArgumentNullException.ThrowIfNull(responder);
        _modifyRules.Add(new ModifyRule(dnContains, null, null, responder));
        return this;
    }

    public SearchResponse Search(SearchRequest request)
    {
        SearchCallCount++;
        // Filter can be string or SearchFilter; ToString() yields the LDAP filter text for both.
        var filterText = request.Filter?.ToString() ?? string.Empty;
        foreach (var rule in _searchRules)
        {
            if (filterText.Contains(rule.FilterContains, StringComparison.OrdinalIgnoreCase))
            {
                if (rule.Throw is not null) throw rule.Throw;
                return rule.Response!;
            }
        }
        if (_defaultSearchResponse is not null) return _defaultSearchResponse;
        throw new InvalidOperationException(
            $"FakeLdapSession: no matching SearchRule for filter='{filterText}'. Register one via OnSearch(...).");
    }

    public ModifyResponse Modify(ModifyRequest request)
    {
        ModifyCallCount++;
        LastModifyRequest = request;
        foreach (var rule in _modifyRules)
        {
            if (request.DistinguishedName.Contains(rule.DnContains, StringComparison.OrdinalIgnoreCase))
            {
                if (rule.Throw is not null) throw rule.Throw;
                if (rule.Responder is not null) return rule.Responder(request);
                return rule.Response!;
            }
        }
        throw new InvalidOperationException(
            $"FakeLdapSession: no matching ModifyRule for DN='{request.DistinguishedName}'. Register one via OnModify(...).");
    }

    public void Dispose() { }

    private sealed record SearchRule(string FilterContains, SearchResponse? Response, Exception? Throw);
    private sealed record ModifyRule(
        string DnContains,
        ModifyResponse? Response,
        Exception? Throw,
        Func<ModifyRequest, ModifyResponse>? Responder);
}

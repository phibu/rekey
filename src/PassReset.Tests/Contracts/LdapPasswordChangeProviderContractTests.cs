using System.DirectoryServices.Protocols;
using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PassReset.Common;
using PassReset.Common.LocalPolicy;
using PassReset.PasswordProvider.Ldap;
using PassReset.Tests.Fakes;

namespace PassReset.Tests.Contracts;

/// <summary>
/// Runs the shared <see cref="IPasswordChangeProviderContract"/> against
/// <see cref="LdapPasswordChangeProvider"/> wired to a <see cref="FakeLdapSession"/>.
/// </summary>
public sealed class LdapPasswordChangeProviderContractTests : IPasswordChangeProviderContract
{
    // One fake per test — xUnit creates a fresh class instance per [Fact], so _fake is
    // constructed once and reused across CreateProvider + SeedUser within the same test.
    private readonly FakeLdapSession _fake = new();

    private const string BaseDn = "DC=corp,DC=example,DC=com";

    // AD extended-error DWORD for ERROR_PASSWORD_RESTRICTION. LdapErrorMapping.Map
    // surfaces this as ApiErrorCode.ComplexPassword regardless of ResultCode.
    private const string ComplexityErrorMessage = "0000052D: SvcErr: complexity";

    // LDAP result code 49 == InvalidCredentials. The ResultCode enum on this TFM
    // has no named value for it, so we cast from int. LdapErrorMapping keys off
    // the integer code directly (see LdapErrorMapping.Map switch arm `49 => ...`).
    private const int LdapInvalidCredentialsResultCode = 49;

    protected override IPasswordChangeProvider CreateProvider()
    {
        // Fallback for any search filter not explicitly seeded — mirrors a real AD
        // returning zero entries for an unknown user, instead of the fake's default
        // "no matching rule" throw. Specific rules registered in SeedUser still win
        // (they're matched before the default is consulted).
        _fake.DefaultSearchResponse(MakeResponse());

        var opts = new PasswordChangeOptions
        {
            AllowedUsernameAttributes = new[] { "samaccountname", "userprincipalname", "mail" },
            EnforceMinimumPasswordAge = false,  // skip PreCheckMinPwdAge in the contract suite
            BaseDn = BaseDn,
            ServiceAccountDn = "CN=svc," + BaseDn,
            ServiceAccountPassword = "svcpw",
            LdapHostnames = new[] { "dc01.corp.example.com" },
            LdapPort = 636,
        };
        return new LdapPasswordChangeProvider(
            Options.Create(opts),
            NullLogger<LdapPasswordChangeProvider>.Instance,
            () => _fake);
    }

    protected override TestUser SeedUser(string username, string currentPassword)
    {
        var dn = $"CN={username},OU=Users,{BaseDn}";
        var entry = MakeEntry(dn);

        // Register search rules to resolve the user DN. The provider iterates
        // AllowedUsernameAttributes in order: sAMAccountName → userPrincipalName → mail.
        // For a UPN-style username (contains '@'), we deliberately register empty
        // responses on the earlier sAM rule so the provider falls through to UPN —
        // exercising the fallback path.
        var looksLikeUpnOrMail = username.Contains('@');
        if (looksLikeUpnOrMail)
        {
            _fake.OnSearch($"(sAMAccountName={username})", MakeResponse());
            _fake.OnSearch($"(userPrincipalName={username})", MakeResponse(entry));
            _fake.OnSearch($"(mail={username})", MakeResponse(entry));
        }
        else
        {
            _fake.OnSearch($"(sAMAccountName={username})", MakeResponse(entry));
            // UPN / mail rules registered as empty for completeness — the provider
            // short-circuits on the first hit so these typically aren't exercised,
            // but FakeLdapSession throws on unmatched filters and some tests
            // (e.g. UnknownUser) may reach them.
            _fake.OnSearch($"(userPrincipalName={username})", MakeResponse());
            _fake.OnSearch($"(mail={username})", MakeResponse());
        }

        // Credential-aware Modify rule: inspect the Delete(unicodePwd) bytes and
        // compare against the seeded current-password + a "weak" sentinel on new-password.
        // AD's atomic change-password ModifyRequest carries the old password as the
        // FIRST modification (Delete, unicodePwd, UTF-16LE "<old>") and the new password
        // as the SECOND (Add, unicodePwd, UTF-16LE "<new>").
        _fake.OnModifyIf(dn, req =>
        {
            var (oldPwd, newPwd) = ExtractAtomicChangePwd(req);

            // Wrong current password → InvalidCredentials. AD surfaces this as LDAP
            // ResultCode 49 (the enum has no named value for it on this TFM, so cast
            // from int — matches LdapErrorMapping which keys off the integer code).
            if (!string.Equals(oldPwd, currentPassword, StringComparison.Ordinal))
            {
                return MakeModifyResponse((ResultCode)LdapInvalidCredentialsResultCode, string.Empty);
            }

            // New-password is weak / empty / identical-to-old → ComplexPassword.
            // The "weak" contract sentinel, empty-string, and old == new all map to
            // AD's ERROR_PASSWORD_RESTRICTION (0x52D).
            if (newPwd.Length == 0
                || string.Equals(newPwd, "weak", StringComparison.Ordinal)
                || string.Equals(newPwd, oldPwd, StringComparison.Ordinal))
            {
                return MakeModifyResponse(ResultCode.ConstraintViolation, ComplexityErrorMessage);
            }

            return MakeModifyResponse(ResultCode.Success, string.Empty);
        });

        return new TestUser(username, currentPassword);
    }

    protected override IPasswordChangeProvider SeedBannedWord(string term)
    {
        // Write a temp banned-words file containing the given term, then wrap the
        // inner LDAP provider with LocalPolicyPasswordChangeProvider pointed at that file.
        var path = Path.Combine(Path.GetTempPath(), $"banned_{Guid.NewGuid():N}.txt");
        File.WriteAllText(path, term);

        var localOpts = new LocalPolicyOptions { BannedWordsPath = path };
        var banned = new BannedWordsChecker(localOpts, NullLogger<BannedWordsChecker>.Instance);
        var pwned = new LocalPwnedPasswordsChecker(
            new LocalPolicyOptions { LocalPwnedPasswordsPath = null },
            NullLogger<LocalPwnedPasswordsChecker>.Instance);

        return new LocalPolicyPasswordChangeProvider(
            CreateProvider(),
            banned,
            pwned,
            NullLogger<LocalPolicyPasswordChangeProvider>.Instance);
    }

    // ---- helpers -----------------------------------------------------------

    // Pull the old and new cleartext passwords out of an AD atomic change-password
    // ModifyRequest. Mirrors the encoding emitted by LdapPasswordChangeProvider.BuildChangePasswordRequest:
    // UTF-16LE with literal " quote chars wrapping the value.
    //
    // The atomic change-password protocol requires exactly two modifications:
    // (1) Delete unicodePwd = "<old>", (2) Add unicodePwd = "<new>". Anything else
    // (e.g. a single Replace, as used by SetPassword) is a structural mismatch we
    // want to surface loudly rather than silently treating as a weak-password case.
    private static (string Old, string New) ExtractAtomicChangePwd(ModifyRequest req)
    {
        var count = req.Modifications.Count;
        if (count != 2)
        {
            throw new InvalidOperationException(
                $"Unexpected modification shape: count={count}, expected atomic Delete+Add");
        }
        var oldBytes = (byte[])req.Modifications[0][0];
        var newBytes = (byte[])req.Modifications[1][0];
        return (Unquote(System.Text.Encoding.Unicode.GetString(oldBytes)),
                Unquote(System.Text.Encoding.Unicode.GetString(newBytes)));
    }

    private static string Unquote(string s) =>
        s.Length >= 2 && s[0] == '"' && s[^1] == '"' ? s[1..^1] : s;

    // These three helpers are duplicated from LdapPasswordChangeProviderTests to keep the
    // contract project self-contained and free of test-to-test coupling. They use reflection
    // because SearchResponse/ModifyResponse/SearchResultEntry have no public constructors
    // on .NET 10.
    private static SearchResponse MakeResponse(params SearchResultEntry[] entries)
    {
        var response = (SearchResponse)Activator.CreateInstance(
            typeof(SearchResponse),
            BindingFlags.NonPublic | BindingFlags.Instance,
            null,
            new object?[] { string.Empty, Array.Empty<DirectoryControl>(), ResultCode.Success, string.Empty, Array.Empty<Uri>() },
            null)!;
        var entriesProp = typeof(SearchResponse).GetProperty("Entries")!;
        var collection = (SearchResultEntryCollection)entriesProp.GetValue(response)!;
        var addMethod = typeof(SearchResultEntryCollection).GetMethod(
            "Add", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
            null, new[] { typeof(SearchResultEntry) }, null)!;
        foreach (var e in entries) addMethod.Invoke(collection, new object?[] { e });
        return response;
    }

    private static SearchResultEntry MakeEntry(string dn, params (string Name, string Value)[] attrs)
    {
        var entry = (SearchResultEntry)Activator.CreateInstance(
            typeof(SearchResultEntry),
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
            null,
            new object[] { dn },
            null)!;

        if (attrs is { Length: > 0 })
        {
            var attrCollection = entry.Attributes;
            var addMethod = typeof(SearchResultAttributeCollection).GetMethod(
                "Add", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                null, new[] { typeof(string), typeof(DirectoryAttribute) }, null)!;
            foreach (var group in attrs.GroupBy(a => a.Name, StringComparer.OrdinalIgnoreCase))
            {
                var directoryAttr = new DirectoryAttribute { Name = group.Key };
                foreach (var (_, value) in group) directoryAttr.Add(value);
                addMethod.Invoke(attrCollection, new object?[] { group.Key, directoryAttr });
            }
        }

        return entry;
    }

    private static ModifyResponse MakeModifyResponse(
        ResultCode resultCode = ResultCode.Success, string errorMessage = "")
    {
        var response = (ModifyResponse)Activator.CreateInstance(
            typeof(ModifyResponse),
            BindingFlags.NonPublic | BindingFlags.Instance,
            null,
            new object?[] { string.Empty, Array.Empty<DirectoryControl>(), resultCode, errorMessage, Array.Empty<Uri>() },
            null)!;
        return response;
    }
}

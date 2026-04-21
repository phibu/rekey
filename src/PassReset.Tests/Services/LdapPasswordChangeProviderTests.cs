using System.DirectoryServices.Protocols;
using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PassReset.Common;
using PassReset.PasswordProvider.Ldap;
using PassReset.Tests.Fakes;
using Xunit;

namespace PassReset.Tests.Services;

public class LdapPasswordChangeProviderTests
{
    private static (LdapPasswordChangeProvider sut, FakeLdapSession fake) Build(
        PasswordChangeOptions? opts = null)
    {
        opts ??= new PasswordChangeOptions
        {
            AllowedUsernameAttributes = new[] { "samaccountname", "userprincipalname", "mail" },
            BaseDn = "DC=corp,DC=example,DC=com",
            ServiceAccountDn = "CN=svc,DC=corp,DC=example,DC=com",
            ServiceAccountPassword = "svcpw",
            LdapHostnames = new[] { "dc01.corp.example.com" },
            LdapPort = 636,
        };
        var fake = new FakeLdapSession();
        var sut = new LdapPasswordChangeProvider(
            Options.Create(opts),
            NullLogger<LdapPasswordChangeProvider>.Instance,
            () => fake);
        return (sut, fake);
    }

    private static SearchResponse MakeResponse(params SearchResultEntry[] entries)
    {
        // SearchResponse has no parameterless ctor on .NET 10; use the internal
        // (string dn, DirectoryControl[] controls, ResultCode result, string message, Uri[] referral) overload.
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
        // SearchResultEntry has a public (string dn) ctor on .NET 10.
        var entry = (SearchResultEntry)Activator.CreateInstance(
            typeof(SearchResultEntry),
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
            null,
            new object[] { dn },
            null)!;

        if (attrs is { Length: > 0 })
        {
            // Group values per attribute name so multiple (Name, Value) pairs with the
            // same Name (e.g. multi-valued memberOf) collapse into one DirectoryAttribute.
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
        // ModifyResponse has no public parameterless ctor. Use the internal
        // (string dn, DirectoryControl[] controls, ResultCode result, string message, Uri[] referral) overload,
        // same pattern as MakeResponse for SearchResponse.
        var response = (ModifyResponse)Activator.CreateInstance(
            typeof(ModifyResponse),
            BindingFlags.NonPublic | BindingFlags.Instance,
            null,
            new object?[] { string.Empty, Array.Empty<DirectoryControl>(), resultCode, errorMessage, Array.Empty<Uri>() },
            null)!;
        return response;
    }

    [Fact]
    public async Task FindUserDn_SamAccountNameHits_ReturnsDn()
    {
        var (sut, fake) = Build();
        fake.OnSearch(
            "(sAMAccountName=alice)",
            MakeResponse(MakeEntry("CN=Alice,OU=Users,DC=corp,DC=example,DC=com")));

        var method = typeof(LdapPasswordChangeProvider).GetMethod(
            "FindUserDnAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var task = (Task<string?>)method.Invoke(sut, new object?[] { fake, "alice" })!;
        var dn = await task;

        Assert.Equal("CN=Alice,OU=Users,DC=corp,DC=example,DC=com", dn);
        Assert.Equal(1, fake.SearchCallCount);
    }

    [Fact]
    public async Task FindUserDn_FallsThroughToUpn_WhenSamEmpty()
    {
        var (sut, fake) = Build();
        fake.OnSearch("(sAMAccountName=alice)", MakeResponse());
        fake.OnSearch(
            "(userPrincipalName=alice)",
            MakeResponse(MakeEntry("CN=Alice,OU=Users,DC=corp,DC=example,DC=com")));

        var method = typeof(LdapPasswordChangeProvider).GetMethod(
            "FindUserDnAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var task = (Task<string?>)method.Invoke(sut, new object?[] { fake, "alice" })!;
        var dn = await task;

        Assert.Equal("CN=Alice,OU=Users,DC=corp,DC=example,DC=com", dn);
        Assert.Equal(2, fake.SearchCallCount);
    }

    [Fact]
    public async Task FindUserDn_AllAttributesMiss_ReturnsNull()
    {
        var (sut, fake) = Build();
        fake.OnSearch("(sAMAccountName=ghost)",     MakeResponse());
        fake.OnSearch("(userPrincipalName=ghost)",  MakeResponse());
        fake.OnSearch("(mail=ghost)",               MakeResponse());

        var method = typeof(LdapPasswordChangeProvider).GetMethod(
            "FindUserDnAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var task = (Task<string?>)method.Invoke(sut, new object?[] { fake, "ghost" })!;
        var dn = await task;

        Assert.Null(dn);
        Assert.Equal(3, fake.SearchCallCount);
    }

    [Fact]
    public async Task PerformPasswordChangeAsync_HappyPath_EmitsAdAtomicChangePasswordRequest()
    {
        var (sut, fake) = Build();
        fake.OnSearch(
            "(sAMAccountName=alice)",
            MakeResponse(MakeEntry("CN=Alice,OU=Users,DC=corp,DC=example,DC=com")));
        // ModifyResponse has no public parameterless ctor; use MakeModifyResponse helper.
        fake.OnModify(
            "CN=Alice,OU=Users,DC=corp,DC=example,DC=com",
            MakeModifyResponse(ResultCode.Success));

        var result = await sut.PerformPasswordChangeAsync("alice", "OldPass1!", "NewPass1!");

        Assert.Null(result);
        Assert.Equal(1, fake.BindCallCount);
        Assert.Equal(1, fake.ModifyCallCount);

        // The AD atomic change-password protocol requires a single ModifyRequest with two
        // ordered modifications: Delete(unicodePwd, "<old>") then Add(unicodePwd, "<new>"),
        // both UTF-16LE encoded with literal quote chars wrapping the password value.
        // A regression that swaps order, flips operations, or changes the encoding must fail here.
        var modify = fake.LastModifyRequest;
        Assert.NotNull(modify);
        Assert.Equal("CN=Alice,OU=Users,DC=corp,DC=example,DC=com", modify!.DistinguishedName);
        Assert.Equal(2, modify.Modifications.Count);

        var del = modify.Modifications[0];
        Assert.Equal(DirectoryAttributeOperation.Delete, del.Operation);
        Assert.Equal("unicodePwd", del.Name);
        var delValue = Assert.Single(del);
        var delBytes = Assert.IsType<byte[]>(delValue);
        Assert.Equal("\"OldPass1!\"", System.Text.Encoding.Unicode.GetString(delBytes));

        var add = modify.Modifications[1];
        Assert.Equal(DirectoryAttributeOperation.Add, add.Operation);
        Assert.Equal("unicodePwd", add.Name);
        var addValue = Assert.Single(add);
        var addBytes = Assert.IsType<byte[]>(addValue);
        Assert.Equal("\"NewPass1!\"", System.Text.Encoding.Unicode.GetString(addBytes));
    }

    [Fact]
    public async Task PerformPasswordChangeAsync_UserNotFound_ReturnsUserNotFound()
    {
        var (sut, fake) = Build();
        fake.OnSearch("(sAMAccountName=ghost)",     MakeResponse());
        fake.OnSearch("(userPrincipalName=ghost)",  MakeResponse());
        fake.OnSearch("(mail=ghost)",               MakeResponse());

        var result = await sut.PerformPasswordChangeAsync("ghost", "any", "new");

        Assert.NotNull(result);
        Assert.Equal(ApiErrorCode.UserNotFound, result!.ErrorCode);
        Assert.Equal(0, fake.ModifyCallCount);
    }

    [Fact]
    public async Task PerformPasswordChangeAsync_RestrictedGroup_ReturnsChangeNotPermitted()
    {
        var opts = new PasswordChangeOptions
        {
            AllowedUsernameAttributes = new[] { "samaccountname" },
            RestrictedAdGroups = new() { "Domain Admins" },
            EnforceMinimumPasswordAge = false,
            BaseDn = "DC=corp,DC=example,DC=com",
            ServiceAccountDn = "CN=svc,DC=corp,DC=example,DC=com",
            ServiceAccountPassword = "svcpw",
            LdapHostnames = new[] { "dc01.corp.example.com" },
            LdapPort = 636,
        };
        var (sut, fake) = Build(opts);

        // First search resolves the user DN (filter=(sAMAccountName=alice)).
        fake.OnSearch(
            "(sAMAccountName=alice)",
            MakeResponse(MakeEntry("CN=Alice,OU=Users,DC=corp,DC=example,DC=com")));

        // Second search is the Base-scope group lookup on the user DN; the implementation
        // emits filter "(objectClass=user)" requesting memberOf. FakeLdapSession matches
        // by Filter.Contains, so we register the rule with that filter substring.
        fake.OnSearch(
            "(objectClass=user)",
            MakeResponse(MakeEntry("CN=Alice,OU=Users,DC=corp,DC=example,DC=com",
                (LdapAttributeNames.MemberOf, "CN=Domain Admins,CN=Users,DC=corp,DC=example,DC=com"))));

        var result = await sut.PerformPasswordChangeAsync("alice", "OldPass1!", "NewPass1!");

        Assert.NotNull(result);
        Assert.Equal(ApiErrorCode.ChangeNotPermitted, result!.ErrorCode);
        Assert.Equal(0, fake.ModifyCallCount);
    }

    // ----- ExtractCommonName: RFC 4514 RDN escape handling -----------------------------------

    [Theory]
    [InlineData("CN=Foo,OU=Users,DC=corp,DC=example,DC=com", "Foo")]
    [InlineData(@"CN=Doe\, John,OU=Users,DC=corp,DC=example,DC=com", "Doe, John")]
    [InlineData(@"CN=Foo\\Bar,OU=Users,DC=corp,DC=example,DC=com", @"Foo\Bar")]
    [InlineData("CN=Foo", "Foo")]
    [InlineData("OU=Foo,DC=corp,DC=example,DC=com", null)]
    [InlineData(@"CN=Smith\\\, Jr,OU=Users,DC=corp,DC=example,DC=com", @"Smith\, Jr")]
    public void ExtractCommonName_HandlesRdnEscapesAndPrefixAbsence(string dn, string? expected)
    {
        var method = typeof(LdapPasswordChangeProvider).GetMethod(
            "ExtractCommonName", BindingFlags.NonPublic | BindingFlags.Static)!;
        var actual = (string?)method.Invoke(null, new object?[] { dn });
        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task PerformPasswordChangeAsync_GroupReadFails_WithRestrictedConfigured_ReturnsChangeNotPermitted()
    {
        var opts = new PasswordChangeOptions
        {
            AllowedUsernameAttributes = new[] { "samaccountname" },
            RestrictedAdGroups = new() { "Domain Admins" },
            EnforceMinimumPasswordAge = false,
            BaseDn = "DC=corp,DC=example,DC=com",
            ServiceAccountDn = "CN=svc,DC=corp,DC=example,DC=com",
            ServiceAccountPassword = "svcpw",
            LdapHostnames = new[] { "dc01.corp.example.com" },
            LdapPort = 636,
        };
        var (sut, fake) = Build(opts);

        fake.OnSearch(
            "(sAMAccountName=alice)",
            MakeResponse(MakeEntry("CN=Alice,OU=Users,DC=corp,DC=example,DC=com")));
        // Base-scope memberOf lookup throws — operator misconfiguration (no read on memberOf).
        // With RestrictedAdGroups configured, the provider must fail closed.
        fake.OnSearchThrow(
            "(objectClass=user)",
            new LdapException("Insufficient access rights"));

        var result = await sut.PerformPasswordChangeAsync("alice", "OldPass1!", "NewPass1!");

        Assert.NotNull(result);
        Assert.Equal(ApiErrorCode.ChangeNotPermitted, result!.ErrorCode);
        Assert.Equal(0, fake.ModifyCallCount);
    }

    [Fact]
    public async Task PerformPasswordChangeAsync_AllowListMode_UserNotInAnyAllowedGroup_ReturnsChangeNotPermitted()
    {
        var opts = new PasswordChangeOptions
        {
            AllowedUsernameAttributes = new[] { "samaccountname" },
            AllowedAdGroups = new() { "Helpdesk" },
            EnforceMinimumPasswordAge = false,
            BaseDn = "DC=corp,DC=example,DC=com",
            ServiceAccountDn = "CN=svc,DC=corp,DC=example,DC=com",
            ServiceAccountPassword = "svcpw",
            LdapHostnames = new[] { "dc01.corp.example.com" },
            LdapPort = 636,
        };
        var (sut, fake) = Build(opts);

        fake.OnSearch(
            "(sAMAccountName=alice)",
            MakeResponse(MakeEntry("CN=Alice,OU=Users,DC=corp,DC=example,DC=com")));
        fake.OnSearch(
            "(objectClass=user)",
            MakeResponse(MakeEntry("CN=Alice,OU=Users,DC=corp,DC=example,DC=com",
                (LdapAttributeNames.MemberOf, "CN=Sales,CN=Users,DC=corp,DC=example,DC=com"))));

        var result = await sut.PerformPasswordChangeAsync("alice", "OldPass1!", "NewPass1!");

        Assert.NotNull(result);
        Assert.Equal(ApiErrorCode.ChangeNotPermitted, result!.ErrorCode);
        Assert.Equal(0, fake.ModifyCallCount);
    }

    [Fact]
    public async Task PerformPasswordChangeAsync_PwdLastSetIsZero_SkipsMinPwdAgeCheck()
    {
        var opts = new PasswordChangeOptions
        {
            AllowedUsernameAttributes = new[] { "samaccountname" },
            EnforceMinimumPasswordAge = true,
            BaseDn = "DC=corp,DC=example,DC=com",
            ServiceAccountDn = "CN=svc,DC=corp,DC=example,DC=com",
            ServiceAccountPassword = "svcpw",
            LdapHostnames = new[] { "dc01.corp.example.com" },
            LdapPort = 636,
        };
        var (sut, fake) = Build(opts);

        var minPwdAgeTicks = -TimeSpan.FromDays(1).Ticks;

        fake.OnSearch(
            "(sAMAccountName=alice)",
            MakeResponse(MakeEntry("CN=Alice,OU=Users,DC=corp,DC=example,DC=com")));
        // pwdLastSet=0 means "must change at next logon" — minPwdAge does not apply.
        fake.OnSearch(
            "(objectClass=user)",
            MakeResponse(MakeEntry("CN=Alice,OU=Users,DC=corp,DC=example,DC=com",
                (LdapAttributeNames.PwdLastSet, "0"))));
        fake.RootDse = MakeEntry("",
            (LdapAttributeNames.MinPwdAge, minPwdAgeTicks.ToString()));
        fake.OnModify(
            "CN=Alice,OU=Users,DC=corp,DC=example,DC=com",
            MakeModifyResponse(ResultCode.Success));

        var result = await sut.PerformPasswordChangeAsync("alice", "OldPass1!", "NewPass1!");

        Assert.Null(result);
        Assert.Equal(1, fake.ModifyCallCount);
    }

    [Fact]
    public async Task PerformPasswordChangeAsync_NoRootDse_SkipsMinPwdAgeCheck()
    {
        var opts = new PasswordChangeOptions
        {
            AllowedUsernameAttributes = new[] { "samaccountname" },
            EnforceMinimumPasswordAge = true,
            BaseDn = "DC=corp,DC=example,DC=com",
            ServiceAccountDn = "CN=svc,DC=corp,DC=example,DC=com",
            ServiceAccountPassword = "svcpw",
            LdapHostnames = new[] { "dc01.corp.example.com" },
            LdapPort = 636,
        };
        var (sut, fake) = Build(opts);

        fake.OnSearch(
            "(sAMAccountName=alice)",
            MakeResponse(MakeEntry("CN=Alice,OU=Users,DC=corp,DC=example,DC=com")));
        fake.RootDse = null;  // simulates silent root-DSE failure
        fake.OnModify(
            "CN=Alice,OU=Users,DC=corp,DC=example,DC=com",
            MakeModifyResponse(ResultCode.Success));

        var result = await sut.PerformPasswordChangeAsync("alice", "OldPass1!", "NewPass1!");

        Assert.Null(result);
        Assert.Equal(1, fake.ModifyCallCount);
    }

    [Fact]
    public async Task PerformPasswordChangeAsync_EnforceMinPwdAgeFalse_SkipsCheck()
    {
        var opts = new PasswordChangeOptions
        {
            AllowedUsernameAttributes = new[] { "samaccountname" },
            EnforceMinimumPasswordAge = false,
            BaseDn = "DC=corp,DC=example,DC=com",
            ServiceAccountDn = "CN=svc,DC=corp,DC=example,DC=com",
            ServiceAccountPassword = "svcpw",
            LdapHostnames = new[] { "dc01.corp.example.com" },
            LdapPort = 636,
        };
        var (sut, fake) = Build(opts);

        fake.OnSearch(
            "(sAMAccountName=alice)",
            MakeResponse(MakeEntry("CN=Alice,OU=Users,DC=corp,DC=example,DC=com")));
        // Intentionally NOT registering an "(objectClass=user)" rule — if PreCheckMinPwdAge
        // ran, the FakeLdapSession would throw "no matching SearchRule". Asserting
        // SearchCallCount==1 confirms only the user-DN lookup ran.
        fake.OnModify(
            "CN=Alice,OU=Users,DC=corp,DC=example,DC=com",
            MakeModifyResponse(ResultCode.Success));

        var result = await sut.PerformPasswordChangeAsync("alice", "OldPass1!", "NewPass1!");

        Assert.Null(result);
        Assert.Equal(1, fake.ModifyCallCount);
        Assert.Equal(1, fake.SearchCallCount);
    }

    [Fact]
    public async Task PerformPasswordChangeAsync_MinPwdAgeViolation_ReturnsPasswordTooRecent()
    {
        var opts = new PasswordChangeOptions
        {
            AllowedUsernameAttributes = new[] { "samaccountname" },
            EnforceMinimumPasswordAge = true,
            BaseDn = "DC=corp,DC=example,DC=com",
            ServiceAccountDn = "CN=svc,DC=corp,DC=example,DC=com",
            ServiceAccountPassword = "svcpw",
            LdapHostnames = new[] { "dc01.corp.example.com" },
            LdapPort = 636,
        };
        var (sut, fake) = Build(opts);

        var pwdLastSet = DateTime.UtcNow.AddHours(-1).ToFileTimeUtc();  // 1 hour ago
        var minPwdAgeTicks = -TimeSpan.FromDays(1).Ticks;               // 1 day min age, negative per AD

        // FindUserDnAsync only requests the distinguishedName attribute, so PwdLastSet
        // here is never read by FindUserDn — it's read by the subsequent Base-scope
        // attribute fetch in PreCheckMinPwdAge (filter "(objectClass=user)").
        fake.OnSearch(
            "(sAMAccountName=alice)",
            MakeResponse(MakeEntry("CN=Alice,OU=Users,DC=corp,DC=example,DC=com")));
        fake.OnSearch(
            "(objectClass=user)",
            MakeResponse(MakeEntry("CN=Alice,OU=Users,DC=corp,DC=example,DC=com",
                (LdapAttributeNames.PwdLastSet, pwdLastSet.ToString()))));
        fake.RootDse = MakeEntry("",
            (LdapAttributeNames.MinPwdAge, minPwdAgeTicks.ToString()));

        var result = await sut.PerformPasswordChangeAsync("alice", "OldPass1!", "NewPass1!");

        Assert.NotNull(result);
        Assert.Equal(ApiErrorCode.PasswordTooRecentlyChanged, result!.ErrorCode);
        Assert.Equal(0, fake.ModifyCallCount);
    }

    // ----- GetUserEmail / GetDomainMaxPasswordAge / GetEffectivePasswordPolicyAsync ------------

    [Fact]
    public void GetUserEmail_Found_ReturnsMail()
    {
        // Scope to samaccountname only — default Build() registers all three
        // AllowedUsernameAttributes (sAM, UPN, mail) and FakeLdapSession throws on unmatched
        // filter. Using a single attribute keeps this test focused on the mail-resolution path.
        var opts = new PasswordChangeOptions
        {
            AllowedUsernameAttributes = new[] { "samaccountname" },
            BaseDn = "DC=corp,DC=example,DC=com",
            ServiceAccountDn = "CN=svc,DC=corp,DC=example,DC=com",
            ServiceAccountPassword = "svcpw",
            LdapHostnames = new[] { "dc01.corp.example.com" },
            LdapPort = 636,
        };
        var (sut, fake) = Build(opts);
        fake.OnSearch(
            "(samAccountName=alice)",
            MakeResponse(MakeEntry("CN=Alice,OU=Users,DC=corp,DC=example,DC=com",
                (LdapAttributeNames.Mail, "alice@corp.example.com"))));

        var email = sut.GetUserEmail("alice");

        Assert.Equal("alice@corp.example.com", email);
    }

    [Fact]
    public void GetUserEmail_NotFound_ReturnsNull()
    {
        // Default Build() iterates sAM → UPN → mail; register an empty rule for each so the
        // fake doesn't throw on the second/third lookup. Mirrors PerformPasswordChangeAsync_UserNotFound.
        var (sut, fake) = Build();
        fake.OnSearch("(sAMAccountName=ghost)",     MakeResponse());
        fake.OnSearch("(userPrincipalName=ghost)",  MakeResponse());
        fake.OnSearch("(mail=ghost)",               MakeResponse());

        var email = sut.GetUserEmail("ghost");

        Assert.Null(email);
    }

    [Fact]
    public void GetUserEmail_FoundButNoMail_ReturnsNullWithoutFallback()
    {
        // First match wins — once an entry is found via any AllowedUsernameAttributes filter,
        // we must NOT fall through to the remaining attribute searches. Confirms there is
        // exactly one search call and that UPN/mail rules are never invoked (FakeLdapSession
        // throws on unmatched filters, so omitting them asserts they aren't called).
        var (sut, fake) = Build();
        fake.OnSearch(
            "(sAMAccountName=alice)",
            MakeResponse(MakeEntry("CN=Alice,OU=Users,DC=corp,DC=example,DC=com")));

        var email = sut.GetUserEmail("alice");

        Assert.Null(email);
        Assert.Equal(1, fake.SearchCallCount);
    }

    [Fact]
    public void GetDomainMaxPasswordAge_ReadsRootDseMaxPwdAge()
    {
        var (sut, fake) = Build();
        var maxPwdAge = -TimeSpan.FromDays(90).Ticks;
        fake.RootDse = MakeEntry("",
            (LdapAttributeNames.MaxPwdAge, maxPwdAge.ToString()));

        var age = sut.GetDomainMaxPasswordAge();

        Assert.Equal(TimeSpan.FromDays(90), age);
    }

    [Fact]
    public void GetDomainMaxPasswordAge_NoRootDse_ReturnsMaxValue()
    {
        var (sut, fake) = Build();
        fake.RootDse = null;

        var age = sut.GetDomainMaxPasswordAge();

        Assert.Equal(TimeSpan.MaxValue, age);
    }

    [Fact]
    public async Task GetEffectivePasswordPolicyAsync_ReturnsPolicyFromRootDse()
    {
        var (sut, fake) = Build();
        fake.RootDse = MakeEntry("",
            (LdapAttributeNames.MinPwdLength, "8"),
            (LdapAttributeNames.MaxPwdAge, (-TimeSpan.FromDays(42).Ticks).ToString()));

        var policy = await sut.GetEffectivePasswordPolicyAsync();

        Assert.NotNull(policy);
        // PasswordPolicy property is named MinLength (not MinPasswordLength) — see PassReset.Common.PasswordPolicy.
        Assert.Equal(8, policy!.MinLength);
        Assert.Equal(42, policy.MaxAgeDays);
    }

    [Fact]
    public async Task GetEffectivePasswordPolicyAsync_NoRootDse_ReturnsNull()
    {
        var (sut, fake) = Build();
        fake.RootDse = null;

        var policy = await sut.GetEffectivePasswordPolicyAsync();

        Assert.Null(policy);
    }
}

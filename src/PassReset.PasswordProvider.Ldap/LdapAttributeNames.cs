namespace PassReset.PasswordProvider.Ldap;

/// <summary>
/// Canonical Active Directory LDAP attribute names used by <see cref="LdapPasswordChangeProvider"/>.
/// Kept as named constants so misspellings surface at compile time rather than as silent empty results.
/// </summary>
internal static class LdapAttributeNames
{
    public const string SamAccountName       = "samAccountName";
    public const string UserPrincipalName    = "userPrincipalName";
    public const string Mail                 = "mail";
    public const string DistinguishedName    = "distinguishedName";
    public const string UnicodePwd           = "unicodePwd";
    public const string PwdLastSet           = "pwdLastSet";
    public const string MinPwdAge            = "minPwdAge";
    public const string MaxPwdAge            = "maxPwdAge";
    public const string MinPwdLength         = "minPwdLength";
    public const string UserAccountControl   = "userAccountControl";
    public const string UacComputed          = "msDS-User-Account-Control-Computed";
    public const string TokenGroups          = "tokenGroups";
    public const string MemberOf             = "memberOf";
    public const string ObjectClass          = "objectClass";
    public const string DisplayName          = "displayName";
    public const string Manager              = "manager";
}

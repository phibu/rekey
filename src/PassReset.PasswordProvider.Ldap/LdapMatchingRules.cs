namespace PassReset.PasswordProvider.Ldap;

internal static class LdapMatchingRules
{
    /// <summary>
    /// LDAP_MATCHING_RULE_IN_CHAIN — performs recursive group-membership evaluation on
    /// linked attributes (e.g. memberOf). Used to resolve nested group memberships in a
    /// single search. AD-specific extension; supported by Samba 4 in AD-DC mode.
    /// </summary>
    public const string InChain = "1.2.840.113556.1.4.1941";
}

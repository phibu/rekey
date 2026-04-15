using PassReset.Common;

namespace PassReset.Tests.Web.Helpers;

/// <summary>
/// The Levenshtein implementation lives on <see cref="IPasswordChangeProvider.MeasureNewPasswordDistance"/>
/// as a default interface method. This test exercises it through a tiny implementor.
/// </summary>
public class LevenshteinTests
{
    private sealed class DistanceOnlyProvider : IPasswordChangeProvider
    {
        public Task<ApiErrorItem?> PerformPasswordChangeAsync(string u, string c, string n) =>
            Task.FromResult<ApiErrorItem?>(null);
        public string? GetUserEmail(string u) => null;
        public IEnumerable<(string Username, string Email, DateTime? PasswordLastSet)> GetUsersInGroup(string g) => [];
        public TimeSpan GetDomainMaxPasswordAge() => TimeSpan.MaxValue;
    }

    private static int Distance(string a, string b) =>
        ((IPasswordChangeProvider)new DistanceOnlyProvider()).MeasureNewPasswordDistance(a, b);

    [Theory]
    [InlineData("", "", 0)]
    [InlineData("abc", "abc", 0)]
    [InlineData("", "abc", 3)]
    [InlineData("abc", "", 3)]
    [InlineData("abc", "abd", 1)]    // substitute
    [InlineData("abc", "abcd", 1)]   // insert
    [InlineData("abcd", "abc", 1)]   // delete
    [InlineData("kitten", "sitting", 3)]
    [InlineData("flaw", "lawn", 2)]
    [InlineData("abc", "ABC", 3)]    // case sensitive by design
    public void Distance_ClassicCases(string a, string b, int expected)
    {
        Assert.Equal(expected, Distance(a, b));
    }

    [Fact]
    public void Distance_IsSymmetric()
    {
        Assert.Equal(Distance("password1", "password2"), Distance("password2", "password1"));
    }
}

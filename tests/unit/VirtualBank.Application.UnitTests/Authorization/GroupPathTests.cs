using VirtualBank.Application.Authorization;
using VirtualBank.Application.Authorization.Principals;
using Xunit;

namespace VirtualBank.Application.UnitTests.Authorization;

public sealed class GroupPathTests
{
    [Theory]
    [InlineData("/Credit")]
    [InlineData("/Credit/Analysis")]
    [InlineData("/Security/Audit")]
    public void Valid_paths_round_trip(string raw)
    {
        Assert.Equal(raw, GroupPath.Parse(raw).Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Credit")]
    [InlineData("/")]
    [InlineData("//Credit")]
    [InlineData("/Credit/")]
    [InlineData("/Credit//Analysis")]
    public void Malformed_paths_are_rejected(string raw)
    {
        Assert.False(GroupPath.TryParse(raw, out _));
    }

    [Fact]
    public void Expansion_yields_self_and_all_ancestors()
    {
        var expanded = GroupPath.Parse("/Credit/Analysis").SelfAndAncestors().Select(g => g.Value).ToList();
        Assert.Equal(new[] { "/Credit/Analysis", "/Credit" }, expanded);
    }

    [Fact]
    public void Matching_is_segment_aware()
    {
        Assert.False(GroupPath.Parse("/Creditors").IsSameOrDescendantOf(GroupPath.Parse("/Credit")));
        Assert.DoesNotContain(GroupPath.Parse("/Creditors/Analysis").SelfAndAncestors(), g => g.Equals(GroupPath.Parse("/Credit")));
    }

    [Fact]
    public void Matching_is_directional()
    {
        // A member of the child group is in the parent; a member of the parent is not in the child.
        Assert.True(GroupPath.Parse("/Credit/Analysis").IsSameOrDescendantOf(GroupPath.Parse("/Credit")));
        Assert.False(GroupPath.Parse("/Credit").IsSameOrDescendantOf(GroupPath.Parse("/Credit/Analysis")));
    }

    [Fact]
    public void Comparison_is_case_sensitive()
    {
        Assert.False(GroupPath.Parse("/credit").Equals(GroupPath.Parse("/Credit")));
    }
}

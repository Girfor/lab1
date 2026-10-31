using Campus.AccessControl.Api.Models;
using Xunit;

namespace Campus.AccessControl.SanityTests;

public sealed class AccessPassRulesTests
{
    [Fact]
    public void ValidPassAccepted()
    {
        var now = DateTimeOffset.UtcNow;
        var request = new CreateAccessPassRequest(
            Guid.NewGuid(), "CARD-101", now.AddHours(2));
        Assert.True(AccessPassRules.IsValid(request, now));
    }

    [Fact]
    public void ExpiredPassRejected()
    {
        var now = DateTimeOffset.UtcNow;
        var request = new CreateAccessPassRequest(
            Guid.NewGuid(), "CARD-101", now.AddMinutes(-1));
        Assert.False(AccessPassRules.IsValid(request, now));
    }

    [Fact]
    public void EmptyPersonRejected()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.False(AccessPassRules.IsValid(
            new CreateAccessPassRequest(Guid.Empty, "CARD-101", now.AddDays(1)), now));
    }
}

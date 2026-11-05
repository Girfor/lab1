using System.Globalization;
using EnergyMeter.Refactored.Models;
using EnergyMeter.Refactored.Services;
using FluentAssertions;
using Xunit;

namespace EnergyMeter.PR6.Tests;

public sealed class ReadingValidationTests
{
    [Theory]
    [InlineData("-0.001", false)]
    [InlineData("0", true)]
    [InlineData("999999.999", true)]
    [InlineData("1000000", false)]
    [InlineData("1.0001", false)]
    public async Task Values_are_validated(string text, bool expected)
    {
        var value = decimal.Parse(text, CultureInfo.InvariantCulture);

        var service = new InMemoryReadingService();
        var principal = TestPrincipal();
        var result = await service.AddAsync(
            principal, InMemoryReadingService.DemoMeterId,
            new AddReadingRequest
            {
                Kwh = value,
                MeasuredAt = DateTimeOffset.UtcNow
            }, null, CancellationToken.None);

        (result.Status == AddStatus.Created).Should().Be(expected);
    }

    private static System.Security.Claims.ClaimsPrincipal TestPrincipal()
    {
        var identity = new System.Security.Claims.ClaimsIdentity(
            new[]
            {
                new System.Security.Claims.Claim(
                    System.Security.Claims.ClaimTypes.Name, "operator"),
                new System.Security.Claims.Claim(
                    System.Security.Claims.ClaimTypes.Role, "Operator")
            }, "UnitTest");

        return new System.Security.Claims.ClaimsPrincipal(identity);
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

public class CompatibilityTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    public CompatibilityTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task V1MaintainsLegacyShape_AndV2AddsQuality()
    {
        Environment.SetEnvironmentVariable("ENERGYMETER_API_KEY", "test-key-015");
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-API-Key", "test-key-015");
        var reading = new ReadingV1(Guid.NewGuid(), 15.25m, DateTimeOffset.UtcNow);
        var posted = await client.PostAsJsonAsync("/api/v1/readings", reading);
        Assert.Equal(HttpStatusCode.Created, posted.StatusCode);

        using var v1 = JsonDocument.Parse(await client.GetStringAsync("/api/v1/readings"));
        var first = v1.RootElement.EnumerateArray().Single();
        Assert.True(first.TryGetProperty("meterId", out _));
        Assert.True(first.TryGetProperty("kwh", out _));
        Assert.True(first.TryGetProperty("timestampUtc", out _));
        Assert.False(first.TryGetProperty("quality", out _));

        using var v2 = JsonDocument.Parse(await client.GetStringAsync("/api/v2/readings"));
        Assert.True(v2.RootElement.EnumerateArray().Single()
            .TryGetProperty("quality", out _));
    }

    [Fact]
    public async Task UnsafeAndDebugEndpointsAreNotPublic()
    {
        using var client = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync("/swagger/index.html")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/api/v1/readings", new ReadingV1(
                Guid.NewGuid(), 2m, DateTimeOffset.UtcNow))).StatusCode);
    }
}

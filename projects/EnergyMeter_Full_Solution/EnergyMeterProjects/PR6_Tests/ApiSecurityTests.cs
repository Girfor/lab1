using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EnergyMeter.Refactored.Services;
using FluentAssertions;
using Xunit;

namespace EnergyMeter.PR6.Tests;

public sealed class ApiSecurityTests : IClassFixture<EnergyApiFactory>
{
    private readonly EnergyApiFactory _factory;

    public ApiSecurityTests(EnergyApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Swagger_is_not_available_in_Production()
    {
        using var client = _factory.CreateHttpsClient();

        var response = await client.GetAsync("/swagger/index.html");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Changing_data_without_csrf_token_is_rejected()
    {
        using var client = _factory.CreateHttpsClient();

        var response = await client.PostAsJsonAsync(
            ReadingsUrl, new
            {
                kwh = 12.5m,
                measuredAt = DateTimeOffset.UtcNow
            });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Valid_post_with_csrf_token_creates_reading()
    {
        using var client = _factory.CreateHttpsClient();
        var token = await RequestCsrfToken(client);

        using var request = NewReadingRequest(
            token, 12.5m, Guid.NewGuid().ToString("N"));

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Boundary_above_limit_is_rejected()
    {
        using var client = _factory.CreateHttpsClient();
        var token = await RequestCsrfToken(client);

        using var request = NewReadingRequest(
            token, 1000000m, Guid.NewGuid().ToString("N"));

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Idempotency_key_prevents_duplicate_reading()
    {
        using var client = _factory.CreateHttpsClient();
        var token = await RequestCsrfToken(client);
        var key = Guid.NewGuid().ToString("N");

        using var firstRequest = NewReadingRequest(token, 30m, key);
        using var secondRequest = NewReadingRequest(token, 30m, key);

        var first = await client.SendAsync(firstRequest);
        var second = await client.SendAsync(secondRequest);

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        second.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static readonly string ReadingsUrl =
        $"/api/meters/{InMemoryReadingService.DemoMeterId}/readings";

    private static HttpRequestMessage NewReadingRequest(
        string token, decimal kwh, string key)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, ReadingsUrl)
        {
            Content = JsonContent.Create(new
            {
                kwh,
                measuredAt = DateTimeOffset.UtcNow
            })
        };
        request.Headers.Add("X-CSRF-TOKEN", token);
        request.Headers.Add("Idempotency-Key", key);
        return request;
    }

    private static async Task<string> RequestCsrfToken(HttpClient client)
    {
        using var response = await client.GetAsync("/api/auth/csrf");
        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());

        return document.RootElement
            .GetProperty("token").GetString()
            ?? throw new InvalidOperationException("CSRF token is missing.");
    }
}

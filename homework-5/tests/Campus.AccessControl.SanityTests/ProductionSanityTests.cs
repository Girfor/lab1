using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Campus.AccessControl.SanityTests;

public sealed class ProductionSanityTests
{
    [Fact]
    [Trait("Category", "ProductionSanity")]
    public async Task TestPassCanBeCreatedReadAndImmediatelyRemoved()
    {
        // Only execute with an explicit production/staging endpoint and secret.
        var baseUrl = Environment.GetEnvironmentVariable("CAMPUS_SANITY_BASE_URL");
        var apiKey = Environment.GetEnvironmentVariable("CAMPUS_SANITY_API_KEY");
        Assert.False(string.IsNullOrWhiteSpace(baseUrl),
            "CAMPUS_SANITY_BASE_URL is required for an intentional sanity run.");
        Assert.False(string.IsNullOrWhiteSpace(apiKey),
            "CAMPUS_SANITY_API_KEY is required for an intentional sanity run.");

        using var client = new HttpClient { BaseAddress = new Uri(baseUrl!),
            Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.Add("X-Api-Key", apiKey!);

        Guid? createdId = null;
        Exception? scenarioError = null;
        try
        {
            var request = new
            {
                personId = Guid.NewGuid(),
                badgeCode = ($"SANITY-{Guid.NewGuid():N}")[..32],
                validUntil = DateTimeOffset.UtcNow.AddMinutes(10),
                isTest = true
            };

            using var create = await client.PostAsJsonAsync("api/access-passes", request);
            Assert.Equal(HttpStatusCode.Created, create.StatusCode);

            using var payload = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
            createdId = payload.RootElement.GetProperty("id").GetGuid();
            Assert.True(createdId != Guid.Empty);

            using var read = await client.GetAsync($"api/access-passes/{createdId:D}");
            Assert.Equal(HttpStatusCode.OK, read.StatusCode);
            using var readPayload = JsonDocument.Parse(await read.Content.ReadAsStringAsync());
            Assert.True(readPayload.RootElement.GetProperty("isTest").GetBoolean());
        }
        catch (Exception ex)
        {
            scenarioError = ex;
        }
        finally
        {
            if (createdId.HasValue)
            {
                // Cleanup runs even when the GET/assertion failed.
                using var delete = await client.DeleteAsync(
                    $"api/access-passes/{createdId.Value:D}/test");
                Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
                using var probe = await client.GetAsync(
                    $"api/access-passes/{createdId.Value:D}");
                Assert.Equal(HttpStatusCode.NotFound, probe.StatusCode);
            }
        }

        if (scenarioError is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(scenarioError).Throw();
    }
}

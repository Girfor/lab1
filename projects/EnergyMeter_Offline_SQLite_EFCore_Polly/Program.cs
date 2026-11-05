using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Polly;
using Polly.Retry;

// This sample queues readings locally and retries transient HTTP errors.
var options = new DbContextOptionsBuilder<BufferContext>()
    .UseSqlite("Data Source=energymeter-buffer.sqlite").Options;
await using var db = new BufferContext(options);
await db.Database.EnsureCreatedAsync(); // educational demo; production: EF migrations

if (args.Length == 0) { Console.WriteLine("enqueue <meter-guid> <kwh> | flush <base-url> <api-key>"); return; }
if (args[0] == "enqueue" && args.Length == 3 &&
    Guid.TryParse(args[1], out var meterId) && decimal.TryParse(args[2], out var kwh) && kwh >= 0) {
    db.Outbox.Add(new PendingReading { Id = Guid.NewGuid(), MeterId = meterId,
        Kwh = kwh, TimestampUtc = DateTimeOffset.UtcNow });
    await db.SaveChangesAsync();
    Console.WriteLine("Reading queued");
} else if (args[0] == "flush" && args.Length == 3) {
    using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
    client.DefaultRequestHeaders.Add("X-API-Key", args[2]);
    var retry = new ResiliencePipelineBuilder<HttpResponseMessage>()
        .AddRetry(new RetryStrategyOptions<HttpResponseMessage> {
            MaxRetryAttempts = 3, Delay = TimeSpan.FromSeconds(1), UseJitter = true,
            BackoffType = DelayBackoffType.Exponential,
            ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                .Handle<HttpRequestException>()
                .HandleResult(response => (int)response.StatusCode >= 500 ||
                    response.StatusCode == HttpStatusCode.RequestTimeout ||
                    (int)response.StatusCode == 429)
        }).Build();
    foreach (var item in await db.Outbox.OrderBy(x => x.TimestampUtc).Take(100).ToListAsync()) {
        try {
            client.DefaultRequestHeaders.Remove("X-Event-Id");
            client.DefaultRequestHeaders.Add("X-Event-Id", item.Id.ToString());
            using var response = await retry.ExecuteAsync(async ct =>
                await client.PostAsJsonAsync(args[1].TrimEnd('/') + "/api/v1/readings",
                    new { item.MeterId, item.Kwh, item.TimestampUtc }, ct));
            if (!response.IsSuccessStatusCode) { Console.Error.WriteLine(response.StatusCode); break; }
            db.Outbox.Remove(item);
            await db.SaveChangesAsync();
        } catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) {
            Console.Error.WriteLine("Network unavailable: " + ex.Message); break;
        }
    }
} else Console.Error.WriteLine("Invalid arguments");

public sealed class PendingReading {
    public Guid Id { get; set; }
    public Guid MeterId { get; set; }
    public decimal Kwh { get; set; }
    public DateTimeOffset TimestampUtc { get; set; }
}
public sealed class BufferContext(DbContextOptions<BufferContext> options) : DbContext(options) {
    public DbSet<PendingReading> Outbox => Set<PendingReading>();
    protected override void OnModelCreating(ModelBuilder b) {
        b.Entity<PendingReading>().HasKey(x => x.Id);
        b.Entity<PendingReading>().HasIndex(x => x.TimestampUtc);
    }
}

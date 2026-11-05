using System.Net;
using System.Net.Http.Json;
using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Polly;
using Polly.Retry;

Console.OutputEncoding = Encoding.UTF8;
if (args.Length == 0)
{
    Console.WriteLine("ENERGYMETER OFFLINE — ДЕМОНСТРАЦИЯ (ВАРИАНТ 15)");
    Console.WriteLine(new string('=', 60));
    try
    {
        bool passed = await OfflineDemo.RunAsync();
        Environment.ExitCode = passed ? 0 : 1;
    }
    catch (Exception error)
    {
        Console.Error.WriteLine("Ошибка демонстрации: " + error);
        Environment.ExitCode = 1;
    }
    if (!Console.IsInputRedirected)
    {
        Console.WriteLine();
        Console.Write("Нажмите Enter, чтобы закрыть окно...");
        Console.ReadLine();
    }
    return;
}

// This sample queues readings locally and retries transient HTTP errors.
var options = new DbContextOptionsBuilder<BufferContext>()
    .UseSqlite("Data Source=energymeter-buffer.sqlite").Options;
await using var db = new BufferContext(options);
await db.Database.EnsureCreatedAsync(); // educational demo; production: EF migrations

if (args[0] == "enqueue" && args.Length == 3 &&
    Guid.TryParse(args[1], out var meterId) &&
    (decimal.TryParse(args[2], NumberStyles.Number, CultureInfo.InvariantCulture, out var kwh)
      || decimal.TryParse(args[2], NumberStyles.Number, CultureInfo.CurrentCulture, out kwh)) && kwh >= 0) {
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
    // DateTimeOffset ORDER BY is not reliably translated by the SQLite provider.
    var pending = (await db.Outbox.ToListAsync())
        .OrderBy(x => x.TimestampUtc).Take(100).ToList();
    foreach (var item in pending) {
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
public sealed class ReceivedReading {
    public Guid Id { get; set; }
    public Guid EventId { get; set; }
    public Guid MeterId { get; set; }
    public decimal Kwh { get; set; }
    public DateTimeOffset TimestampUtc { get; set; }
}
public sealed class BufferContext(DbContextOptions<BufferContext> options) : DbContext(options) {
    public DbSet<PendingReading> Outbox => Set<PendingReading>();
    public DbSet<ReceivedReading> ReceivedEvents => Set<ReceivedReading>();
    protected override void OnModelCreating(ModelBuilder b) {
        b.Entity<PendingReading>().HasKey(x => x.Id);
        b.Entity<PendingReading>().HasIndex(x => x.TimestampUtc);
        b.Entity<ReceivedReading>().HasKey(x => x.Id);
        b.Entity<ReceivedReading>().HasIndex(x => x.EventId).IsUnique();
    }
}

// Учебный запуск: настоящая SQLite-база, EF Core и повторные попытки Polly,
// но сервер имитируется локально (никаких HTTP-запросов во внешнюю сеть).
internal static class OfflineDemo
{
    public static async Task<bool> RunAsync()
    {
        string root = Path.Combine(Environment.CurrentDirectory, "DemoOutput",
            DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
        Directory.CreateDirectory(root);
        string databasePath = Path.Combine(root, "energymeter-demo.sqlite");
        var options = new DbContextOptionsBuilder<BufferContext>()
            .UseSqlite($"Data Source={databasePath}").Options;

        int queuedBefore;
        int queuedOffline;
        int receivedOffline;
        int retryCount = 0;
        int queuedAfter;
        int receivedAfter;
        bool duplicateBlocked = false;

        await using (var demoDb = new BufferContext(options))
        {
            await demoDb.Database.EnsureCreatedAsync();
            Console.WriteLine("[1] База SQLite создана: " +
                (File.Exists(databasePath) ? "УСПЕШНО" : "ОШИБКА"));

            Guid meter = Guid.NewGuid();
            for (int i = 0; i < 5; i++)
            {
                demoDb.Outbox.Add(new PendingReading
                {
                    Id = Guid.NewGuid(),
                    MeterId = meter,
                    Kwh = 100.5m + i * 3.25m,
                    TimestampUtc = DateTimeOffset.UtcNow.AddMinutes(i)
                });
            }
            await demoDb.SaveChangesAsync();
            queuedBefore = await demoDb.Outbox.CountAsync();
            Console.WriteLine($"[2] Сохранено показаний: {queuedBefore}");

            // Сеть «отключена»: данные остаются в SQLite, отправки нет.
            queuedOffline = await demoDb.Outbox.CountAsync();
            receivedOffline = await demoDb.ReceivedEvents.CountAsync();
            bool offlineOk = queuedOffline == 5 && receivedOffline == 0;
            Console.WriteLine("[3] Офлайн-буфер: " +
                (offlineOk ? "УСПЕШНО — 5 показаний сохранены" : "ОШИБКА"));

            var rows = (await demoDb.Outbox.ToListAsync())
                .OrderBy(x => x.TimestampUtc).ToList();
            var csv = new List<string> { "EventId;MeterId;Kwh;TimestampUtc" };
            csv.AddRange(rows.Select(x =>
                $"{x.Id};{x.MeterId};{x.Kwh.ToString(CultureInfo.InvariantCulture)};{x.TimestampUtc:O}"));
            await File.WriteAllLinesAsync(Path.Combine(root, "queued-before-sync.csv"),
                csv, Encoding.UTF8);

            // Первое событие дважды получает симулированный HTTP 503.
            // Настоящий Polly перезапускает делегат до успешного ответа.
            var pipeline = new ResiliencePipelineBuilder<HttpResponseMessage>()
                .AddRetry(new RetryStrategyOptions<HttpResponseMessage>
                {
                    MaxRetryAttempts = 3,
                    Delay = TimeSpan.FromMilliseconds(150),
                    BackoffType = DelayBackoffType.Exponential,
                    UseJitter = false,
                    ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                        .HandleResult(r => r.StatusCode == HttpStatusCode.ServiceUnavailable)
                }).Build();

            for (int i = 0; i < rows.Count; i++)
            {
                var item = rows[i];
                int attemptsForItem = 0;
                using var response = await pipeline.ExecuteAsync(async ct =>
                {
                    await Task.Delay(10, ct);
                    attemptsForItem++;
                    if (i == 0 && attemptsForItem <= 2)
                        return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
                    return new HttpResponseMessage(HttpStatusCode.OK);
                });

                if (!response.IsSuccessStatusCode)
                    throw new InvalidOperationException("Синхронизация не удалась");
                if (i == 0) retryCount = attemptsForItem - 1;

                // Таблица ReceivedEvents имитирует прием на сервере.
                demoDb.ReceivedEvents.Add(new ReceivedReading
                {
                    Id = Guid.NewGuid(), EventId = item.Id,
                    MeterId = item.MeterId, Kwh = item.Kwh,
                    TimestampUtc = item.TimestampUtc
                });
                demoDb.Outbox.Remove(item);
                await demoDb.SaveChangesAsync();
            }
            Console.WriteLine($"[4] Polly повторил запрос после HTTP 503: {retryCount} раза");

            queuedAfter = await demoDb.Outbox.CountAsync();
            receivedAfter = await demoDb.ReceivedEvents.CountAsync();
            bool synchronized = queuedAfter == 0 && receivedAfter == 5;
            Console.WriteLine("[5] Синхронизация после восстановления сети: " +
                (synchronized ? "УСПЕШНО — очередь пуста, 5 получено" : "ОШИБКА"));

            // UNIQUE(EventId) на уровне самой SQLite запрещает повторы.
            var first = await demoDb.ReceivedEvents.FirstAsync();
            var duplicate = new ReceivedReading
            {
                Id = Guid.NewGuid(), EventId = first.EventId,
                MeterId = first.MeterId, Kwh = first.Kwh,
                TimestampUtc = first.TimestampUtc
            };
            demoDb.ReceivedEvents.Add(duplicate);
            try
            {
                await demoDb.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                duplicateBlocked = true;
                demoDb.Entry(duplicate).State = EntityState.Detached;
            }
            Console.WriteLine("[6] Защита от повторного EventId: " +
                (duplicateBlocked ? "УСПЕШНО" : "ОШИБКА"));
        }

        // Проверяем физический файл БД после закрытия первого контекста.
        await using var reopened = new BufferContext(options);
        int persisted = await reopened.ReceivedEvents.CountAsync();
        bool persistent = persisted == 5;
        Console.WriteLine("[7] Повторное открытие базы SQLite: " +
            (persistent ? "УСПЕШНО — данные сохранены" : "ОШИБКА"));

        bool allPassed = File.Exists(databasePath) && queuedBefore == 5 &&
            queuedOffline == 5 && receivedOffline == 0 && retryCount == 2 &&
            queuedAfter == 0 && receivedAfter == 5 && duplicateBlocked && persistent;
        string report = Path.Combine(root, "demo-report.txt");
        await File.WriteAllLinesAsync(report, new[]
        {
            "EnergyMeter System — Offline Buffer / вариант 15",
            $"Дата запуска: {DateTime.Now:dd.MM.yyyy HH:mm:ss}",
            $"Создание SQLite: {(File.Exists(databasePath) ? "PASS" : "FAIL")}",
            $"Запись 5 показаний: {(queuedBefore == 5 ? "PASS" : "FAIL")}",
            $"Офлайн-буфер: {(queuedOffline == 5 && receivedOffline == 0 ? "PASS" : "FAIL")}",
            $"Два повтора Polly после HTTP 503: {(retryCount == 2 ? "PASS" : "FAIL")}",
            $"Синхронизация 5 событий: {(queuedAfter == 0 && receivedAfter == 5 ? "PASS" : "FAIL")}",
            $"SQLite UNIQUE(EventId): {(duplicateBlocked ? "PASS" : "FAIL")}",
            $"Сохранность БД после открытия: {(persistent ? "PASS" : "FAIL")}",
            $"ИТОГ: {(allPassed ? "PASS" : "FAIL")}",
            "Сервер симулирован локально; настоящая сеть не использовалась."
        }, Encoding.UTF8);

        Console.WriteLine(new string('-', 60));
        Console.WriteLine("ИТОГ: " + (allPassed ? "ВСЕ ДЕМО-ТЕСТЫ ПРОЙДЕНЫ" : "ЕСТЬ ОШИБКИ"));
        Console.WriteLine("Папка демонстрации: " + root);
        Console.WriteLine("Отчёт: " + report);
        Console.WriteLine("Примечание: реальный сервер не использовался.");
        return allPassed;
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

internal sealed record DemoCheck(string Name, string Status, string Details);

internal sealed class DemoStatus
{
    public DemoStatus() { }

    private readonly object _gate = new();
    private readonly List<DemoCheck> _checks = new();
    private bool _done;
    private string? _reportPath;

    public void Add(DemoCheck value)
    {
        lock (_gate) _checks.Add(value);
    }

    public void Complete(string reportPath)
    {
        lock (_gate)
        {
            _reportPath = reportPath;
            _done = true;
        }
    }

    public object Snapshot()
    {
        lock (_gate)
        {
            var copy = _checks.ToArray();
            return new
            {
                done = _done,
                allPassed = _done && copy.Length == 18 && copy.All(x => x.Status == "PASS"),
                reportPath = _reportPath,
                checks = copy
            };
        }
    }

    public DemoCheck[] Checks()
    {
        lock (_gate) return _checks.ToArray();
    }
}

internal static class DemoRunner
{
    public static async Task RunAsync(IServiceProvider services, string contentRoot, string rootUrl)
    {
        var secrets = services.GetRequiredService<DemoSecrets>();
        var state = services.GetRequiredService<DemoStatus>();
        var folder = Path.Combine(contentRoot, "DemoOutput", DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
        Directory.CreateDirectory(folder);

        using var client = new HttpClient { BaseAddress = new Uri(rootUrl), Timeout = TimeSpan.FromSeconds(8) };
        using var cookieHandler = new HttpClientHandler
        {
            UseCookies = true,
            CookieContainer = new CookieContainer(),
            AllowAutoRedirect = false
        };
        using var browser = new HttpClient(cookieHandler)
        {
            BaseAddress = new Uri(rootUrl), Timeout = TimeSpan.FromSeconds(8)
        };

        Console.WriteLine(new string('=', 60));
        Console.WriteLine("Выполняются локальные HTTP-проверки API, Swagger и CSRF...");

        async Task Check(string title, Func<Task<bool>> test, string description)
        {
            try
            {
                bool success = await test();
                state.Add(new DemoCheck(title, success ? "PASS" : "FAIL", description));
                Console.WriteLine($"[{title}]: {(success ? "PASS" : "FAIL")}");
            }
            catch (Exception ex)
            {
                var details = $"{description} — {ex.GetType().Name}: {ex.Message}";
                state.Add(new DemoCheck(title, "FAIL", details));
                Console.WriteLine($"[{title}]: FAIL ({ex.GetType().Name})");
            }
        }

        async Task<HttpResponseMessage> SendReadingAsync(ReadingV1 item, string? apiKey,
            string? eventId = null)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/readings")
            {
                Content = JsonContent.Create(item)
            };
            if (apiKey is not null) req.Headers.Add("X-API-Key", apiKey);
            if (eventId is not null) req.Headers.Add("X-Event-Id", eventId);
            return await client.SendAsync(req);
        }

        var newReading = new ReadingV1(Guid.NewGuid(), 24.5m, DateTimeOffset.UtcNow);
        var eventId = Guid.NewGuid().ToString();
        string? csrfToken = null;

        await Check("01. Стартовая страница", async () =>
        {
            using var result = await client.GetAsync("/demo");
            return result.StatusCode == HttpStatusCode.OK &&
                   (await result.Content.ReadAsStringAsync()).Contains("EnergyMeter", StringComparison.Ordinal);
        }, "Главная страница доступна на локальном сервере");

        await Check("02. GET API v1", async () =>
        {
            using var result = await client.GetAsync("/api/v1/readings");
            if (result.StatusCode != HttpStatusCode.OK) return false;
            using var doc = JsonDocument.Parse(await result.Content.ReadAsStringAsync());
            return doc.RootElement.ValueKind == JsonValueKind.Array &&
                   doc.RootElement.GetArrayLength() == 0;
        }, "Первый API возвращает пустой список до добавления данных");

        await Check("03. GET API v2", async () =>
        {
            using var result = await client.GetAsync("/api/v2/readings");
            return result.StatusCode == HttpStatusCode.OK;
        }, "Вторая версия API доступна параллельно первой");

        await Check("04. POST без API-ключа", async () =>
        {
            using var result = await SendReadingAsync(newReading, null);
            return result.StatusCode == HttpStatusCode.Unauthorized;
        }, "Неавторизованная запись отклоняется статусом 401");

        await Check("05. Неверный API-ключ", async () =>
        {
            using var result = await SendReadingAsync(newReading, "incorrect-key");
            return result.StatusCode == HttpStatusCode.Unauthorized;
        }, "Проверка неверного ключа возвращает 401");

        await Check("06. Авторизованная запись", async () =>
        {
            using var result = await SendReadingAsync(newReading, secrets.CollectorKey, eventId);
            return result.StatusCode == HttpStatusCode.Created;
        }, "Случайный локальный ключ разрешает добавление показания");

        await Check("07. Идемпотентность EventId", async () =>
        {
            using var repeated = await SendReadingAsync(newReading, secrets.CollectorKey, eventId);
            using var list = await client.GetAsync("/api/v1/readings");
            if (repeated.StatusCode != HttpStatusCode.Created || list.StatusCode != HttpStatusCode.OK)
                return false;
            using var doc = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
            return doc.RootElement.GetArrayLength() == 1;
        }, "Повторная отправка того же события не создаёт дубликат");

        await Check("08. Проверка отрицательных показаний", async () =>
        {
            var invalid = newReading with { Kwh = -1m };
            using var result = await SendReadingAsync(invalid, secrets.CollectorKey);
            return result.StatusCode == HttpStatusCode.BadRequest;
        }, "Некорректное значение kWh отклонено статусом 400");

        await Check("09. Неверный формат EventId", async () =>
        {
            using var result = await SendReadingAsync(newReading, secrets.CollectorKey, "not-a-guid");
            return result.StatusCode == HttpStatusCode.BadRequest;
        }, "Не-GUID в заголовке X-Event-Id отклонён");

        await Check("10. Совместимость API v2", async () =>
        {
            using var result = await client.GetAsync("/api/v2/readings");
            if (result.StatusCode != HttpStatusCode.OK) return false;
            using var doc = JsonDocument.Parse(await result.Content.ReadAsStringAsync());
            return doc.RootElement.GetArrayLength() == 1 &&
                   doc.RootElement[0].GetProperty("quality").GetString() == "measured";
        }, "Версия v2 содержит дополнительное поле quality");

        await Check("11. Swagger доступен при разработке", async () =>
        {
            using var result = await client.GetAsync("/swagger/index.html");
            return result.StatusCode == HttpStatusCode.OK &&
                   (await result.Content.ReadAsStringAsync()).Contains("swagger", StringComparison.OrdinalIgnoreCase);
        }, "Swagger UI включён только для среды Development");

        await Check("12. Swagger отключён в Production", () => Task.FromResult(
            SwaggerVisibility.IsEnabled("Development") &&
            !SwaggerVisibility.IsEnabled("Production")),
            "Проверено условие включения Swagger по среде (не запуск второго сервера)");

        await Check("13. CSRF-токен требует авторизации", async () =>
        {
            using var result = await browser.GetAsync("/admin/csrf");
            return result.StatusCode == HttpStatusCode.Unauthorized;
        }, "Без cookie-сессии получить административный CSRF-токен нельзя");

        await Check("14. Неверный вход администратора", async () =>
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "/demo/admin-login");
            req.Headers.Add("X-Demo-Login", "incorrect-demo-token");
            using var result = await browser.SendAsync(req);
            return result.StatusCode == HttpStatusCode.Unauthorized;
        }, "Случайный секрет входа защищает локальную учебную cookie-сессию");

        await Check("15. Вход администратора", async () =>
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "/demo/admin-login");
            req.Headers.Add("X-Demo-Login", secrets.LoginToken);
            using var result = await browser.SendAsync(req);
            return result.StatusCode == HttpStatusCode.OK;
        }, "Создана защищённая от доступа извне учебная cookie-сессия");

        await Check("16. Выдача CSRF-токена", async () =>
        {
            using var result = await browser.GetAsync("/admin/csrf");
            if (result.StatusCode != HttpStatusCode.OK) return false;
            using var doc = JsonDocument.Parse(await result.Content.ReadAsStringAsync());
            csrfToken = doc.RootElement.GetProperty("csrfToken").GetString();
            return !string.IsNullOrWhiteSpace(csrfToken);
        }, "CSRF-токен выдан только после авторизации в cookie-сессии");

        await Check("17. POST без CSRF-токена отклонён", async () =>
        {
            using var result = await browser.PostAsync("/admin/maintenance",
                new StringContent("", Encoding.UTF8, "text/plain"));
            return result.StatusCode == HttpStatusCode.BadRequest;
        }, "Запрос с действующей cookie, но без CSRF-токена отклонён статусом 400");

        await Check("18. POST с CSRF-токеном принят", async () =>
        {
            if (string.IsNullOrWhiteSpace(csrfToken)) return false;
            using var req = new HttpRequestMessage(HttpMethod.Post, "/admin/maintenance")
            {
                Content = new StringContent("", Encoding.UTF8, "text/plain")
            };
            req.Headers.Add("X-CSRF-TOKEN", csrfToken);
            using var result = await browser.SendAsync(req);
            return result.StatusCode == HttpStatusCode.OK;
        }, "Действительная cookie и корректный CSRF-токен разрешают операцию");

        // Сохраняем независимый текстовый отчёт, пригодный для преподавателя.
        var reportPath = Path.Combine(folder, "demo-report.txt");
        var report = new StringBuilder();
        report.AppendLine("EnergyMeter System — API + Swagger + CSRF / вариант 15");
        report.AppendLine("Дата запуска: " + DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss"));
        report.AppendLine("Среда: ASP.NET Core 8, локальный адрес " + rootUrl);
        report.AppendLine("Демонстрация: API v1/v2, роли, идемпотентность, Swagger, CSRF");
        report.AppendLine();

        var checks = state.Checks();
        foreach (var item in checks)
        {
            report.AppendLine($"{item.Name}: {item.Status}");
            report.AppendLine("  " + item.Details);
        }

        bool passed = checks.Length == 18 && checks.All(item => item.Status == "PASS");
        report.AppendLine();
        report.AppendLine("ИТОГ: " + (passed ? "PASS" : "FAIL"));
        report.AppendLine("Production: Swagger запрещён по условию среды. Проверка условия выполнена без второго хоста.");
        report.AppendLine("Локальные учебные тесты не заменяют внешний аудит безопасности.");
        await File.WriteAllTextAsync(reportPath, report.ToString(), new UTF8Encoding(false));
        state.Complete(reportPath);

        Console.WriteLine(new string('=', 60));
        Console.WriteLine(passed ? "ИТОГ: ВСЕ 18 ДЕМО-ТЕСТОВ ПРОЙДЕНЫ" : "ИТОГ: ЕСТЬ ОШИБКИ");
        Console.WriteLine("Отчёт: " + reportPath);
        Console.WriteLine("Откройте страницу: " + rootUrl + "/demo");
    }
}

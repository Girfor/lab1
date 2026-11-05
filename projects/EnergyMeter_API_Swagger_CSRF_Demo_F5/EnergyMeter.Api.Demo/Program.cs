using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// Только локальный интерфейс: внешний сервер и настройки сети не требуются.
builder.WebHost.UseUrls("http://127.0.0.1:5162");
builder.Services.AddSingleton<DemoSecrets>();
builder.Services.AddSingleton<DemoStatus>();
builder.Services.AddSingleton<ConcurrentDictionary<Guid, MeterReading>>();
builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = "HeaderKey";
        options.DefaultChallengeScheme = "HeaderKey";
    })
    .AddScheme<AuthenticationSchemeOptions, HeaderKeyHandler>("HeaderKey", _ => { })
    .AddCookie("DemoCookie", options =>
    {
        options.Cookie.Name = "EnergyMeterDemoAuth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    });
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("Collector", policy =>
        policy.AddAuthenticationSchemes("HeaderKey")
            .RequireAuthenticatedUser().RequireRole("Collector"));
    options.AddPolicy("AdminCookie", new AuthorizationPolicyBuilder("DemoCookie")
        .RequireAuthenticatedUser().RequireRole("Admin").Build());
});
builder.Services.AddAntiforgery(options => options.HeaderName = "X-CSRF-TOKEN");
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// В Production Swagger не публикуется.
if (SwaggerVisibility.IsEnabled(app.Environment.EnvironmentName))
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthentication();
app.UseAuthorization();

var readings = app.Services.GetRequiredService<ConcurrentDictionary<Guid, MeterReading>>();

app.MapGet("/", () => Results.Redirect("/demo"))
    .ExcludeFromDescription();
app.MapGet("/demo", () => Results.Content(DemoPage.Html, "text/html; charset=utf-8"))
    .ExcludeFromDescription();
app.MapGet("/demo/status", (DemoStatus status) => Results.Ok(status.Snapshot()))
    .ExcludeFromDescription();

app.MapGet("/api/v1/readings", () => Results.Ok(readings.Values
        .OrderBy(x => x.TimestampUtc)
        .Select(x => new ReadingV1(x.MeterId, x.Kwh, x.TimestampUtc))))
    .WithName("GetEnergyReadingsV1")
    .WithTags("Energy readings v1");

app.MapGet("/api/v2/readings", () => Results.Ok(readings.Values
        .OrderBy(x => x.TimestampUtc)
        .Select(x => new ReadingV2(x.MeterId, x.Kwh, x.TimestampUtc, x.Quality))))
    .WithName("GetEnergyReadingsV2")
    .WithTags("Energy readings v2");

app.MapPost("/api/v1/readings", (ReadingV1 request, HttpContext context) =>
    {
        if (request.MeterId == Guid.Empty || request.Kwh < 0)
            return Results.BadRequest(new { error = "Invalid meterId or kwh" });

        string suppliedEvent = context.Request.Headers["X-Event-Id"].ToString();
        if (suppliedEvent.Length > 0 && !Guid.TryParse(suppliedEvent, out _))
            return Results.BadRequest(new { error = "Invalid X-Event-Id" });

        Guid eventId = Guid.TryParse(suppliedEvent, out var parsed) ? parsed : Guid.NewGuid();
        readings.TryAdd(eventId, new MeterReading(request.MeterId, request.Kwh,
            request.TimestampUtc, "measured")); // Повторный EventId не создаёт дубль.
        return Results.Created("/api/v1/readings", request);
    })
    .RequireAuthorization("Collector")
    .WithName("PostEnergyReadingV1")
    .WithTags("Energy readings v1");

// Учебная cookie-сессия существует только в Development и доступна лишь по
// случайному секрету, который хранится исключительно внутри текущего процесса.
if (app.Environment.IsDevelopment())
{
    app.MapPost("/demo/admin-login", async (HttpContext context, DemoSecrets secrets) =>
    {
        string supplied = context.Request.Headers["X-Demo-Login"].ToString();
        if (!SecureCompare.Matches(supplied, secrets.LoginToken))
            return Results.Unauthorized();

        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "local-demo-admin"),
            new Claim(ClaimTypes.Role, "Admin")
        }, "DemoCookie");

        await context.SignInAsync("DemoCookie", new ClaimsPrincipal(identity));
        return Results.Ok(new { status = "demo session created" });
    }).ExcludeFromDescription();

    app.MapGet("/admin/csrf", (HttpContext context, IAntiforgery antiforgery) =>
    {
        var tokenSet = antiforgery.GetAndStoreTokens(context);
        return Results.Ok(new { csrfToken = tokenSet.RequestToken });
    }).RequireAuthorization("AdminCookie");

    app.MapPost("/admin/maintenance", async (HttpContext context, IAntiforgery antiforgery) =>
    {
        try
        {
            await antiforgery.ValidateRequestAsync(context);
        }
        catch (AntiforgeryValidationException)
        {
            return Results.BadRequest(new { error = "Invalid CSRF token" });
        }

        return Results.Ok(new { status = "accepted" });
    }).RequireAuthorization("AdminCookie");
}

Console.OutputEncoding = Encoding.UTF8;
Console.WriteLine("ENERGYMETER API — ДЕМОНСТРАЦИЯ (ВАРИАНТ 15)");
Console.WriteLine("Локальный адрес: http://127.0.0.1:5162/demo");

await app.StartAsync();

if (app.Environment.IsDevelopment())
{
    await DemoRunner.RunAsync(app.Services, app.Environment.ContentRootPath,
        "http://127.0.0.1:5162");
}
else
{
    Console.WriteLine("Production: автоматический учебный тест и Swagger отключены.");
}

Console.WriteLine("Приложение продолжает работать. Для остановки нажмите Shift+F5 в Visual Studio.");
await app.WaitForShutdownAsync();

public record ReadingV1(Guid MeterId, decimal Kwh, DateTimeOffset TimestampUtc);
public record ReadingV2(Guid MeterId, decimal Kwh, DateTimeOffset TimestampUtc, string Quality);
public record MeterReading(Guid MeterId, decimal Kwh, DateTimeOffset TimestampUtc, string Quality);

internal static class SwaggerVisibility
{
    public static bool IsEnabled(string environmentName) =>
        string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase);
}

internal sealed class DemoSecrets
{
    public DemoSecrets() { }

    public string CollectorKey { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
    public string LoginToken { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
}

internal static class SecureCompare
{
    public static bool Matches(string? supplied, string? expected)
    {
        if (string.IsNullOrEmpty(supplied) || string.IsNullOrEmpty(expected))
            return false;

        byte[] a = Encoding.UTF8.GetBytes(supplied);
        byte[] b = Encoding.UTF8.GetBytes(expected);
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }
}

internal sealed class HeaderKeyHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly DemoSecrets _secrets;
    private readonly IWebHostEnvironment _environment;

    public HeaderKeyHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger, UrlEncoder encoder, DemoSecrets secrets, IWebHostEnvironment environment)
        : base(options, logger, encoder)
    {
        _secrets = secrets;
        _environment = environment;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string? supplied = Request.Headers["X-API-Key"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(supplied))
            return Task.FromResult(AuthenticateResult.NoResult());

        string? configured = Environment.GetEnvironmentVariable("ENERGYMETER_API_KEY");
        bool valid = SecureCompare.Matches(supplied, configured) ||
            (_environment.IsDevelopment() && SecureCompare.Matches(supplied, _secrets.CollectorKey));

        if (!valid)
            return Task.FromResult(AuthenticateResult.Fail("Invalid API key"));

        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "local-meter-collector"),
            new Claim(ClaimTypes.Role, "Collector")
        }, Scheme.Name);

        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(
            new ClaimsPrincipal(identity), Scheme.Name)));
    }
}

public partial class Program { }

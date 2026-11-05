using System.Collections.Concurrent;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddAuthentication("HeaderKey")
    .AddScheme<AuthenticationSchemeOptions, HeaderKeyHandler>("HeaderKey", _ => { });
builder.Services.AddAuthorization(options => {
    options.AddPolicy("Collector", p => p.RequireRole("Collector", "Admin"));
    options.AddPolicy("Admin", p => p.RequireRole("Admin"));
});
builder.Services.AddAntiforgery(options => options.HeaderName = "X-CSRF-TOKEN");
builder.Services.AddSingleton<ConcurrentDictionary<Guid, MeterReading>>();
builder.Services.AddEndpointsApiExplorer();

var app = builder.Build();
// Debug/Swagger endpoint deliberately absent in Production.
// Swagger can be added only in Development after installing Swashbuckle.
app.UseAuthentication();
app.UseAuthorization();

var readings = app.Services.GetRequiredService<ConcurrentDictionary<Guid, MeterReading>>();
app.MapGet("/api/v1/readings", () => Results.Ok(readings.Values
    .OrderBy(x => x.TimestampUtc)
    .Select(x => new ReadingV1(x.MeterId, x.Kwh, x.TimestampUtc))));
app.MapGet("/api/v2/readings", () => Results.Ok(readings.Values
    .OrderBy(x => x.TimestampUtc)
    .Select(x => new ReadingV2(x.MeterId, x.Kwh, x.TimestampUtc, x.Quality))));

app.MapPost("/api/v1/readings", (ReadingV1 request, HttpContext context) => {
    if (request.Kwh < 0 || request.MeterId == Guid.Empty) return Results.BadRequest();
    var reading = new MeterReading(request.MeterId, request.Kwh, request.TimestampUtc, "measured");
    var eventHeader = context.Request.Headers["X-Event-Id"].ToString();
    if (!string.IsNullOrEmpty(eventHeader) && !Guid.TryParse(eventHeader, out _))
        return Results.BadRequest("Invalid X-Event-Id");
    Guid eventId = Guid.TryParse(eventHeader, out var parsed) ? parsed : Guid.NewGuid();
    readings.TryAdd(eventId, reading); // repeated event IDs are idempotent
    return Results.Created("/api/v1/readings", request);
}).RequireAuthorization("Collector");

// For browser sessions authenticated by cookies, add antiforgery validation
// to all unsafe actions and issue the token from an authenticated same-origin page.
app.MapGet("/admin/csrf", (HttpContext context,
    Microsoft.AspNetCore.Antiforgery.IAntiforgery antiforgery) => {
    var token = antiforgery.GetAndStoreTokens(context);
    return Results.Ok(new { csrfToken = token.RequestToken });
}).RequireAuthorization("Admin");
app.MapPost("/admin/maintenance", async (HttpContext context,
    Microsoft.AspNetCore.Antiforgery.IAntiforgery antiforgery) => {
    try { await antiforgery.ValidateRequestAsync(context); }
    catch (Microsoft.AspNetCore.Antiforgery.AntiforgeryValidationException) {
        return Results.BadRequest(new { error = "Invalid CSRF token" });
    }
    return Results.Ok(new { status = "accepted" });
}).RequireAuthorization("Admin");

app.Run();

public record ReadingV1(Guid MeterId, decimal Kwh, DateTimeOffset TimestampUtc);
public record ReadingV2(Guid MeterId, decimal Kwh, DateTimeOffset TimestampUtc, string Quality);
public record MeterReading(Guid MeterId, decimal Kwh, DateTimeOffset TimestampUtc, string Quality);

internal sealed class HeaderKeyHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public HeaderKeyHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger, UrlEncoder encoder) : base(options, logger, encoder) { }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string? supplied = Request.Headers["X-API-Key"].FirstOrDefault();
        string? configured = Environment.GetEnvironmentVariable("ENERGYMETER_API_KEY");
        string? admin = Environment.GetEnvironmentVariable("ENERGYMETER_ADMIN_API_KEY");
        if (string.IsNullOrWhiteSpace(supplied))
            return Task.FromResult(AuthenticateResult.NoResult());
        byte[] a = System.Text.Encoding.UTF8.GetBytes(supplied);
        bool Matches(string? secret) {
            if (string.IsNullOrWhiteSpace(secret)) return false;
            byte[] b = System.Text.Encoding.UTF8.GetBytes(secret);
            return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
        }
        bool isAdmin = Matches(admin);
        if (!isAdmin && !Matches(configured))
            return Task.FromResult(AuthenticateResult.Fail("Invalid API key"));
        // Production: resolve role from an IAM, not from a user-provided header.
        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, "service-collector"),
            new Claim(ClaimTypes.Role, isAdmin ? "Admin" : "Collector") };
        var identity = new ClaimsIdentity(claims, Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
    }
}

// Entry point marker for WebApplicationFactory integration tests.
public partial class Program { }

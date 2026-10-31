using System.Security.Cryptography;
using System.Text;
using Campus.AccessControl.Api.Data;
using Npgsql;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration["CAMPUS_DB_CONNECTION"];
var apiKey = builder.Configuration["CAMPUS_API_KEY"];
if (string.IsNullOrWhiteSpace(connectionString) || string.IsNullOrWhiteSpace(apiKey))
{
    throw new InvalidOperationException(
        "Set CAMPUS_DB_CONNECTION and CAMPUS_API_KEY environment variables.");
}

builder.Services.AddSingleton(NpgsqlDataSource.Create(connectionString));
builder.Services.AddScoped<AccessPassRepository>();
builder.Services.AddControllers();
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("campus-access-control"))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddOtlpExporter());

var app = builder.Build();

app.MapGet("/health/live", () => Results.Ok(new { status = "ok" }));

// Demonstration auth; a deployed campus system should use OIDC and RBAC.
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/health/live"))
    {
        await next();
        return;
    }

    var submitted = context.Request.Headers["X-Api-Key"].ToString();
    if (!CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(submitted), Encoding.UTF8.GetBytes(apiKey)))
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new { error = "unauthorized" });
        return;
    }

    await next();
});

app.MapControllers();
app.Run();

public partial class Program { }

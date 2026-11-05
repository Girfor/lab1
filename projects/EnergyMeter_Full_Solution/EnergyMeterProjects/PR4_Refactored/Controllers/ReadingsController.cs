using EnergyMeter.Refactored.Models;
using EnergyMeter.Refactored.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EnergyMeter.Refactored.Controllers;

[ApiController]
[Route("api/meters/{meterId:guid}/readings")]
[Authorize]
public sealed class ReadingsController : ControllerBase
{
    private readonly IReadingService _readings;

    public ReadingsController(IReadingService readings)
    {
        _readings = readings;
    }

    /// <summary>Получить список показаний счетчика.</summary>
    [HttpGet]
    public async Task<IActionResult> GetReadings(
        Guid meterId, CancellationToken cancellationToken)
    {
        var readings = await _readings.ListAsync(User, meterId, cancellationToken);
        return readings is null ? NotFound() : Ok(readings);
    }

    /// <summary>Получить конкретное показание.</summary>
    [HttpGet("{readingId:guid}")]
    public async Task<IActionResult> GetReading(
        Guid meterId, Guid readingId, CancellationToken cancellationToken)
    {
        var reading = await _readings.GetAsync(
            User, meterId, readingId, cancellationToken);

        return reading is null ? NotFound() : Ok(reading);
    }

    /// <summary>Сохранить новое показание с анти-CSRF проверкой.</summary>
    [HttpPost]
    [Authorize(Policy = "MeterWriter")]
    public async Task<IActionResult> AddReading(
        Guid meterId, [FromBody] AddReadingRequest request,
        [FromHeader(Name = "X-CSRF-TOKEN")] string? csrfToken,
        CancellationToken cancellationToken)
    {
        var key = Request.Headers["Idempotency-Key"].ToString();
        var result = await _readings.AddAsync(
            User, meterId, request, key, cancellationToken);

        return result.Status switch
        {
            AddStatus.NotFound => NotFound(),
            AddStatus.Forbidden => Forbid(),
            AddStatus.Invalid => BadRequest(new { message = result.Error }),
            AddStatus.Duplicate => Ok(result.Reading),
            _ => CreatedAtAction(
                nameof(GetReading),
                new { meterId, readingId = result.Reading!.Id },
                result.Reading)
        };
    }
}

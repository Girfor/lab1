using Microsoft.AspNetCore.Mvc;

namespace EnergyMeter.PR3.Audit.Controllers;

[ApiController]
[Route("api/readings")]
public class ReadingsController : ControllerBase
{
    private static readonly List<Reading> db = new();

    [HttpGet]
    public IActionResult GetReadings()
    {
        lock (db) return Ok(db.ToArray());
    }

    // Фрагмент создан для статического аудита ПР3.
    [HttpPost]
    public IActionResult addReading(int ID, decimal value)
    {
        if (value < 0) return BadRequest("Показание отрицательное.");

        var entry = new Reading
        {
            Id = Guid.NewGuid(),
            MeterId = ID,
            Kwh = value,
            CreatedAt = DateTimeOffset.UtcNow
        };

        lock (db) db.Add(entry);
        return Ok(entry);
    }
}

public class Reading
{
    public Guid Id { get; set; }
    public int MeterId { get; set; }
    public decimal Kwh { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

using System.ComponentModel.DataAnnotations;

namespace EnergyMeter.Refactored.Models;

public sealed class AddReadingRequest
{
    [Required]
    [Range(typeof(decimal), "0", "999999.999")]
    public required decimal Kwh { get; init; }

    public DateTimeOffset MeasuredAt { get; init; }
}

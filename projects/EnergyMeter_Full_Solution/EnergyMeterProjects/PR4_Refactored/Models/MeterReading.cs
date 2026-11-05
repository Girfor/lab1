namespace EnergyMeter.Refactored.Models;

public sealed record MeterReading(
    Guid Id,
    Guid MeterId,
    decimal Kwh,
    DateTimeOffset MeasuredAt,
    DateTimeOffset ReceivedAt,
    string Author);

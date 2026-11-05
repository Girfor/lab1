namespace EnergyMeter.Refactored.Models;

public enum AddStatus
{
    Created,
    Duplicate,
    NotFound,
    Forbidden,
    Invalid
}

public sealed record AddResult(
    AddStatus Status,
    MeterReading? Reading = null,
    string? Error = null);

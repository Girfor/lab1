using System.Security.Claims;
using EnergyMeter.Refactored.Models;

namespace EnergyMeter.Refactored.Services;

public interface IReadingService
{
    Task<AddResult> AddAsync(
        ClaimsPrincipal user,
        Guid meterId,
        AddReadingRequest request,
        string? idempotencyKey,
        CancellationToken cancellationToken);

    Task<MeterReading?> GetAsync(
        ClaimsPrincipal user,
        Guid meterId,
        Guid readingId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<MeterReading>?> ListAsync(
        ClaimsPrincipal user,
        Guid meterId,
        CancellationToken cancellationToken);
}

using System.Security.Claims;
using EnergyMeter.Refactored.Models;

namespace EnergyMeter.Refactored.Services;

public sealed class InMemoryReadingService : IReadingService
{
    public static readonly Guid DemoMeterId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");

    private readonly object _sync = new();
    private readonly Dictionary<Guid, MeterReading> _readings = new();
    private readonly Dictionary<string, Guid> _idempotency = new();

    private static bool CanAccess(ClaimsPrincipal user) =>
        user.Identity?.IsAuthenticated == true &&
        (string.Equals(user.Identity.Name, "operator", StringComparison.Ordinal) ||
         user.IsInRole("Admin"));

    public Task<AddResult> AddAsync(
        ClaimsPrincipal user,
        Guid meterId,
        AddReadingRequest request,
        string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (meterId != DemoMeterId)
            return Task.FromResult(new AddResult(AddStatus.NotFound));

        if (!CanAccess(user))
            return Task.FromResult(new AddResult(AddStatus.Forbidden));

        if (request.Kwh is < 0 or > 999999.999m ||
            decimal.Round(request.Kwh, 3) != request.Kwh)
            return Task.FromResult(new AddResult(
                AddStatus.Invalid, Error: "Показание должно быть от 0 до 999999.999 с точностью до 0.001."));

        var now = DateTimeOffset.UtcNow;
        if (request.MeasuredAt < new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero) ||
            request.MeasuredAt > now.AddMinutes(5))
            return Task.FromResult(new AddResult(
                AddStatus.Invalid, Error: "Некорректное время измерения."));

        if (idempotencyKey is { Length: > 128 })
            return Task.FromResult(new AddResult(
                AddStatus.Invalid, Error: "Слишком длинный Idempotency-Key."));

        var reading = new MeterReading(
            Guid.NewGuid(), meterId, request.Kwh,
            request.MeasuredAt, now, user.Identity!.Name!);

        lock (_sync)
        {
            if (!string.IsNullOrWhiteSpace(idempotencyKey))
            {
                var key = meterId + ":" + idempotencyKey;
                if (_idempotency.TryGetValue(key, out var existingId))
                    return Task.FromResult(new AddResult(
                        AddStatus.Duplicate, _readings[existingId]));

                _idempotency[key] = reading.Id;
            }

            _readings[reading.Id] = reading;
        }

        return Task.FromResult(new AddResult(AddStatus.Created, reading));
    }

    public Task<MeterReading?> GetAsync(
        ClaimsPrincipal user, Guid meterId, Guid readingId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (meterId != DemoMeterId || !CanAccess(user))
            return Task.FromResult<MeterReading?>(null);

        lock (_sync)
            return Task.FromResult(
                _readings.GetValueOrDefault(readingId));
    }

    public Task<IReadOnlyList<MeterReading>?> ListAsync(
        ClaimsPrincipal user, Guid meterId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (meterId != DemoMeterId || !CanAccess(user))
            return Task.FromResult<IReadOnlyList<MeterReading>?>(null);

        lock (_sync)
        {
            IReadOnlyList<MeterReading> list = _readings.Values
                .OrderByDescending(x => x.MeasuredAt)
                .ToArray();
            return Task.FromResult<IReadOnlyList<MeterReading>?>(list);
        }
    }
}

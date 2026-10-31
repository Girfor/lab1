using Campus.AccessControl.Api.Models;
using Npgsql;

namespace Campus.AccessControl.Api.Data;

public sealed class AccessPassRepository
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly bool _dualMode;
    private readonly string _readTable;

    public AccessPassRepository(NpgsqlDataSource dataSource, IConfiguration configuration)
    {
        _dataSource = dataSource;
        var mode = (configuration["CAMPUS_SCHEMA_MODE"] ?? "dual").ToLowerInvariant();
        if (mode is not ("dual" or "new"))
            throw new InvalidOperationException("CAMPUS_SCHEMA_MODE must be dual or new.");

        _dualMode = mode == "dual";
        _readTable = _dualMode ? "\"Passes\"" : "\"AccessPasses\"";
    }

    public async Task CreateAsync(AccessPass pass, CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        if (_dualMode)
            await WriteAsync(connection, transaction, "\"Passes\"", pass, cancellationToken);

        // Transactional dual-write; trigger in 01_expand also covers old clients.
        await WriteAsync(connection, transaction, "\"AccessPasses\"", pass, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task WriteAsync(NpgsqlConnection connection,
        NpgsqlTransaction transaction, string table, AccessPass pass,
        CancellationToken cancellationToken)
    {
        var sql = $"""
            INSERT INTO public.{table}
                (id, person_id, badge_code, valid_until, is_active, is_test, updated_at)
            VALUES (@id, @person_id, @badge_code, @valid_until,
                    @is_active, @is_test, NOW())
            ON CONFLICT (id) DO UPDATE SET
                person_id = EXCLUDED.person_id,
                badge_code = EXCLUDED.badge_code,
                valid_until = EXCLUDED.valid_until,
                is_active = EXCLUDED.is_active,
                is_test = EXCLUDED.is_test,
                updated_at = EXCLUDED.updated_at;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("id", pass.Id);
        command.Parameters.AddWithValue("person_id", pass.PersonId);
        command.Parameters.AddWithValue("badge_code", pass.BadgeCode);
        command.Parameters.AddWithValue("valid_until", pass.ValidUntil);
        command.Parameters.AddWithValue("is_active", pass.IsActive);
        command.Parameters.AddWithValue("is_test", pass.IsTest);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<AccessPass?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        // _readTable is from a fixed allowlist; it cannot contain user input.
        var sql = $"""
            SELECT id, person_id, badge_code, valid_until, is_active, is_test
            FROM public.{_readTable} WHERE id = @id;
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;

        return new AccessPass(
            reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2),
            reader.GetFieldValue<DateTimeOffset>(3), reader.GetBoolean(4),
            reader.GetBoolean(5));
    }

    public async Task<bool> DeleteTestAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var deleted = false;

        if (_dualMode)
            deleted = await DeleteFromAsync(connection, transaction, "\"Passes\"",
                id, cancellationToken);

        deleted = await DeleteFromAsync(connection, transaction, "\"AccessPasses\"",
            id, cancellationToken) || deleted;
        await transaction.CommitAsync(cancellationToken);
        return deleted;
    }

    private static async Task<bool> DeleteFromAsync(NpgsqlConnection connection,
        NpgsqlTransaction transaction, string table, Guid id,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            $"DELETE FROM public.{table} WHERE id = @id AND is_test = TRUE;",
            connection, transaction);
        command.Parameters.AddWithValue("id", id);
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }
}

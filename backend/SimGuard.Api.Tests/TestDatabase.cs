using Microsoft.EntityFrameworkCore;
using Npgsql;
using SimGuard.Api.Data;

namespace SimGuard.Api.Tests;

// A throwaway PostgreSQL database with every migration applied. Tests that
// need it are skipped unless SIMGUARD_TEST_CONNECTION is set to a
// connection string for a user that may create databases; its Database
// value is replaced by a random name, and the database is dropped after.
public sealed class TestDatabase : IAsyncLifetime
{
    public const string Variable = "SIMGUARD_TEST_CONNECTION";

    public static bool Available => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(Variable));

    public string ConnectionString { get; private set; } = string.Empty;

    private string _adminConnection = string.Empty;
    private string _name = string.Empty;

    public async Task InitializeAsync()
    {
        if (!Available)
        {
            return;
        }

        var builder = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable(Variable));
        _name = $"simguard_test_{Guid.NewGuid():N}"[..30];
        builder.Database = "postgres";
        _adminConnection = builder.ConnectionString;
        builder.Database = _name;
        builder.Pooling = false;
        ConnectionString = builder.ConnectionString;

        await using (var admin = new NpgsqlConnection(_adminConnection))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{_name}\"", admin);
            await create.ExecuteNonQueryAsync();
        }

        await using var db = NewContext();
        await db.Database.MigrateAsync();
    }

    public SimGuardDbContext NewContext() =>
        new(new DbContextOptionsBuilder<SimGuardDbContext>().UseNpgsql(ConnectionString).Options);

    public async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<long> ScalarAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    public async Task DisposeAsync()
    {
        if (!Available || _name.Length == 0)
        {
            return;
        }

        NpgsqlConnection.ClearAllPools();
        await using var admin = new NpgsqlConnection(_adminConnection);
        await admin.OpenAsync();
        await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_name}\" WITH (FORCE)", admin);
        await drop.ExecuteNonQueryAsync();
    }
}

// [DbFact] runs only when SIMGUARD_TEST_CONNECTION is set.
public sealed class DbFactAttribute : FactAttribute
{
    public DbFactAttribute()
    {
        if (!TestDatabase.Available)
        {
            Skip = $"Set {TestDatabase.Variable} to run database tests.";
        }
    }
}

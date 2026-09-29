using Microsoft.Data.SqlClient;
using U1.Business.Testing;

namespace U1.Business.Tests;

[Collection(ApiTestCollection.Name)]
public sealed class TestDatabaseLifecycleTests
{
    [Fact]
    public async Task Cleanup_drops_only_the_exact_owned_database()
    {
        if (TestDatabaseLifecycle.PreserveRequested)
            return;

        var cancellationToken = TestContext.Current.CancellationToken;
        var first = $"U1Business_CI_cleanup_{Guid.NewGuid():N}";
        var second = $"U1Business_CI_cleanup_{Guid.NewGuid():N}";

        await CreateDatabase(first, cancellationToken);
        await CreateDatabase(second, cancellationToken);
        try
        {
            await TestDatabaseLifecycle.DropAsync(first, "U1Business_CI_", cancellationToken);

            Assert.False(await DatabaseExists(first, cancellationToken));
            Assert.True(await DatabaseExists(second, cancellationToken));
        }
        finally
        {
            await TestDatabaseLifecycle.DropUncancellableAsync(first, "U1Business_CI_");
            await TestDatabaseLifecycle.DropUncancellableAsync(second, "U1Business_CI_");
        }
    }

    [Fact]
    public async Task Cleanup_rejects_database_names_outside_the_owned_prefix()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            TestDatabaseLifecycle.DropAsync("U1Business", "U1Business_CI_", TestContext.Current.CancellationToken));
    }

    private static async Task CreateDatabase(string databaseName, CancellationToken cancellationToken)
    {
        await using var connection = await OpenMaster(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = """
            DECLARE @sql nvarchar(max) = N'CREATE DATABASE ' + QUOTENAME(@databaseName) + N';';
            EXEC sys.sp_executesql @sql;
            """;
        command.Parameters.AddWithValue("@databaseName", databaseName);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<bool> DatabaseExists(string databaseName, CancellationToken cancellationToken)
    {
        await using var connection = await OpenMaster(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT CASE WHEN DB_ID(@databaseName) IS NULL THEN 0 ELSE 1 END;";
        command.Parameters.AddWithValue("@databaseName", databaseName);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) == 1;
    }

    private static async Task<SqlConnection> OpenMaster(CancellationToken cancellationToken)
    {
        var connection = new SqlConnection(
            @"Server=(localdb)\MSSQLLocalDB;Database=master;Integrated Security=true;TrustServerCertificate=true;Connect Timeout=30");
        await connection.OpenAsync(cancellationToken);
        return connection;
    }
}

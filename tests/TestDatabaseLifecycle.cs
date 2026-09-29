using Microsoft.Data.SqlClient;

namespace U1.Business.Testing;

internal static class TestDatabaseLifecycle
{
    public const string PreserveEnvironmentVariable = "U1_KEEP_TEST_DATABASES";

    public static bool PreserveRequested => IsTruthy(
        Environment.GetEnvironmentVariable(PreserveEnvironmentVariable));

    public static Task DropAsync(string databaseName, string expectedPrefix) =>
        DropAsync(databaseName, expectedPrefix, CancellationToken.None);

    public static async Task DropAsync(
        string databaseName,
        string expectedPrefix,
        CancellationToken cancellationToken)
    {
        ValidateOwnedDatabaseName(databaseName, expectedPrefix);
        if (PreserveRequested)
            return;

        SqlConnection.ClearAllPools();
        try
        {
            var builder = new SqlConnectionStringBuilder
            {
                DataSource = @"(localdb)\MSSQLLocalDB",
                InitialCatalog = "master",
                IntegratedSecurity = true,
                TrustServerCertificate = true,
                ConnectTimeout = 30
            };

            await using var connection = new SqlConnection(builder.ConnectionString);
            await connection.OpenAsync(cancellationToken);
            using var command = connection.CreateCommand();
            command.CommandText = """
                IF DB_ID(@databaseName) IS NOT NULL
                BEGIN
                    DECLARE @sql nvarchar(max) =
                        N'ALTER DATABASE ' + QUOTENAME(@databaseName)
                        + N' SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE '
                        + QUOTENAME(@databaseName) + N';';
                    EXEC sys.sp_executesql @sql;
                END
                """;
            command.Parameters.AddWithValue("@databaseName", databaseName);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            SqlConnection.ClearAllPools();
        }
    }

    private static void ValidateOwnedDatabaseName(string databaseName, string expectedPrefix)
    {
        if (string.IsNullOrWhiteSpace(expectedPrefix)
            || !databaseName.StartsWith(expectedPrefix, StringComparison.Ordinal)
            || databaseName.Length <= expectedPrefix.Length
            || databaseName.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_'))
        {
            throw new InvalidOperationException(
                $"Test veritabanı adı beklenen sahiplik sınırının dışında: {databaseName}");
        }
    }

    private static bool IsTruthy(string? value) =>
        value is not null
        && (value.Equals("1", StringComparison.OrdinalIgnoreCase)
            || value.Equals("true", StringComparison.OrdinalIgnoreCase)
            || value.Equals("yes", StringComparison.OrdinalIgnoreCase));
}

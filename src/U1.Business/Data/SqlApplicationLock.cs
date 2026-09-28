using System.Data;
using Microsoft.Data.SqlClient;

namespace U1.Business.Data;

internal static class SqlApplicationLock
{
    public static async Task<int> AcquireAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        string resource,
        string owner,
        int lockTimeoutMilliseconds)
    {
        if (lockTimeoutMilliseconds < 0)
            throw new ArgumentOutOfRangeException(nameof(lockTimeoutMilliseconds));

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = Math.Max(1, (int)Math.Ceiling(lockTimeoutMilliseconds / 1000d) + 5);
        command.CommandText = """
            SET NOCOUNT ON;
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock
                @Resource = @resource,
                @LockMode = 'Exclusive',
                @LockOwner = @owner,
                @LockTimeout = @lockTimeout;
            SELECT @result;
            """;
        command.Parameters.Add(new SqlParameter("@resource", SqlDbType.NVarChar, 255) { Value = resource });
        command.Parameters.Add(new SqlParameter("@owner", SqlDbType.VarChar, 32) { Value = owner });
        command.Parameters.Add(new SqlParameter("@lockTimeout", SqlDbType.Int) { Value = lockTimeoutMilliseconds });

        var result = await command.ExecuteScalarAsync();
        if (result is null or DBNull)
            throw new InvalidOperationException("SQL uygulama kilidi dönüş kodu alınamadı.");

        return Convert.ToInt32(result);
    }

    public static async Task ReleaseAsync(
        SqlConnection connection,
        string resource,
        string owner)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            EXEC sys.sp_releaseapplock
                @Resource = @resource,
                @LockOwner = @owner;
            """;
        command.Parameters.Add(new SqlParameter("@resource", SqlDbType.NVarChar, 255) { Value = resource });
        command.Parameters.Add(new SqlParameter("@owner", SqlDbType.VarChar, 32) { Value = owner });
        await command.ExecuteNonQueryAsync();
    }
}

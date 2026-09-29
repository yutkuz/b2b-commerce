using System.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Configuration;
using U1.Business.Data;
using U1.Business.Domain;
using Xunit;

namespace U1.Business.Tests;

[Collection(ApiTestCollection.Name)]
public sealed class SchemaUpgradeTests
{
    [Fact]
    public async Task Legacy_database_upgrade_preserves_records_and_matches_the_runtime_model()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var databaseName = "U1Business_Upgrade_" + Guid.NewGuid().ToString("N");
        var builder = new SqlConnectionStringBuilder(
            @"Server=(localdb)\MSSQLLocalDB;Integrated Security=true;TrustServerCertificate=true;Connect Timeout=30")
        {
            InitialCatalog = databaseName
        };
        var connectionString = builder.ConnectionString;
        var previousExpectedDatabase = Environment.GetEnvironmentVariable("U1_TEST_DATABASE");

        await CreateOwnedDatabase(databaseName, builder, cancellationToken);
        try
        {
            Environment.SetEnvironmentVariable("U1_TEST_DATABASE", databaseName);
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await ApplyVersionOneSchema(connection, cancellationToken);

            var user = new User { Email = "legacy@example.test" };
            var passwordHash = new PasswordHasher<User>().HashPassword(user, "LegacyPassword!2026");
            await SeedLegacyRecords(connection, passwordHash, cancellationToken);

            var factory = new UpgradeDbFactory(connectionString);
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:SqlServer"] = connectionString
                })
                .Build();
            var database = new Database(configuration, factory);

            await database.Initialize(development: false);
            await AssertPreservedState(factory, passwordHash, cancellationToken);
            await AssertSqlAndEfMappings(connection, factory, cancellationToken);

            await database.Initialize(development: false);
            await AssertPreservedState(factory, passwordHash, cancellationToken);
            Assert.Equal(3, await Scalar<int>(connection,
                "SELECT COUNT(*) FROM dbo.SchemaVersions", cancellationToken));
            Assert.Equal(1, await Scalar<int>(connection,
                "SELECT COUNT(*) FROM dbo.DemoSetup WHERE Component = 'catalog'", cancellationToken));

            await Execute(connection,
                "INSERT INTO dbo.SchemaVersions(Version) VALUES(4)", cancellationToken);
            var error = await Assert.ThrowsAsync<InvalidOperationException>(
                () => database.Initialize(development: false));
            Assert.Equal("Veritabanı şeması bu uygulamadan daha yeni.", error.Message);
            Assert.Equal(4, await Scalar<int>(connection,
                "SELECT MAX(Version) FROM dbo.SchemaVersions", cancellationToken));
            await AssertPreservedState(factory, passwordHash, cancellationToken);
        }
        finally
        {
            Environment.SetEnvironmentVariable("U1_TEST_DATABASE", previousExpectedDatabase);
            using var poolConnection = new SqlConnection(connectionString);
            SqlConnection.ClearPool(poolConnection);
            await DropOwnedDatabase(databaseName, builder, CancellationToken.None);
        }
    }

    private static async Task AssertPreservedState(
        IDbContextFactory<BusinessDbContext> factory, string passwordHash, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var user = await db.Users.SingleAsync(x => x.Email == "legacy@example.test", cancellationToken);
        Assert.Equal(passwordHash, user.PasswordHash);
        Assert.Equal(PasswordVerificationResult.Success,
            new PasswordHasher<User>().VerifyHashedPassword(user, user.PasswordHash, "LegacyPassword!2026"));

        var product = await db.Products.SingleAsync(x => x.Code == "LEGACY-001", cancellationToken);
        Assert.Equal(7, product.Stock);
        Assert.Equal(12.50m, product.Price);
        Assert.Equal(8, product.RowVersion.Length);

        var order = await db.Orders.SingleAsync(x => x.Number == "LEGACY-ORDER", cancellationToken);
        Assert.Equal(user.Id, order.UserId);
        Assert.Equal(25m, order.Total);
        var line = await db.OrderItems.SingleAsync(x => x.OrderId == order.Id, cancellationToken);
        Assert.Equal(product.Id, line.ProductId);
        Assert.Equal("Legacy product snapshot", line.ProductName);
        Assert.Equal(2, line.Quantity);
        Assert.Equal(12.50m, line.UnitPrice);
        Assert.Equal(25m, line.Total);
        Assert.Equal(1, await db.Carts.CountAsync(x => x.UserId == user.Id, cancellationToken));
    }

    private static async Task AssertSqlAndEfMappings(
        SqlConnection connection, IDbContextFactory<BusinessDbContext> factory, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var user = db.Model.FindEntityType(typeof(User))!;
        var product = db.Model.FindEntityType(typeof(Product))!;
        var order = db.Model.FindEntityType(typeof(Order))!;
        var orderItem = db.Model.FindEntityType(typeof(OrderItem))!;

        await AssertColumn(connection, "Users", "Email", "nvarchar", 400, 0, 0,
            user.FindProperty(nameof(User.Email))!, 200, cancellationToken);
        await AssertColumn(connection, "Users", "Role", "varchar", 12, 0, 0,
            user.FindProperty(nameof(User.Role))!, 12, cancellationToken);
        await AssertColumn(connection, "Products", "Name", "nvarchar", 360, 0, 0,
            product.FindProperty(nameof(Product.Name))!, 180, cancellationToken);
        await AssertColumn(connection, "Products", "Price", "decimal", 9, 18, 2,
            product.FindProperty(nameof(Product.Price))!, null, cancellationToken);
        await AssertColumn(connection, "Orders", "Total", "decimal", 9, 18, 2,
            order.FindProperty(nameof(Order.Total))!, null, cancellationToken);
        await AssertColumn(connection, "OrderItems", "UnitPrice", "decimal", 9, 18, 2,
            orderItem.FindProperty(nameof(OrderItem.UnitPrice))!, null, cancellationToken);

        var rowVersion = product.FindProperty(nameof(Product.RowVersion))!;
        var rowVersionColumn = await ReadColumn(connection, "Products", "RowVersion", cancellationToken);
        Assert.Equal("timestamp", rowVersionColumn.Type);
        Assert.Equal(8, rowVersionColumn.Length);
        Assert.True(rowVersion.IsConcurrencyToken);
        Assert.Equal(ValueGenerated.OnAddOrUpdate, rowVersion.ValueGenerated);

        var createdAt = await ReadColumn(connection, "Orders", "CreatedAt", cancellationToken);
        Assert.Contains("sysutcdatetime", createdAt.Default!.ToLowerInvariant());
        Assert.Equal("SYSUTCDATETIME()",
            order.FindProperty(nameof(Order.CreatedAt))!.GetDefaultValueSql());
        var status = await ReadColumn(connection, "Orders", "Status", cancellationToken);
        Assert.Contains("Bekliyor", status.Default!);
        Assert.Equal("Bekliyor", order.FindProperty(nameof(Order.Status))!.GetDefaultValue());

        await AssertForeignKey(connection, db, "Products", nameof(Product.CategoryId), "Categories", cancellationToken);
        await AssertForeignKey(connection, db, "Carts", nameof(Cart.UserId), "Users", cancellationToken);
        await AssertForeignKey(connection, db, "CartItems", nameof(CartItem.CartId), "Carts", cancellationToken);
        await AssertForeignKey(connection, db, "CartItems", nameof(CartItem.ProductId), "Products", cancellationToken);
        await AssertForeignKey(connection, db, "Orders", nameof(Order.UserId), "Users", cancellationToken);
        await AssertForeignKey(connection, db, "OrderItems", nameof(OrderItem.OrderId), "Orders", cancellationToken);
        await AssertForeignKey(connection, db, "OrderItems", nameof(OrderItem.ProductId), "Products", cancellationToken);
    }

    private static async Task AssertForeignKey(
        SqlConnection connection, BusinessDbContext db, string childTable, string childColumn,
        string parentTable, CancellationToken cancellationToken)
    {
        var child = db.Model.GetEntityTypes().Single(x => x.GetTableName() == childTable);
        Assert.Contains(child.GetForeignKeys(), x =>
            x.Properties.Single().Name == childColumn
            && x.PrincipalEntityType.GetTableName() == parentTable);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*) FROM sys.foreign_key_columns fkc
            JOIN sys.tables child ON child.object_id = fkc.parent_object_id
            JOIN sys.columns childColumn ON childColumn.object_id = child.object_id
                AND childColumn.column_id = fkc.parent_column_id
            JOIN sys.tables parent ON parent.object_id = fkc.referenced_object_id
            WHERE child.name = @childTable AND childColumn.name = @childColumn
                AND parent.name = @parentTable
            """;
        command.Parameters.AddWithValue("@childTable", childTable);
        command.Parameters.AddWithValue("@childColumn", childColumn);
        command.Parameters.AddWithValue("@parentTable", parentTable);
        Assert.Equal(1, Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)));
    }

    private static async Task AssertColumn(
        SqlConnection connection, string table, string column, string type, int length,
        int precision, int scale, IProperty property, int? maxLength,
        CancellationToken cancellationToken)
    {
        var actual = await ReadColumn(connection, table, column, cancellationToken);
        Assert.Equal(type, actual.Type);
        Assert.Equal(length, actual.Length);
        if (precision != 0)
        {
            Assert.Equal(precision, actual.Precision);
            Assert.Equal(scale, actual.Scale);
            Assert.Equal(precision, property.GetPrecision());
            Assert.Equal(scale, property.GetScale());
        }
        if (maxLength is not null)
            Assert.Equal(maxLength, property.GetMaxLength());
        if (type is "nvarchar" or "varchar")
            Assert.Equal(type == "nvarchar", property.IsUnicode() ?? true);
    }

    private static async Task<(string Type, int Length, int Precision, int Scale, string? Default)> ReadColumn(
        SqlConnection connection, string table, string column, CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT TYPE_NAME(c.user_type_id), c.max_length, c.precision, c.scale, dc.definition
            FROM sys.columns c
            JOIN sys.tables t ON t.object_id = c.object_id
            LEFT JOIN sys.default_constraints dc ON dc.object_id = c.default_object_id
            WHERE t.name = @table AND c.name = @column
            """;
        command.Parameters.AddWithValue("@table", table);
        command.Parameters.AddWithValue("@column", column);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        Assert.True(await reader.ReadAsync(cancellationToken));
        return (reader.GetString(0), reader.GetInt16(1), reader.GetByte(2), reader.GetByte(3),
            reader.IsDBNull(4) ? null : reader.GetString(4));
    }

    private static async Task CreateOwnedDatabase(
        string name, SqlConnectionStringBuilder builder, CancellationToken cancellationToken)
    {
        Assert.Matches("^U1Business_Upgrade_[0-9a-f]{32}$", name);
        builder.InitialCatalog = "master";
        await using var master = new SqlConnection(builder.ConnectionString);
        await master.OpenAsync(cancellationToken);
        await Execute(master, $"CREATE DATABASE [{name}]", cancellationToken);
        builder.InitialCatalog = name;
    }

    private static async Task DropOwnedDatabase(
        string name, SqlConnectionStringBuilder builder, CancellationToken cancellationToken)
    {
        Assert.Matches("^U1Business_Upgrade_[0-9a-f]{32}$", name);
        builder.InitialCatalog = "master";
        await using var master = new SqlConnection(builder.ConnectionString);
        await master.OpenAsync(cancellationToken);
        await Execute(master,
            $"ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}]",
            cancellationToken);
    }

    private static async Task ApplyVersionOneSchema(
        SqlConnection connection, CancellationToken cancellationToken)
    {
        var assembly = typeof(Database).Assembly;
        await using var stream = assembly.GetManifestResourceStream("U1.Business.Data.001-schema.sql")!;
        using var reader = new StreamReader(stream);
        await Execute(connection, await reader.ReadToEndAsync(cancellationToken), cancellationToken);
        Assert.Equal(1, await Scalar<int>(connection,
            "SELECT MAX(Version) FROM dbo.SchemaVersions", cancellationToken));
    }

    private static async Task SeedLegacyRecords(
        SqlConnection connection, string passwordHash, CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            DECLARE @userId int, @categoryId int, @productId int, @orderId int;
            INSERT dbo.Users(FirstName, LastName, Email, Phone, Company, PasswordHash, Role, IsActive, AuthVersion)
            VALUES(N'Legacy', N'Dealer', N'legacy@example.test', N'05321234567', N'Legacy Co', @passwordHash, 'Dealer', 1, 2);
            SET @userId = SCOPE_IDENTITY();
            INSERT dbo.Carts(UserId) VALUES(@userId);
            INSERT dbo.Categories(Name) VALUES(N'Legacy category');
            SET @categoryId = SCOPE_IDENTITY();
            INSERT dbo.Products(Code, Name, Description, Brand, ManufacturerCode, ImageUrl,
                Stock, CriticalStock, Price, CategoryId)
            VALUES(N'LEGACY-001', N'Legacy product', N'Legacy description', N'Legacy brand',
                N'LEGACY-MAKER', N'/images/product.svg', 7, 2, 12.50, @categoryId);
            SET @productId = SCOPE_IDENTITY();
            INSERT dbo.Orders(Number, UserId, Status, Total, RequestId, Note)
            VALUES('LEGACY-ORDER', @userId, N'Bekliyor', 25.00, @requestId, N'Legacy note');
            SET @orderId = SCOPE_IDENTITY();
            INSERT dbo.OrderItems(OrderId, ProductId, ProductCode, ProductName, Quantity, UnitPrice, Total)
            VALUES(@orderId, @productId, N'LEGACY-001', N'Legacy product snapshot', 2, 12.50, 25.00);
            """;
        command.Parameters.AddWithValue("@passwordHash", passwordHash);
        command.Parameters.AddWithValue("@requestId", Guid.NewGuid());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task Execute(SqlConnection connection, string sql, CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<T> Scalar<T>(SqlConnection connection, string sql, CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (T)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private sealed class UpgradeDbFactory(string connectionString) : IDbContextFactory<BusinessDbContext>
    {
        private readonly DbContextOptions<BusinessDbContext> options =
            new DbContextOptionsBuilder<BusinessDbContext>().UseSqlServer(connectionString).Options;

        public BusinessDbContext CreateDbContext() => new(options);
    }
}

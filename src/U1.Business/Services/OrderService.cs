using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using U1.Business.Data;
using U1.Business.Domain;

namespace U1.Business.Services;

public sealed class OrderService(Database database)
{
    private const int DeadlockErrorNumber = 1205;
    private const int MaxDeadlockAttempts = 3;

    public async Task<object> Checkout(int userId, CheckoutInput? input)
    {
        if (input is null)
            throw new BusinessException("Sipariş onayındaki ürün bilgileri geçersiz.");

        ValidateCheckout(input);

        for (var attempt = 1; attempt <= MaxDeadlockAttempts; attempt++)
        {
            try
            {
                return await CheckoutOnce(userId, input);
            }
            catch (SqlException ex) when (ex.Number == DeadlockErrorNumber && attempt < MaxDeadlockAttempts)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(40 * attempt));
            }
            catch (SqlException ex) when (ex.Number == DeadlockErrorNumber)
            {
                throw new BusinessException(
                    "Sipariş işlemi geçici yoğunluk nedeniyle tamamlanamadı. Aynı siparişi biraz sonra tekrar deneyin.",
                    503,
                    "CHECKOUT_RETRY");
            }
        }

        throw new InvalidOperationException("Sipariş yeniden deneme döngüsü beklenmeyen şekilde sona erdi.");
    }

    private static void ValidateCheckout(CheckoutInput input)
    {
        var lines = input.Lines;

        var invalid =
            input.RequestId == Guid.Empty ||
            input.Note?.Length > 1000 ||
            lines is null ||
            lines.Length == 0 ||
            lines.Any(line =>
                line is null ||
                line.ProductId <= 0 ||
                line.Quantity <= 0 ||
                line.UnitPrice <= 0) ||
            lines.Select(line => line.ProductId).Distinct().Count() != lines.Length;

        if (invalid)
            throw new BusinessException("Sipariş onayındaki ürün bilgileri geçersiz.");
    }

    private async Task<object> CheckoutOnce(int userId, CheckoutInput input)
    {
        var approvedLines = input.Lines
            ?? throw new BusinessException("Sipariş onayındaki ürün bilgileri geçersiz.");

        using var db = database.Open();
        await db.OpenAsync();

        using var tx = db.BeginTransaction(IsolationLevel.ReadCommitted);

        var cartId = await LockCart(db, tx, userId);
        await LockRequest(db, tx, input.RequestId);

        var existingOrder = await FindExistingOrder(db, tx, userId, input.RequestId);
        if (existingOrder is not null)
        {
            tx.Commit();
            return existingOrder;
        }

        await EnsureRequestIdIsAvailable(db, tx, input.RequestId);

        var cartLines = (await db.QueryAsync<CartLine>(
            "SELECT ProductId,Quantity FROM CartItems WHERE CartId=@cartId ORDER BY ProductId",
            new { cartId },
            tx)).ToList();

        if (cartLines.Count == 0)
            throw CartChanged();

        await LoadProductSnapshots(db, tx, cartLines);

        if (!MatchesApprovedCart(cartLines, approvedLines))
            throw CartChanged();

        EnsureStockIsAvailable(cartLines);

        var total = cartLines.Sum(line => line.Quantity * line.Product.Price);
        var orderNumber = CreateOrderNumber();

        var orderId = await db.ExecuteScalarAsync<int>(
            """
            INSERT INTO Orders(Number,UserId,Total,RequestId,Note)
            OUTPUT INSERTED.Id
            VALUES(@orderNumber,@userId,@total,@RequestId,@note)
            """,
            new
            {
                orderNumber,
                userId,
                total,
                input.RequestId,
                note = input.Note?.Trim() ?? ""
            },
            tx);

        foreach (var line in cartLines)
        {
            await db.ExecuteAsync(
                """
                INSERT INTO OrderItems(
                    OrderId,ProductId,ProductCode,ProductName,Quantity,UnitPrice,Total
                )
                VALUES(
                    @orderId,@ProductId,@Code,@Name,@Quantity,@Price,@lineTotal
                );

                UPDATE Products
                SET Stock=Stock-@Quantity
                WHERE Id=@ProductId;
                """,
                new
                {
                    orderId,
                    line.ProductId,
                    line.Product.Code,
                    line.Product.Name,
                    line.Quantity,
                    line.Product.Price,
                    lineTotal = line.Quantity * line.Product.Price
                },
                tx);
        }

        await db.ExecuteAsync(
            "DELETE FROM CartItems WHERE CartId=@cartId",
            new { cartId },
            tx);

        tx.Commit();

        return new
        {
            Id = orderId,
            Number = orderNumber,
            Total = total
        };
    }

    private static async Task<int> LockCart(SqlConnection db, SqlTransaction tx, int userId)
    {
        var cartId = await db.ExecuteScalarAsync<int?>(
            "SELECT Id FROM Carts WITH(UPDLOCK,HOLDLOCK) WHERE UserId=@userId",
            new { userId },
            tx);

        return cartId
            ?? throw new BusinessException("Sepet bulunamadı.", 404);
    }

    private static async Task LockRequest(
        SqlConnection db,
        SqlTransaction tx,
        Guid requestId)
    {
        var resource = "U1Business.Order." + requestId.ToString("N");

        var lockResult = await db.ExecuteScalarAsync<int>(
            """
            EXEC sp_getapplock
                @Resource=@resource,
                @LockMode='Exclusive',
                @LockOwner='Transaction',
                @LockTimeout=30000
            """,
            new { resource },
            tx);

        if (lockResult < 0)
        {
            throw new BusinessException(
                "Sipariş yeniden denenemedi. Biraz sonra tekrar deneyin.",
                409,
                "REQUEST_BUSY");
        }
    }

    private static Task<OrderSummary?> FindExistingOrder(
        SqlConnection db,
        SqlTransaction tx,
        int userId,
        Guid requestId) =>
        db.QuerySingleOrDefaultAsync<OrderSummary>(
            """
            SELECT Id,Number,Total
            FROM Orders WITH(READCOMMITTEDLOCK)
            WHERE RequestId=@requestId AND UserId=@userId
            """,
            new { requestId, userId },
            tx);

    private static async Task EnsureRequestIdIsAvailable(
        SqlConnection db,
        SqlTransaction tx,
        Guid requestId)
    {
        var requestIdExists = await db.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM Orders WITH(READCOMMITTEDLOCK)
            WHERE RequestId=@requestId
            """,
            new { requestId },
            tx) > 0;

        if (requestIdExists)
        {
            throw new BusinessException(
                "Bu sipariş anahtarı kullanılamaz.",
                409,
                "REQUEST_ID_CONFLICT");
        }
    }

    private static async Task LoadProductSnapshots(
        SqlConnection db,
        SqlTransaction tx,
        IReadOnlyList<CartLine> lines)
    {
        foreach (var line in lines)
        {
            line.Product = await db.QuerySingleAsync<ProductSnapshot>(
                """
                SELECT Code,Name,Price,Stock
                FROM Products WITH(UPDLOCK,HOLDLOCK)
                WHERE Id=@ProductId
                """,
                new { line.ProductId },
                tx);
        }
    }

    private static bool MatchesApprovedCart(
        IReadOnlyList<CartLine> cartLines,
        IReadOnlyList<CheckoutLine> approvedLines)
    {
        if (cartLines.Count != approvedLines.Count)
            return false;

        var approvedByProduct = approvedLines.ToDictionary(line => line.ProductId);

        return cartLines.All(line =>
            approvedByProduct.TryGetValue(line.ProductId, out var approved) &&
            approved.Quantity == line.Quantity &&
            approved.UnitPrice == line.Product.Price);
    }

    private static void EnsureStockIsAvailable(IEnumerable<CartLine> lines)
    {
        foreach (var line in lines)
        {
            if (line.Quantity <= line.Product.Stock)
                continue;

            throw new BusinessException(
                $"{line.Product.Name} için yeterli stok bulunmamaktadır. Mevcut stok: {line.Product.Stock}.",
                409,
                "INSUFFICIENT_STOCK");
        }
    }

    private static BusinessException CartChanged() =>
        new(
            "Sepetiniz onaydan sonra değişti. Güncel tutarı yeniden onaylayın.",
            409,
            "CART_CHANGED");

    private static string CreateOrderNumber() =>
        "U1-" +
        DateTime.UtcNow.ToString("yyyyMMdd") +
        "-" +
        Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();

    public async Task ChangeStatus(int id, string status)
    {
        if (status is not ("Onaylandı" or "Reddedildi"))
            throw new BusinessException("Geçersiz sipariş durumu.");

        using var db = database.Open();
        await db.OpenAsync();

        using var tx = db.BeginTransaction(IsolationLevel.Serializable);

        var currentStatus = await db.QuerySingleOrDefaultAsync<string>(
            "SELECT Status FROM Orders WITH(UPDLOCK,HOLDLOCK) WHERE Id=@id",
            new { id },
            tx)
            ?? throw new BusinessException("Sipariş bulunamadı.", 404);

        if (currentStatus == status)
        {
            tx.Commit();
            return;
        }

        if (currentStatus == "Reddedildi")
        {
            throw new BusinessException(
                "Reddedilen sipariş yeniden açılamaz. Yeni sipariş oluşturun.",
                409);
        }

        if (status == "Reddedildi")
            await RestoreStock(db, tx, id);

        await db.ExecuteAsync(
            "UPDATE Orders SET Status=@status WHERE Id=@id",
            new { id, status },
            tx);

        tx.Commit();
    }

    private static async Task RestoreStock(
        SqlConnection db,
        SqlTransaction tx,
        int orderId)
    {
        var lines = await db.QueryAsync<CartLine>(
            "SELECT ProductId,Quantity FROM OrderItems WHERE OrderId=@orderId ORDER BY ProductId",
            new { orderId },
            tx);

        foreach (var line in lines)
        {
            var updated = await db.ExecuteAsync(
                """
                UPDATE Products
                SET Stock=Stock+@Quantity
                WHERE Id=@ProductId
                  AND Stock<=2147483647-@Quantity
                """,
                line,
                tx);

            if (updated != 1)
            {
                throw new BusinessException(
                    "Stok iadesi tam sayı sınırını aşıyor. Fiziksel stoğu kontrol edin.",
                    409,
                    "STOCK_LIMIT");
            }
        }
    }

    private sealed class OrderSummary
    {
        public int Id { get; set; }
        public string Number { get; set; } = "";
        public decimal Total { get; set; }
    }

    private sealed class CartLine
    {
        public int ProductId { get; set; }
        public int Quantity { get; set; }
        public ProductSnapshot Product { get; set; } = new();
    }

    private sealed class ProductSnapshot
    {
        public string Code { get; set; } = "";
        public string Name { get; set; } = "";
        public decimal Price { get; set; }
        public int Stock { get; set; }
    }
}

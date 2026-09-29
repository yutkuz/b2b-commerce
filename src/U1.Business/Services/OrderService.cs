using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using U1.Business.Data;
using U1.Business.Domain;

namespace U1.Business.Services;

public sealed class OrderService(
    IDbContextFactory<BusinessDbContext> dbFactory,
    IConfiguration config)
{
    private const int DeadlockErrorNumber = 1205;
    private const int SqlCommandTimeoutErrorNumber = -2;
    private const int MaxTransientAttempts = 3;
    private readonly int requestLockTimeoutMilliseconds =
        config.GetValue<int?>("DatabaseLocks:OrderTimeoutMilliseconds") ?? 30_000;

    public async Task<object> Checkout(int userId, CheckoutInput? input)
    {
        if (input is null)
            throw new BusinessException("Sipariş onayındaki ürün bilgileri geçersiz.");

        ValidateCheckout(input);

        for (var attempt = 1; attempt <= MaxTransientAttempts; attempt++)
        {
            try
            {
                return await CheckoutOnce(userId, input);
            }
            catch (Exception ex) when (IsTransientSqlFailure(ex) && attempt < MaxTransientAttempts)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(40 * attempt));
            }
            catch (Exception ex) when (IsTransientSqlFailure(ex))
            {
                throw new BusinessException(
                    "Sipariş işlemi geçici yoğunluk nedeniyle tamamlanamadı. Aynı siparişi biraz sonra tekrar deneyin.",
                    503,
                    "CHECKOUT_RETRY");
            }
        }

        throw new InvalidOperationException("Sipariş yeniden deneme döngüsü beklenmeyen şekilde sona erdi.");
    }

    private static bool IsTransientSqlFailure(Exception ex) =>
        ex is SqlException sqlException
            && sqlException.Number is DeadlockErrorNumber or SqlCommandTimeoutErrorNumber
        || ex.InnerException is not null && IsTransientSqlFailure(ex.InnerException);

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

        await using var db = await dbFactory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);

        var cartId = await LockCart(db, userId);
        await LockRequest(db, input.RequestId);

        var existingOrder = await FindExistingOrder(db, userId, input.RequestId);
        if (existingOrder is not null)
        {
            await tx.CommitAsync();
            return existingOrder;
        }

        await EnsureRequestIdIsAvailable(db, input.RequestId);

        var cartLines = await db.CartItems
            .AsNoTracking()
            .Where(x => x.CartId == cartId)
            .OrderBy(x => x.ProductId)
            .Select(x => new CartLine
            {
                ProductId = x.ProductId,
                Quantity = x.Quantity
            })
            .ToListAsync();

        if (cartLines.Count == 0)
            throw CartChanged();

        await LoadProductSnapshots(db, cartLines);

        if (!MatchesApprovedCart(cartLines, approvedLines))
            throw CartChanged();

        EnsureStockIsAvailable(cartLines);

        var total = cartLines.Sum(line => line.Quantity * line.Product.Price);
        var order = new Order
        {
            Number = CreateOrderNumber(),
            UserId = userId,
            Total = total,
            RequestId = input.RequestId,
            Note = input.Note?.Trim() ?? "",
            Status = "Bekliyor"
        };

        db.Orders.Add(order);
        await db.SaveChangesAsync();

        foreach (var line in cartLines)
        {
            db.OrderItems.Add(new OrderItem
            {
                OrderId = order.Id,
                ProductId = line.ProductId,
                ProductCode = line.Product.Code,
                ProductName = line.Product.Name,
                Quantity = line.Quantity,
                UnitPrice = line.Product.Price,
                Total = line.Quantity * line.Product.Price
            });
            var previousStock = line.Product.Stock;
            line.Product.Stock -= line.Quantity;
            db.StockMovements.Add(AuditTrail.Stock(
                line.ProductId,
                previousStock,
                line.Product.Stock,
                "OrderPlaced",
                $"{order.Number} numaralı sipariş için stok ayrıldı.",
                userId,
                order.Id));
        }

        await db.CartItems
            .Where(x => x.CartId == cartId)
            .ExecuteDeleteAsync();

        await db.SaveChangesAsync();
        await tx.CommitAsync();

        return new
        {
            Id = order.Id,
            order.Number,
            order.Total
        };
    }

    private static async Task<int> LockCart(BusinessDbContext db, int userId)
    {
        var cart = await db.Carts
            .FromSqlInterpolated($"SELECT * FROM Carts WITH(UPDLOCK,HOLDLOCK) WHERE UserId={userId}")
            .AsNoTracking()
            .SingleOrDefaultAsync();

        return cart?.Id
            ?? throw new BusinessException("Sepet bulunamadı.", 404);
    }

    private async Task LockRequest(BusinessDbContext db, Guid requestId)
    {
        var resource = "U1Business.Order." + requestId.ToString("N");
        var connection = (SqlConnection)db.Database.GetDbConnection();

        var lockResult = await SqlApplicationLock.AcquireAsync(
            connection,
            (SqlTransaction)db.Database.CurrentTransaction!.GetDbTransaction(),
            resource,
            owner: "Transaction",
            requestLockTimeoutMilliseconds);
        if (lockResult < 0)
        {
            throw new BusinessException(
                "Sipariş yeniden denenemedi. Biraz sonra tekrar deneyin.",
                409,
                "REQUEST_BUSY");
        }
    }

    private static async Task<OrderSummary?> FindExistingOrder(
        BusinessDbContext db,
        int userId,
        Guid requestId)
    {
        return await db.Orders
            .AsNoTracking()
            .Where(x => x.RequestId == requestId && x.UserId == userId)
            .Select(x => new OrderSummary
            {
                Id = x.Id,
                Number = x.Number,
                Total = x.Total
            })
            .SingleOrDefaultAsync();
    }

    private static async Task EnsureRequestIdIsAvailable(
        BusinessDbContext db,
        Guid requestId)
    {
        if (await db.Orders.AsNoTracking().AnyAsync(x => x.RequestId == requestId))
        {
            throw new BusinessException(
                "Bu sipariş anahtarı kullanılamaz.",
                409,
                "REQUEST_ID_CONFLICT");
        }
    }

    private static async Task LoadProductSnapshots(
        BusinessDbContext db,
        IReadOnlyList<CartLine> lines)
    {
        foreach (var line in lines)
        {
            line.Product = await db.Products
                .FromSqlInterpolated($"SELECT * FROM Products WITH(UPDLOCK,HOLDLOCK) WHERE Id={line.ProductId}")
                .SingleAsync();
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

    public async Task ChangeStatus(int id, string status, int actorUserId)
    {
        if (status is not ("Onaylandı" or "Reddedildi"))
            throw new BusinessException("Geçersiz sipariş durumu.");

        for (var attempt = 1; attempt <= MaxTransientAttempts; attempt++)
        {
            try
            {
                await ChangeStatusOnce(id, status, actorUserId);
                return;
            }
            catch (Exception ex) when (IsTransientSqlFailure(ex) && attempt < MaxTransientAttempts)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(40 * attempt));
            }
            catch (Exception ex) when (IsTransientSqlFailure(ex))
            {
                throw new BusinessException("Sipariş durumu geçici veritabanı yoğunluğu nedeniyle değiştirilemedi. Biraz sonra tekrar deneyin.", 503, "ORDER_STATUS_RETRY");
            }
        }

        throw new InvalidOperationException("Sipariş durumu yeniden deneme döngüsü beklenmeyen şekilde sona erdi.");
    }

    private async Task ChangeStatusOnce(int id, string status, int actorUserId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);

        var order = await db.Orders
            .FromSqlInterpolated($"SELECT * FROM Orders WITH(UPDLOCK,HOLDLOCK) WHERE Id={id}")
            .SingleOrDefaultAsync()
            ?? throw new BusinessException("Sipariş bulunamadı.", 404);

        if (order.Status == status)
        {
            await tx.CommitAsync();
            return;
        }

        if (order.Status == "Reddedildi")
            throw new BusinessException("Reddedilen sipariş yeniden açılamaz. Yeni sipariş oluşturun.", 409);

        var previousStatus = order.Status;
        var audit = AuditTrail.Event(
            actorUserId,
            "OrderStatusChanged",
            "Order",
            order.Id,
            $"{order.Number} numaralı siparişin durumu {previousStatus} değerinden {status} değerine güncellendi.");
        db.AdminEvents.Add(audit);
        await db.SaveChangesAsync();

        if (status == "Reddedildi")
            await RestoreStock(db, order, actorUserId, audit.Id);

        order.Status = status;
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    private static async Task RestoreStock(
        BusinessDbContext db,
        Order order,
        int actorUserId,
        long adminEventId)
    {
        var lines = await db.OrderItems
            .AsNoTracking()
            .Where(x => x.OrderId == order.Id)
            .OrderBy(x => x.ProductId)
            .Select(x => new { x.ProductId, x.Quantity })
            .ToListAsync();

        foreach (var line in lines)
        {
            var product = await db.Products
                .FromSqlInterpolated($"SELECT * FROM Products WITH(UPDLOCK,HOLDLOCK) WHERE Id={line.ProductId}")
                .SingleAsync();

            if (product.Stock > int.MaxValue - line.Quantity)
            {
                throw new BusinessException(
                    "Stok iadesi tam sayı sınırını aşıyor. Fiziksel stoğu kontrol edin.",
                    409,
                    "STOCK_LIMIT");
            }

            var previousStock = product.Stock;
            product.Stock += line.Quantity;
            db.StockMovements.Add(AuditTrail.Stock(
                product.Id,
                previousStock,
                product.Stock,
                "OrderRejected",
                $"{order.Number} numaralı sipariş reddedildiği için stok iade edildi.",
                actorUserId,
                order.Id,
                adminEventId));
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
        public Product Product { get; set; } = null!;
    }
}

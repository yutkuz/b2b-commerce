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
        var discountPercent = await Pricing.LockDiscountFor(db, userId);

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
        foreach (var line in cartLines)
        {
            line.DiscountPercent = discountPercent;
            line.AppliedPrice = Pricing.Apply(line.Product.Price, discountPercent);
        }
        EnsureProductsAreSellable(cartLines);

        if (!MatchesApprovedCart(cartLines, approvedLines))
            throw CartChanged();

        EnsureStockIsAvailable(cartLines);

        var total = cartLines.Sum(line => line.Quantity * line.AppliedPrice);
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
        db.OrderStatusHistory.Add(new OrderStatusHistory
        {
            OrderId = order.Id,
            ToStatus = order.Status,
            ActorUserId = userId
        });

        foreach (var line in cartLines)
        {
            db.OrderItems.Add(new OrderItem
            {
                OrderId = order.Id,
                ProductId = line.ProductId,
                ProductCode = line.Product.Code,
                ProductName = line.Product.Name,
                Quantity = line.Quantity,
                ListUnitPrice = line.Product.Price,
                UnitPrice = line.AppliedPrice,
                DiscountPercent = line.DiscountPercent,
                Total = line.Quantity * line.AppliedPrice
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
            approved.UnitPrice == line.AppliedPrice);
    }

    private static void EnsureProductsAreSellable(IEnumerable<CartLine> lines)
    {
        foreach (var line in lines)
        {
            if (!line.Product.IsArchived)
                continue;

            throw new BusinessException(
                $"{line.Product.Name} artık satışta değil. Ürünü sepetten çıkarıp güncel sepeti yeniden onaylayın.",
                409,
                "PRODUCT_ARCHIVED");
        }
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

    public async Task ChangeStatus(int id, StatusInput input, int actorUserId)
    {
        if (input.Status is not ("Onaylandı" or "Hazırlanıyor" or "Sevk edildi" or "Teslim edildi" or "Reddedildi" or "İptal edildi"))
            throw new BusinessException("Geçersiz sipariş durumu.");
        if (input.Reason?.Length > 300)
            throw new BusinessException("Ret veya iptal nedeni en fazla 300 karakter olabilir.");
        if (input.Status is "Reddedildi" or "İptal edildi" && string.IsNullOrWhiteSpace(input.Reason))
            throw new BusinessException("Ret veya iptal nedeni gerekli.");

        for (var attempt = 1; attempt <= MaxTransientAttempts; attempt++)
        {
            try
            {
                await ChangeStatusOnce(id, input, actorUserId);
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

    private async Task ChangeStatusOnce(int id, StatusInput input, int actorUserId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);

        var order = await db.Orders
            .FromSqlInterpolated($"SELECT * FROM Orders WITH(UPDLOCK,HOLDLOCK) WHERE Id={id}")
            .SingleOrDefaultAsync()
            ?? throw new BusinessException("Sipariş bulunamadı.", 404);

        if (order.Status == input.Status)
        {
            await tx.CommitAsync();
            return;
        }

        if (input.RowVersion is not { Length: 8 })
            throw new BusinessException("Sipariş sürümü gerekli.");
        if (!order.RowVersion.AsSpan().SequenceEqual(input.RowVersion))
            throw new BusinessException("Sipariş başka bir işlemde değişti. Yeniden yükleyin.", 409, "ORDER_CHANGED");
        if (!CanTransition(order.Status, input.Status))
            throw new BusinessException("Bu sipariş durumu geçişine izin verilmiyor.", 409, "ORDER_STATUS_INVALID");

        var previousStatus = order.Status;
        var reason = input.Reason?.Trim() ?? "";
        var audit = AuditTrail.Event(
            actorUserId,
            "OrderStatusChanged",
            "Order",
            order.Id,
            $"{order.Number} numaralı siparişin durumu {previousStatus} değerinden {input.Status} değerine güncellendi.");
        db.AdminEvents.Add(audit);
        await db.SaveChangesAsync();

        if (input.Status is "Reddedildi" or "İptal edildi")
            await RestoreStock(db, order, actorUserId, audit.Id, input.Status);

        order.Status = input.Status;
        if (input.Status is "Reddedildi" or "İptal edildi")
            order.RejectionReason = reason;
        db.OrderStatusHistory.Add(new OrderStatusHistory
        {
            OrderId = order.Id,
            FromStatus = previousStatus,
            ToStatus = input.Status,
            ActorUserId = actorUserId,
            Reason = reason
        });
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    private static bool CanTransition(string current, string next) => current switch
    {
        "Bekliyor" => next is "Onaylandı" or "Reddedildi" or "İptal edildi",
        "Onaylandı" => next is "Hazırlanıyor" or "Reddedildi" or "İptal edildi",
        "Hazırlanıyor" => next is "Sevk edildi" or "Reddedildi" or "İptal edildi",
        "Sevk edildi" => next == "Teslim edildi",
        _ => false
    };

    public async Task UpdateAdminNote(int id, OrderNoteInput input, int actorUserId)
    {
        if (input.Note is null || input.Note.Length > 1000)
            throw new BusinessException("Yönetici notu en fazla 1000 karakter olabilir.");
        if (input.RowVersion is not { Length: 8 })
            throw new BusinessException("Sipariş sürümü gerekli.");

        await using var db = await dbFactory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var order = await db.Orders
            .FromSqlInterpolated($"SELECT * FROM Orders WITH(UPDLOCK,HOLDLOCK) WHERE Id={id}")
            .SingleOrDefaultAsync()
            ?? throw new BusinessException("Sipariş bulunamadı.", 404);
        if (!order.RowVersion.AsSpan().SequenceEqual(input.RowVersion))
            throw new BusinessException("Sipariş başka bir işlemde değişti. Yeniden yükleyin.", 409, "ORDER_CHANGED");

        var note = input.Note.Trim();
        if (order.AdminNote == note)
        {
            await tx.CommitAsync();
            return;
        }

        order.AdminNote = note;
        db.AdminEvents.Add(AuditTrail.Event(
            actorUserId, "OrderAdminNoteChanged", "Order", order.Id,
            $"{order.Number} numaralı siparişin yönetici notu güncellendi."));
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    public async Task<object> ReaddPreview(int userId, int orderId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        if (!await db.Orders.AsNoTracking().AnyAsync(x => x.Id == orderId && x.UserId == userId))
            throw new BusinessException("Sipariş bulunamadı.", 404);
        var discountPercent = await Pricing.DiscountFor(db, userId);

        var items = await (
            from line in db.OrderItems.AsNoTracking()
            join product in db.Products.AsNoTracking() on line.ProductId equals product.Id
            where line.OrderId == orderId
            orderby line.ProductId
            select new
            {
                line.ProductId,
                line.ProductCode,
                line.ProductName,
                line.Quantity,
                PreviousUnitPrice = line.UnitPrice,
                CurrentListPrice = product.Price,
                product.Stock,
                product.IsArchived
            }).ToListAsync();
        var cartQuantities = await (
            from cart in db.Carts.AsNoTracking()
            join cartItem in db.CartItems.AsNoTracking() on cart.Id equals cartItem.CartId
            where cart.UserId == userId
            select new { cartItem.ProductId, cartItem.Quantity })
            .ToDictionaryAsync(x => x.ProductId, x => x.Quantity);
        return new
        {
            items = items.Select(item => new
            {
                item.ProductId,
                item.ProductCode,
                item.ProductName,
                item.Quantity,
                item.PreviousUnitPrice,
                item.CurrentListPrice,
                CurrentUnitPrice = Pricing.Apply(item.CurrentListPrice, discountPercent),
                DiscountPercent = discountPercent,
                item.Stock,
                item.IsArchived,
                ExistingCartQuantity = cartQuantities.GetValueOrDefault(item.ProductId),
                CanAdd = !item.IsArchived &&
                    item.Quantity + (long)cartQuantities.GetValueOrDefault(item.ProductId) <= 1_000_000 &&
                    item.Stock >= item.Quantity + (long)cartQuantities.GetValueOrDefault(item.ProductId)
            }).ToList()
        };
    }

    public async Task ReaddToCart(int userId, int orderId, ReaddInput input)
    {
        var requested = input.Lines;
        if (requested is null || requested.Length == 0 || requested.Any(x =>
                x is null || x.ProductId <= 0 || x.Quantity <= 0 || x.UnitPrice <= 0)
            || requested.Select(x => x.ProductId).Distinct().Count() != requested.Length)
            throw new BusinessException("Sepete eklenecek sipariş kalemleri geçersiz.");

        await using var db = await dbFactory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var cart = await db.Carts
            .FromSqlInterpolated($"SELECT * FROM Carts WITH(UPDLOCK,HOLDLOCK) WHERE UserId={userId}")
            .SingleOrDefaultAsync()
            ?? throw new BusinessException("Sepet bulunamadı.", 404);
        if (!await db.Orders.AsNoTracking().AnyAsync(x => x.Id == orderId && x.UserId == userId))
            throw new BusinessException("Sipariş bulunamadı.", 404);
        var discountPercent = await Pricing.LockDiscountFor(db, userId);

        var originals = await db.OrderItems.AsNoTracking()
            .Where(x => x.OrderId == orderId)
            .ToDictionaryAsync(x => x.ProductId);

        foreach (var line in requested.OrderBy(x => x.ProductId))
        {
            if (!originals.TryGetValue(line.ProductId, out var original)
                || original.Quantity != line.Quantity)
                throw new BusinessException("Sipariş kalemleri değişti. Önizlemeyi yeniden açın.", 409, "ORDER_LINES_CHANGED");

            var product = await db.Products
                .FromSqlInterpolated($"SELECT * FROM Products WITH(UPDLOCK,HOLDLOCK) WHERE Id={line.ProductId}")
                .SingleOrDefaultAsync()
                ?? throw new BusinessException("Ürün artık bulunamıyor.", 409, "PRODUCT_UNAVAILABLE");
            if (product.IsArchived)
                throw new BusinessException("Ürün artık satışta değil.", 409, "PRODUCT_ARCHIVED");
            if (Pricing.Apply(product.Price, discountPercent) != line.UnitPrice)
                throw new BusinessException("Ürün fiyatı veya bayi indirimi değişti. Güncel tutarı yeniden onaylayın.", 409, "PRICE_CHANGED");

            var cartItem = await db.CartItems.SingleOrDefaultAsync(x =>
                x.CartId == cart.Id && x.ProductId == line.ProductId);
            var totalQuantity = (long)(cartItem?.Quantity ?? 0) + line.Quantity;
            if (totalQuantity > 1_000_000 || totalQuantity > product.Stock)
                throw new BusinessException("Ürün için yeterli stok yok. Güncel sepeti kontrol edin.", 409, "INSUFFICIENT_STOCK");

            if (cartItem is null)
                db.CartItems.Add(new CartItem
                {
                    CartId = cart.Id,
                    ProductId = line.ProductId,
                    Quantity = (int)totalQuantity
                });
            else
                cartItem.Quantity = (int)totalQuantity;
        }

        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    private static async Task RestoreStock(
        BusinessDbContext db,
        Order order,
        int actorUserId,
        long adminEventId,
        string status)
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
                status == "İptal edildi" ? "OrderCancelled" : "OrderRejected",
                $"{order.Number} numaralı sipariş {status.ToLowerInvariant()} için stok iade edildi.",
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
        public decimal AppliedPrice { get; set; }
        public decimal DiscountPercent { get; set; }
        public Product Product { get; set; } = null!;
    }
}

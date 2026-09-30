using System.Data;
using Microsoft.EntityFrameworkCore;
using U1.Business.Data;
using U1.Business.Domain;
using U1.Business.Services;

namespace U1.Business.Endpoints;

public static class CommerceEndpoints
{
    public static void MapCommerce(this WebApplication app)
    {
        var api = app.MapGroup("/api").RequireAuthorization();

        api.MapGet(
            "/cart",
            async (BusinessDbContext db, HttpContext c) =>
            {
                var id = c.UserId();
                var items = await (
                    from ca in db.Carts.AsNoTracking()
                    join ci in db.CartItems.AsNoTracking() on ca.Id equals ci.CartId
                    join p in db.Products.AsNoTracking() on ci.ProductId equals p.Id
                    where ca.UserId == id
                    orderby p.Id
                    select new
                    {
                        p.Id,
                        p.Code,
                        p.Name,
                        p.ImageUrl,
                        p.Price,
                        p.Stock,
                        p.IsArchived,
                        ci.Quantity,
                        UnavailableMessage = p.IsArchived
                            ? "Bu ürün artık satışta değil. Sepetten çıkarın."
                            : null,
                        Total = p.IsArchived ? 0m : p.Price * ci.Quantity
                    }).ToListAsync();

                return new
                {
                    items,
                    total = items.Sum(i => i.Total),
                    count = items.Sum(i => i.Quantity),
                };
            }
        );

        api.MapPost(
            "/cart",
            async (CartInput? input, BusinessDbContext db, HttpContext c) =>
                await SetCart(input, true, db, c)
        );

        api.MapPut(
            "/cart",
            async (CartInput? input, BusinessDbContext db, HttpContext c) =>
                await SetCart(input, false, db, c)
        );

        api.MapDelete(
            "/cart/{productId:int}",
            async (int productId, BusinessDbContext db, HttpContext c) =>
                await SetCart(new CartInput(productId, 0), false, db, c)
        );

        api.MapPost(
            "/orders",
            async (CheckoutInput input, OrderService orders, HttpContext c) =>
                await orders.Checkout(c.UserId(), input)
        );

        api.MapGet(
            "/orders/{id:int}/readd-preview",
            async (int id, OrderService orders, HttpContext c) =>
                await orders.ReaddPreview(c.UserId(), id)
        );

        api.MapPost(
            "/orders/{id:int}/readd",
            async (int id, ReaddInput input, OrderService orders, HttpContext c) =>
            {
                if (input is null)
                    throw new BusinessException("Sepete eklenecek ürünler gerekli.");
                await orders.ReaddToCart(c.UserId(), id, input);
                return Results.Ok();
            }
        );

        api.MapGet(
            "/orders",
            async (BusinessDbContext db, HttpContext c, int? page) =>
            {
                var id = c.UserId();
                var offset = (Math.Clamp(page ?? 1, 1, 100000) - 1) * 20;
                var query = db.Orders.AsNoTracking().Where(x => x.UserId == id);

                return new
                {
                    items = await query
                        .OrderByDescending(x => x.Id)
                        .Skip(offset)
                        .Take(20)
                        .Select(x => new { x.Id, x.Number, x.CreatedAt, x.Status, x.Total })
                        .ToListAsync(),
                    total = await query.CountAsync(),
                };
            }
        );

        api.MapGet(
            "/orders/{id:int}",
            async (int id, BusinessDbContext db, HttpContext c) =>
            {
                var userId = c.UserId();
                var admin = c.User.IsInRole("Admin");

                var order = await (
                    from o in db.Orders.AsNoTracking()
                    join u in db.Users.AsNoTracking() on o.UserId equals u.Id
                    where o.Id == id && (o.UserId == userId || admin)
                    select new
                    {
                        o.Id,
                        o.Number,
                        o.UserId,
                        o.CreatedAt,
                        o.Status,
                        o.Total,
                        o.RequestId,
                        o.Note,
                        o.RejectionReason,
                        o.RowVersion,
                        u.FirstName,
                        u.LastName,
                        u.Company
                    }).SingleOrDefaultAsync()
                    ?? throw new BusinessException("Sipariş bulunamadı.", 404);

                var items = await db.OrderItems
                    .AsNoTracking()
                    .Where(x => x.OrderId == id)
                    .OrderBy(x => x.Id)
                    .Select(x => new
                    {
                        x.ProductId,
                        x.ProductCode,
                        x.ProductName,
                        x.Quantity,
                        x.UnitPrice,
                        x.Total
                    })
                    .ToListAsync();

                var history = await db.OrderStatusHistory.AsNoTracking()
                    .Where(x => x.OrderId == id)
                    .OrderBy(x => x.ChangedAt)
                    .ThenBy(x => x.Id)
                    .Select(x => new
                    {
                        x.FromStatus,
                        x.ToStatus,
                        x.Reason,
                        x.ChangedAt
                    })
                    .ToListAsync();

                return new { order, items, history };
            }
        );
    }

    private static async Task<IResult> SetCart(
        CartInput? input,
        bool add,
        BusinessDbContext db,
        HttpContext c
    )
    {
        if (
            input is null
            || input.ProductId <= 0
            || input.Quantity < 0
            || input.Quantity > 1000000
            || (add && input.Quantity == 0)
        )
            throw new BusinessException("Geçerli bir ürün ve adet girin.");

        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var userId = c.UserId();
        var cart = await db.Carts
            .FromSqlInterpolated($"SELECT * FROM Carts WITH(UPDLOCK,HOLDLOCK) WHERE UserId={userId}")
            .SingleOrDefaultAsync()
            ?? throw new BusinessException("Sepet bulunamadı.", 404);

        var product = await db.Products
            .AsNoTracking()
            .Where(x => x.Id == input.ProductId)
            .Select(x => new { x.Stock, x.IsArchived })
            .SingleOrDefaultAsync()
            ?? throw new BusinessException("Ürün bulunamadı.", 404);

        var cartItem = await db.CartItems.SingleOrDefaultAsync(
            x => x.CartId == cart.Id && x.ProductId == input.ProductId);
        var current = cartItem?.Quantity ?? 0;
        var quantity = add ? (long)current + input.Quantity : input.Quantity;

        if (quantity > 1000000)
            throw new BusinessException(
                "Sepette bir ürün en fazla 1.000.000 adet olabilir.",
                400,
                "QUANTITY_LIMIT"
            );

        if (quantity > 0 && product.IsArchived)
            throw new BusinessException(
                "Bu ürün artık satışta değil. Sepetten çıkarın.",
                409,
                "PRODUCT_ARCHIVED"
            );

        if (quantity > product.Stock)
            throw new BusinessException(
                $"Yeterli stok yok. Mevcut stok: {product.Stock}. Sepetinizde: {current}.",
                409
            );

        if (quantity == 0)
        {
            if (cartItem is not null)
                db.CartItems.Remove(cartItem);
        }
        else if (cartItem is not null)
        {
            cartItem.Quantity = (int)quantity;
        }
        else
        {
            db.CartItems.Add(new CartItem
            {
                CartId = cart.Id,
                ProductId = input.ProductId,
                Quantity = (int)quantity
            });
        }

        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return Results.Ok();
    }
}

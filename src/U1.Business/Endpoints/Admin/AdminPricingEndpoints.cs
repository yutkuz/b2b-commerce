using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using U1.Business.Data;
using U1.Business.Domain;
using U1.Business.Services;

namespace U1.Business.Endpoints;

public static partial class AdminEndpoints
{
    private static void MapPricing(RouteGroupBuilder api)
    {
        api.MapGet(
            "/dealer-groups",
            async (BusinessDbContext db) =>
            {
                var items = await db.DealerGroups
                    .AsNoTracking()
                    .OrderBy(x => x.Id)
                    .Select(x => new
                    {
                        x.Id,
                        x.Name,
                        x.DiscountPercent,
                        x.RowVersion,
                        DealerCount = db.Users.Count(u => u.Role == "Dealer" && u.DealerGroupId == x.Id)
                    })
                    .ToListAsync();
                return new { items };
            });

        api.MapPost(
            "/dealer-groups",
            async (DealerGroupInput input, BusinessDbContext db, HttpContext c) =>
            {
                if (input is null)
                    throw new BusinessException("Bayi grubu bilgileri gerekli.");
                Rules.Validate(input);
                ValidateDiscount(input.DiscountPercent);

                var group = new DealerGroup
                {
                    Name = input.Name.Trim(),
                    DiscountPercent = input.DiscountPercent
                };
                db.DealerGroups.Add(group);
                try
                {
                    await db.SaveChangesAsync();
                }
                catch (DbUpdateException ex) when (IsUniqueGroupName(ex))
                {
                    throw new BusinessException("Bu bayi grubu adı zaten kullanılıyor.", 409, "DEALER_GROUP_NAME_EXISTS");
                }

                db.AdminEvents.Add(AuditTrail.Event(
                    c.UserId(), "DealerGroupCreated", "DealerGroup", group.Id,
                    $"{group.Name} bayi grubu %{group.DiscountPercent:0.00} iskonto ile oluşturuldu."));
                await db.SaveChangesAsync();
                return Results.Ok(new { id = group.Id });
            });

        api.MapPut(
            "/dealer-groups/{id:int}",
            async (int id, DealerGroupUpdateInput input, BusinessDbContext db, HttpContext c) =>
            {
                if (input is null || input.RowVersion is not { Length: 8 })
                    throw new BusinessException("Bayi grubu sürümü gerekli.");
                Rules.Validate(input);
                ValidateDiscount(input.DiscountPercent);

                await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
                var group = await db.DealerGroups
                    .FromSqlInterpolated($"SELECT * FROM DealerGroups WITH(UPDLOCK,HOLDLOCK) WHERE Id={id}")
                    .SingleOrDefaultAsync()
                    ?? throw new BusinessException("Bayi grubu bulunamadı.", 404);
                if (!group.RowVersion.AsSpan().SequenceEqual(input.RowVersion))
                    throw new BusinessException("Bayi grubu başka bir işlemde değişti. Yeniden yükleyin.", 409, "DEALER_GROUP_CHANGED");

                var previous = group.DiscountPercent;
                group.Name = input.Name.Trim();
                group.DiscountPercent = input.DiscountPercent;
                try
                {
                    await db.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    throw new BusinessException("Bayi grubu başka bir işlemde değişti. Yeniden yükleyin.", 409, "DEALER_GROUP_CHANGED");
                }
                catch (DbUpdateException ex) when (IsUniqueGroupName(ex))
                {
                    throw new BusinessException("Bu bayi grubu adı zaten kullanılıyor.", 409, "DEALER_GROUP_NAME_EXISTS");
                }

                db.AdminEvents.Add(AuditTrail.Event(
                    c.UserId(), "DealerGroupUpdated", "DealerGroup", group.Id,
                    $"{group.Name} bayi grubu iskontosu %{previous:0.00} değerinden %{group.DiscountPercent:0.00} değerine güncellendi."));
                await db.SaveChangesAsync();
                await tx.CommitAsync();
                return Results.Ok();
            });
    }

    private static void ValidateDiscount(decimal discountPercent)
    {
        if (decimal.Round(discountPercent, 2) != discountPercent)
            throw new BusinessException("İskonto en fazla iki ondalık basamak içermeli.");
    }

    private static bool IsUniqueGroupName(DbUpdateException exception) =>
        exception.InnerException is SqlException { Number: 2601 or 2627 };
}

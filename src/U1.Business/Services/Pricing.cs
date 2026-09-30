using Microsoft.EntityFrameworkCore;
using U1.Business.Data;

namespace U1.Business.Services;

public static class Pricing
{
    public static decimal Apply(decimal listPrice, decimal discountPercent) =>
        decimal.Round(
            listPrice * (100m - discountPercent) / 100m,
            2,
            MidpointRounding.AwayFromZero);

    public static async Task<decimal> DiscountFor(BusinessDbContext db, int userId)
    {
        var groupId = await db.Users
            .AsNoTracking()
            .Where(x => x.Id == userId)
            .Select(x => x.DealerGroupId)
            .SingleOrDefaultAsync();
        if (groupId is null)
            return 0m;

        return await db.DealerGroups
            .AsNoTracking()
            .Where(x => x.Id == groupId)
            .Select(x => x.DiscountPercent)
            .SingleAsync();
    }

    public static async Task<decimal> LockDiscountFor(BusinessDbContext db, int userId)
    {
        var user = await db.Users
            .FromSqlInterpolated($"SELECT * FROM Users WITH(UPDLOCK,HOLDLOCK) WHERE Id={userId}")
            .AsNoTracking()
            .SingleAsync();
        if (user.DealerGroupId is null)
            return 0m;

        var group = await db.DealerGroups
            .FromSqlInterpolated($"SELECT * FROM DealerGroups WITH(UPDLOCK,HOLDLOCK) WHERE Id={user.DealerGroupId.Value}")
            .AsNoTracking()
            .SingleAsync();
        return group.DiscountPercent;
    }
}

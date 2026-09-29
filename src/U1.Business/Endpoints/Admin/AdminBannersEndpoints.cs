using Microsoft.EntityFrameworkCore;
using U1.Business.Data;
using U1.Business.Domain;
using U1.Business.Services;

namespace U1.Business.Endpoints;

public static partial class AdminEndpoints
{
    private static void MapBanners(RouteGroupBuilder api)
    {
        api.MapGet(
            "/banners",
            async (BusinessDbContext db) =>
                await db.Banners
                    .AsNoTracking()
                    .OrderBy(x => x.Position)
                    .ThenBy(x => x.Id)
                    .ToListAsync()
        );

        api.MapPost(
            "/banners",
            async (BannerInput input, BusinessDbContext db, HttpContext c) =>
            {
                if (input is null)
                    throw new BusinessException("Duyuru bilgileri gerekli.");
                input.SearchTerm ??= "";
                Rules.Validate(input);
                await using var tx = await db.Database.BeginTransactionAsync();

                var banner = new Banner
                {
                    Title = input.Title,
                    Subtitle = input.Subtitle,
                    ButtonText = input.ButtonText,
                    SearchTerm = input.SearchTerm,
                    IsActive = input.IsActive,
                    Position = input.Position
                };
                db.Banners.Add(banner);
                await db.SaveChangesAsync();
                db.AdminEvents.Add(AuditTrail.Event(
                    c.UserId(),
                    "BannerCreated",
                    "Banner",
                    banner.Id,
                    "Duyuru oluşturuldu."));
                await db.SaveChangesAsync();
                await tx.CommitAsync();
                return new { id = banner.Id };
            }
        );

        api.MapPut(
            "/banners/{id:int}",
            async (int id, BannerUpdateInput input, BusinessDbContext db, HttpContext c) =>
            {
                if (input is null)
                    throw new BusinessException("Duyuru bilgileri gerekli.");
                input.SearchTerm ??= "";
                Rules.Validate(input);
                EnsureRowVersion(input.RowVersion, "Duyuru sürümü geçersiz.");
                await using var tx = await db.Database.BeginTransactionAsync();

                var banner = await db.Banners.SingleOrDefaultAsync(x => x.Id == id)
                    ?? throw new BusinessException("Duyuru bulunamadı.", 404);

                db.Entry(banner).Property(x => x.RowVersion).OriginalValue = input.RowVersion;
                banner.Title = input.Title;
                banner.Subtitle = input.Subtitle;
                banner.ButtonText = input.ButtonText;
                banner.SearchTerm = input.SearchTerm;
                banner.IsActive = input.IsActive;
                banner.Position = input.Position;

                try
                {
                    await db.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    db.ChangeTracker.Clear();
                    if (!await db.Banners.AsNoTracking().AnyAsync(x => x.Id == id))
                        throw new BusinessException("Duyuru bulunamadı.", 404);

                    throw new BusinessException(
                        "Duyuru başka bir yönetici tarafından değiştirildi. Girdilerinizi koruyarak güncel duyuruyu yeniden yükleyin.",
                        409,
                        "BANNER_CHANGED"
                    );
                }

                db.AdminEvents.Add(AuditTrail.Event(
                    c.UserId(),
                    "BannerUpdated",
                    "Banner",
                    banner.Id,
                    "Duyuru güncellendi."));
                await db.SaveChangesAsync();
                await tx.CommitAsync();
                return Results.Ok();
            }
        );
    }
}

using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using U1.Business.Data;
using U1.Business.Domain;
using U1.Business.Services;

namespace U1.Business.Endpoints;

public static partial class AdminEndpoints
{
    private static void MapCategories(RouteGroupBuilder api)
    {
        api.MapGet(
            "/categories",
            async (BusinessDbContext db) =>
                await db.Categories
                    .AsNoTracking()
                    .OrderBy(x => x.Name)
                    .ThenBy(x => x.Id)
                    .Select(x => new
                    {
                        x.Id,
                        x.Name,
                        x.RowVersion,
                        ProductCount = db.Products.Count(p => p.CategoryId == x.Id),
                        ArchivedProductCount = db.Products.Count(p => p.CategoryId == x.Id && p.IsArchived)
                    })
                    .ToListAsync()
        );

        api.MapPost(
            "/categories",
            async (CategoryInput input, BusinessDbContext db, HttpContext c) =>
            {
                if (input is null)
                    throw new BusinessException("Kategori bilgileri gerekli.");
                Rules.Validate(input);
                var name = input.Name.Trim();
                if (name.Length == 0)
                    throw new BusinessException("Kategori adı gerekli.");

                await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
                if (await db.Categories.AnyAsync(x => x.Name == name))
                    throw new BusinessException("Bu kategori adı zaten kullanılıyor.", 409, "CATEGORY_NAME_EXISTS");

                var category = new Category { Name = name };
                db.Categories.Add(category);
                try
                {
                    await db.SaveChangesAsync();
                }
                catch (DbUpdateException ex) when (IsUniqueConstraint(ex))
                {
                    throw new BusinessException("Bu kategori adı zaten kullanılıyor.", 409, "CATEGORY_NAME_EXISTS");
                }

                db.AdminEvents.Add(AuditTrail.Event(
                    c.UserId(),
                    "CategoryCreated",
                    "Category",
                    category.Id,
                    $"{category.Name} kategorisi oluşturuldu."));
                await db.SaveChangesAsync();
                await tx.CommitAsync();
                return Results.Ok(new { id = category.Id });
            }
        );

        api.MapPut(
            "/categories/{id:int}",
            async (int id, CategoryUpdateInput input, BusinessDbContext db, HttpContext c) =>
            {
                if (input is null)
                    throw new BusinessException("Kategori bilgileri gerekli.");
                Rules.Validate(input);
                EnsureRowVersion(input.RowVersion, "Kategori sürümü geçersiz.");
                var name = input.Name.Trim();
                if (name.Length == 0)
                    throw new BusinessException("Kategori adı gerekli.");

                await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
                var category = await db.Categories.SingleOrDefaultAsync(x => x.Id == id)
                    ?? throw new BusinessException("Kategori bulunamadı.", 404);
                db.Entry(category).Property(x => x.RowVersion).OriginalValue = input.RowVersion;

                if (await db.Categories.AnyAsync(x => x.Id != id && x.Name == name))
                    throw new BusinessException("Bu kategori adı zaten kullanılıyor.", 409, "CATEGORY_NAME_EXISTS");

                category.Name = name;
                try
                {
                    await db.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    throw CategoryChanged();
                }
                catch (DbUpdateException ex) when (IsUniqueConstraint(ex))
                {
                    throw new BusinessException("Bu kategori adı zaten kullanılıyor.", 409, "CATEGORY_NAME_EXISTS");
                }

                db.AdminEvents.Add(AuditTrail.Event(
                    c.UserId(),
                    "CategoryUpdated",
                    "Category",
                    category.Id,
                    $"{category.Name} kategorisi güncellendi."));
                await db.SaveChangesAsync();
                await tx.CommitAsync();
                return Results.Ok();
            }
        );

        api.MapPost(
            "/categories/{id:int}/merge",
            async (int id, CategoryMergeInput input, BusinessDbContext db, HttpContext c) =>
            {
                if (input is null || id <= 0 || input.TargetCategoryId <= 0 || id == input.TargetCategoryId)
                    throw new BusinessException("Birleştirme için farklı iki kategori seçin.");
                EnsureRowVersion(input.SourceRowVersion, "Kaynak kategori sürümü geçersiz.");
                EnsureRowVersion(input.TargetRowVersion, "Hedef kategori sürümü geçersiz.");

                await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
                var source = await db.Categories.SingleOrDefaultAsync(x => x.Id == id)
                    ?? throw new BusinessException("Kaynak kategori bulunamadı.", 404);
                var target = await db.Categories.SingleOrDefaultAsync(x => x.Id == input.TargetCategoryId)
                    ?? throw new BusinessException("Hedef kategori bulunamadı.", 404);

                if (!source.RowVersion.AsSpan().SequenceEqual(input.SourceRowVersion)
                    || !target.RowVersion.AsSpan().SequenceEqual(input.TargetRowVersion))
                    throw CategoryChanged();

                db.Entry(source).Property(x => x.RowVersion).OriginalValue = input.SourceRowVersion;

                var products = await db.Products
                    .FromSqlInterpolated($"SELECT * FROM Products WITH(UPDLOCK,HOLDLOCK) WHERE CategoryId={id}")
                    .ToListAsync();
                foreach (var product in products)
                    product.CategoryId = target.Id;

                db.Categories.Remove(source);
                try
                {
                    await db.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    throw CategoryChanged();
                }

                db.AdminEvents.Add(AuditTrail.Event(
                    c.UserId(),
                    "CategoryMerged",
                    "Category",
                    target.Id,
                    $"{source.Name} kategorisi {target.Name} kategorisiyle birleştirildi; {products.Count} ürün taşındı."));
                await db.SaveChangesAsync();
                await tx.CommitAsync();
                return Results.Ok(new { movedProducts = products.Count });
            }
        );
    }

    private static BusinessException CategoryChanged() =>
        new(
            "Kategori başka bir işlemde değişti. Güncel kategorileri yeniden yükleyin.",
            409,
            "CATEGORY_CHANGED");

    private static bool IsUniqueConstraint(DbUpdateException exception) =>
        exception.InnerException is SqlException sql && sql.Number is 2601 or 2627;
}

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
            async (CategoryInput input, IDbContextFactory<BusinessDbContext> factory, HttpContext c) =>
            {
                if (input is null)
                    throw new BusinessException("Kategori bilgileri gerekli.");
                Rules.Validate(input);
                var name = input.Name.Trim();
                if (name.Length == 0)
                    throw new BusinessException("Kategori adı gerekli.");

                return await RetryCategoryWrite(factory, async db =>
                {
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
                });
            }
        );

        api.MapPut(
            "/categories/{id:int}",
            async (int id, CategoryUpdateInput input, IDbContextFactory<BusinessDbContext> factory, HttpContext c) =>
            {
                if (input is null)
                    throw new BusinessException("Kategori bilgileri gerekli.");
                Rules.Validate(input);
                EnsureRowVersion(input.RowVersion, "Kategori sürümü geçersiz.");
                var name = input.Name.Trim();
                if (name.Length == 0)
                    throw new BusinessException("Kategori adı gerekli.");

                return await RetryCategoryWrite(factory, async db =>
                {
                    await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
                    var category = await LockCategory(db, id)
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
                });
            }
        );

        api.MapPost(
            "/categories/{id:int}/merge",
            async (int id, CategoryMergeInput input, IDbContextFactory<BusinessDbContext> factory, HttpContext c) =>
            {
                if (input is null || id <= 0 || input.TargetCategoryId <= 0 || id == input.TargetCategoryId)
                    throw new BusinessException("Birleştirme için farklı iki kategori seçin.");
                EnsureRowVersion(input.SourceRowVersion, "Kaynak kategori sürümü geçersiz.");
                EnsureRowVersion(input.TargetRowVersion, "Hedef kategori sürümü geçersiz.");

                return await RetryCategoryWrite(factory, async db =>
                {
                    await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
                    // Lock both rows in the same order for A→B and B→A merges.
                    var first = await LockCategory(db, Math.Min(id, input.TargetCategoryId));
                    var second = await LockCategory(db, Math.Max(id, input.TargetCategoryId));
                    var source = (id < input.TargetCategoryId ? first : second)
                        ?? throw CategoryChanged();
                    var target = (id < input.TargetCategoryId ? second : first)
                        ?? throw CategoryChanged();

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
                });
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

    private static async Task<Category?> LockCategory(BusinessDbContext db, int id) =>
        await db.Categories
            .FromSqlInterpolated($"SELECT * FROM Categories WITH(UPDLOCK,HOLDLOCK) WHERE Id={id}")
            .SingleOrDefaultAsync();

    internal static async Task<IResult> RetryCategoryWrite(
        IDbContextFactory<BusinessDbContext> factory,
        Func<BusinessDbContext, Task<IResult>> operation)
    {
        const int maxAttempts = 3;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                // A failed attempt must dispose both its transaction and tracked EF entities.
                await using var db = await factory.CreateDbContextAsync();
                return await operation(db);
            }
            catch (Exception ex) when (IsTransientCategoryFailure(ex) && attempt < maxAttempts)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(40 * attempt));
            }
            catch (Exception ex) when (IsTransientCategoryFailure(ex))
            {
                throw new BusinessException(
                    "Kategori işlemi geçici veritabanı yoğunluğu nedeniyle tamamlanamadı. Biraz sonra tekrar deneyin.",
                    503,
                    "CATEGORY_RETRY");
            }
        }

        throw new InvalidOperationException("Kategori yeniden deneme döngüsü beklenmeyen şekilde sona erdi.");
    }

    private static bool IsTransientCategoryFailure(Exception ex) =>
        ex is SqlException { Number: 1205 or -2 }
        || ex.InnerException is not null && IsTransientCategoryFailure(ex.InnerException);
}

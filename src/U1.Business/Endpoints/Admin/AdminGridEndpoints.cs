using Microsoft.EntityFrameworkCore;
using U1.Business.Data;
using U1.Business.Domain;

namespace U1.Business.Endpoints;

public static partial class AdminEndpoints
{
    private static void MapGrid(RouteGroupBuilder api)
    {
        api.MapGet(
            "/grid",
            async (BusinessDbContext db) =>
                await db.GridColumns
                    .AsNoTracking()
                    .OrderBy(x => x.Position)
                    .ThenBy(x => x.Id)
                    .ToListAsync()
        );

        api.MapPut(
            "/grid",
            async (GridColumn[] columns, BusinessDbContext db) =>
            {
                if (
                    columns is null
                    || columns.Length is < 1 or > 20
                    || columns.Any(c => c is null)
                    || columns.Select(c => c.Id).Distinct().Count() != columns.Length
                )
                    throw new BusinessException("Kolon yapılandırması geçersiz.");

                await using var tx = await db.Database.BeginTransactionAsync();
                var saved = await db.GridColumns.ToListAsync();
                if (saved.Count != columns.Length)
                    throw new BusinessException("Tüm kolonları kaydedin.");

                foreach (var col in columns)
                {
                    Rules.Validate(col);
                    var original = saved.SingleOrDefault(x => x.Id == col.Id);
                    if (
                        original is null
                        || original.Field != col.Field
                        || col.Align is not ("left" or "center" or "right")
                    )
                        throw new BusinessException("Kolon bilgileri geçersiz.");

                    var types = col.Field switch
                    {
                        "imageUrl" => new[] { "image", "text" },
                        "stock" => new[] { "stock", "text" },
                        "price" => new[] { "money", "text" },
                        "quantity" => new[] { "purchase" },
                        "name" => new[] { "product", "text" },
                        _ => new[] { "text" },
                    };
                    if (!types.Contains(col.RenderType))
                        throw new BusinessException("Bu alan için render tipi geçersiz.");

                    if (
                        col.Field is "name" or "price" or "quantity"
                        && !(col.Desktop && col.Tablet && col.Mobile)
                    )
                        throw new BusinessException(
                            "Ürün adı, fiyat ve satın alma kolonları tüm cihazlarda açık kalmalıdır."
                        );

                    original.Label = col.Label;
                    original.RenderType = col.RenderType;
                    original.Position = col.Position;
                    original.Width = col.Width;
                    original.Align = col.Align;
                    original.Desktop = col.Desktop;
                    original.Tablet = col.Tablet;
                    original.Mobile = col.Mobile;
                }

                await db.SaveChangesAsync();
                await tx.CommitAsync();
                return Results.Ok();
            }
        );
    }
}

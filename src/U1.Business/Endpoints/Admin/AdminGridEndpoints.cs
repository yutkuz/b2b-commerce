using Dapper;
using U1.Business.Data;
using U1.Business.Domain;

namespace U1.Business.Endpoints;

public static partial class AdminEndpoints
{
    private static void MapGrid(RouteGroupBuilder api)
    {
        api.MapGet(
            "/grid",
            async (Database database) =>
            {
                using var db = database.Open();
                return await db.QueryAsync<GridColumn>(
                    "SELECT * FROM GridColumns ORDER BY Position,Id"
                );
            }
        );
        api.MapPut(
            "/grid",
            async (GridColumn[] columns, Database database) =>
            {
                if (
                    columns is null
                    || columns.Length is < 1 or > 20
                    || columns.Any(c => c is null)
                    || columns.Select(c => c.Id).Distinct().Count() != columns.Length
                )
                    throw new BusinessException("Kolon yapılandırması geçersiz.");
                using var db = database.Open();
                await db.OpenAsync();
                using var tx = db.BeginTransaction();
                var saved = (
                    await db.QueryAsync<GridColumn>("SELECT * FROM GridColumns", transaction: tx)
                ).ToList();
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
                    await db.ExecuteAsync(
                        "UPDATE GridColumns SET Label=@Label,RenderType=@RenderType,Position=@Position,Width=@Width,Align=@Align,Desktop=@Desktop,Tablet=@Tablet,Mobile=@Mobile WHERE Id=@Id",
                        col,
                        tx
                    );
                }
                tx.Commit();
                return Results.Ok();
            }
        );
    }
}

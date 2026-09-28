using Dapper;
using U1.Business.Data;
using U1.Business.Domain;

namespace U1.Business.Endpoints;

public static partial class AdminEndpoints
{
    private static void MapBanners(RouteGroupBuilder api)
    {
        api.MapGet(
            "/banners",
            async (Database database) =>
            {
                using var db = database.Open();
                return await db.QueryAsync("SELECT * FROM Banners ORDER BY Position,Id");
            }
        );
        api.MapPost(
            "/banners",
            async (BannerInput input, Database database) =>
            {
                if (input is null)
                    throw new BusinessException("Duyuru bilgileri gerekli.");
                input.SearchTerm ??= "";
                Rules.Validate(input);
                using var db = database.Open();
                return new
                {
                    id = await db.ExecuteScalarAsync<int>(
                        "INSERT INTO Banners(Title,Subtitle,ButtonText,SearchTerm,IsActive,Position) OUTPUT INSERTED.Id VALUES(@Title,@Subtitle,@ButtonText,@SearchTerm,@IsActive,@Position)",
                        input
                    ),
                };
            }
        );
        api.MapPut(
            "/banners/{id:int}",
            async (int id, BannerInput input, Database database) =>
            {
                if (input is null)
                    throw new BusinessException("Duyuru bilgileri gerekli.");
                input.SearchTerm ??= "";
                Rules.Validate(input);
                using var db = database.Open();
                var args = new DynamicParameters(input);
                args.Add("Id", id);
                if (
                    await db.ExecuteAsync(
                        "UPDATE Banners SET Title=@Title,Subtitle=@Subtitle,ButtonText=@ButtonText,SearchTerm=@SearchTerm,IsActive=@IsActive,Position=@Position WHERE Id=@Id",
                        args
                    ) == 0
                )
                    throw new BusinessException("Duyuru bulunamadı.", 404);
                return Results.Ok();
            }
        );
    }
}

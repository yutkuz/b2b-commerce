using Dapper;
using U1.Business.Data;
using U1.Business.Domain;

namespace U1.Business.Endpoints;

public static partial class AdminEndpoints
{
    private const string ProductValues =
        "Code=@Code,Name=@Name,Description=@Description,Brand=@Brand,ManufacturerCode=@ManufacturerCode,SpecialCode1=@SpecialCode1,SpecialCode2=@SpecialCode2,ImageUrl=@ImageUrl,Stock=@Stock,CriticalStock=@CriticalStock,Price=@Price,CategoryId=@CategoryId";

    private static void MapProducts(RouteGroupBuilder api)
    {
        api.MapPost(
            "/products",
            async (ProductInput input, Database database) =>
            {
                await ValidateProduct(input, database);
                using var db = database.Open();
                var id = await db.ExecuteScalarAsync<int>(
                    "INSERT INTO Products(Code,Name,Description,Brand,ManufacturerCode,SpecialCode1,SpecialCode2,ImageUrl,Stock,CriticalStock,Price,CategoryId) OUTPUT INSERTED.Id VALUES(@Code,@Name,@Description,@Brand,@ManufacturerCode,@SpecialCode1,@SpecialCode2,@ImageUrl,@Stock,@CriticalStock,@Price,@CategoryId)",
                    input
                );
                return Results.Ok(new { id });
            }
        );
        api.MapPut(
            "/products/{id:int}",
            async (int id, ProductUpdateInput input, Database database) =>
            {
                if (input is null)
                    throw new BusinessException("Ürün bilgileri gerekli.");
                if (string.IsNullOrWhiteSpace(input.Version))
                    throw new BusinessException("Ürün sürümü gerekli.");
                byte[] version;
                try
                {
                    version = Convert.FromBase64String(input.Version);
                }
                catch (FormatException)
                {
                    throw new BusinessException("Ürün sürümü geçersiz.");
                }
                if (version.Length != 8)
                    throw new BusinessException("Ürün sürümü geçersiz.");
                await ValidateProduct(input, database);
                using var db = database.Open();
                var args = new DynamicParameters(input);
                args.Add("Id", id);
                args.Add("ExpectedVersion", version);
                if (
                    await db.ExecuteAsync(
                        "UPDATE Products SET "
                            + ProductValues
                            + " WHERE Id=@Id AND RowVersion=@ExpectedVersion",
                        args
                    ) == 0
                )
                {
                    if (
                        await db.ExecuteScalarAsync<int>(
                            "SELECT COUNT(*) FROM Products WHERE Id=@id",
                            new { id }
                        ) == 0
                    )
                        throw new BusinessException("Ürün bulunamadı.", 404);
                    throw new BusinessException(
                        "Ürün başka bir işlemde değişti. Girdilerinizi koruyarak güncel ürünü yeniden yükleyin.",
                        409,
                        "PRODUCT_CHANGED"
                    );
                }
                return Results.Ok();
            }
        );
        api.MapPost(
            "/images",
            async (HttpRequest request, IWebHostEnvironment env) =>
            {
                if (!request.HasFormContentType || request.ContentLength > 5 * 1024 * 1024)
                    throw new BusinessException(
                        "En fazla 4 MB boyutunda PNG, JPEG veya WebP seçin."
                    );
                var form = await request.ReadFormAsync();
                var file = form.Files.GetFile("file");
                if (file is null || file.Length == 0 || file.Length > 4 * 1024 * 1024)
                    throw new BusinessException("Geçerli bir görsel seçin (en fazla 4 MB).");
                using var ms = new MemoryStream();
                await file.CopyToAsync(ms);
                var bytes = ms.ToArray();
                string ext;
                if (
                    bytes.Length > 8
                    && bytes[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })
                )
                    ext = ".png";
                else if (bytes.Length > 3 && bytes[0] == 255 && bytes[1] == 216 && bytes[2] == 255)
                    ext = ".jpg";
                else if (
                    bytes.Length > 12
                    && System.Text.Encoding.ASCII.GetString(bytes, 0, 4) == "RIFF"
                    && System.Text.Encoding.ASCII.GetString(bytes, 8, 4) == "WEBP"
                )
                    ext = ".webp";
                else
                    throw new BusinessException(
                        "Yalnızca PNG, JPEG ve WebP dosyaları kabul edilir."
                    );
                var name = Guid.NewGuid().ToString("N") + ext;
                var folder = Path.Combine(env.WebRootPath, "uploads");
                Directory.CreateDirectory(folder);
                await File.WriteAllBytesAsync(Path.Combine(folder, name), bytes);
                return Results.Ok(new { url = "/uploads/" + name });
            }
        );
    }

    private static async Task ValidateProduct(ProductInput input, Database database)
    {
        if (input is null)
            throw new BusinessException("Ürün bilgileri gerekli.");
        input.SpecialCode1 ??= "";
        input.SpecialCode2 ??= "";
        Rules.Validate(input);
        if (decimal.Round(input.Price, 2) != input.Price)
            throw new BusinessException("Fiyat en fazla iki ondalık basamak içermeli.");
        if (
            string.IsNullOrWhiteSpace(input.ImageUrl)
            || !input.ImageUrl.StartsWith("/images/")
                && !input.ImageUrl.StartsWith("/uploads/")
                && !(
                    Uri.TryCreate(input.ImageUrl, UriKind.Absolute, out var uri)
                    && uri.Scheme == "https"
                )
        )
            throw new BusinessException("Görsel adresi bir yükleme yolu veya HTTPS adresi olmalı.");
        using var db = database.Open();
        if (
            await db.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM Categories WHERE Id=@CategoryId",
                input
            ) == 0
        )
            throw new BusinessException("Geçerli bir kategori seçin.");
    }
}

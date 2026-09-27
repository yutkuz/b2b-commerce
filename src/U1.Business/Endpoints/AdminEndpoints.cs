using Dapper;
using Microsoft.AspNetCore.Identity;
using U1.Business.Data;
using U1.Business.Domain;
using U1.Business.Services;

namespace U1.Business.Endpoints;

public static class AdminEndpoints
{
    private const string ProductValues = "Code=@Code,Name=@Name,Description=@Description,Brand=@Brand,ManufacturerCode=@ManufacturerCode,SpecialCode1=@SpecialCode1,SpecialCode2=@SpecialCode2,ImageUrl=@ImageUrl,Stock=@Stock,CriticalStock=@CriticalStock,Price=@Price,CategoryId=@CategoryId";
    public static void MapAdmin(this WebApplication app)
    {
        var api = app.MapGroup("/api/admin").RequireAuthorization("Admin");
        api.MapGet("/dashboard",async(Database database)=>
        {
            using var db=database.Open();
            return new { products=await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Products"),users=await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Users WHERE Role='Dealer'"),pending=await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Orders WHERE Status=N'Bekliyor'"),revenue=await db.ExecuteScalarAsync<decimal>("SELECT COALESCE(SUM(Total),0) FROM Orders WHERE Status=N'Onaylandı'"),lowStock=await db.QueryAsync("SELECT TOP(8) Id,Code,Name,Stock,CriticalStock FROM Products WHERE Stock<=CriticalStock ORDER BY Stock,Id"),recentOrders=await db.QueryAsync("SELECT TOP(8) o.Id,o.Number,o.CreatedAt,o.Total,o.Status,u.Company,u.FirstName,u.LastName FROM Orders o JOIN Users u ON u.Id=o.UserId ORDER BY o.Id DESC") };
        });
        api.MapPost("/products",async(ProductInput input,Database database)=>
        {
            await ValidateProduct(input,database); using var db=database.Open();
            var id=await db.ExecuteScalarAsync<int>("INSERT INTO Products(Code,Name,Description,Brand,ManufacturerCode,SpecialCode1,SpecialCode2,ImageUrl,Stock,CriticalStock,Price,CategoryId) OUTPUT INSERTED.Id VALUES(@Code,@Name,@Description,@Brand,@ManufacturerCode,@SpecialCode1,@SpecialCode2,@ImageUrl,@Stock,@CriticalStock,@Price,@CategoryId)",input);
            return Results.Ok(new {id});
        });
        api.MapPut("/products/{id:int}",async(int id,ProductUpdateInput input,Database database)=>
        {
            if (input is null) throw new BusinessException("Ürün bilgileri gerekli.");
            if (string.IsNullOrWhiteSpace(input.Version)) throw new BusinessException("Ürün sürümü gerekli.");
            byte[] version;
            try { version=Convert.FromBase64String(input.Version); } catch (FormatException) { throw new BusinessException("Ürün sürümü geçersiz."); }
            if(version.Length!=8) throw new BusinessException("Ürün sürümü geçersiz.");
            await ValidateProduct(input,database); using var db=database.Open(); var args=new DynamicParameters(input);args.Add("Id",id);args.Add("ExpectedVersion",version);
            if(await db.ExecuteAsync("UPDATE Products SET "+ProductValues+" WHERE Id=@Id AND RowVersion=@ExpectedVersion",args)==0)
            {
                if(await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Products WHERE Id=@id",new{id})==0) throw new BusinessException("Ürün bulunamadı.",404);
                throw new BusinessException("Ürün başka bir işlemde değişti. Girdilerinizi koruyarak güncel ürünü yeniden yükleyin.",409,"PRODUCT_CHANGED");
            }
            return Results.Ok();
        });
        api.MapPost("/images",async(HttpRequest request,IWebHostEnvironment env)=>
        {
            if (!request.HasFormContentType || request.ContentLength>5*1024*1024) throw new BusinessException("En fazla 4 MB boyutunda PNG, JPEG veya WebP seçin.");
            var form=await request.ReadFormAsync(); var file=form.Files.GetFile("file");
            if(file is null || file.Length==0 || file.Length>4*1024*1024) throw new BusinessException("Geçerli bir görsel seçin (en fazla 4 MB).");
            using var ms=new MemoryStream(); await file.CopyToAsync(ms); var bytes=ms.ToArray();
            string ext;
            if(bytes.Length>8 && bytes[..8].SequenceEqual(new byte[]{137,80,78,71,13,10,26,10})) ext=".png";
            else if(bytes.Length>3 && bytes[0]==255 && bytes[1]==216 && bytes[2]==255) ext=".jpg";
            else if(bytes.Length>12 && System.Text.Encoding.ASCII.GetString(bytes,0,4)=="RIFF" && System.Text.Encoding.ASCII.GetString(bytes,8,4)=="WEBP") ext=".webp";
            else throw new BusinessException("Yalnızca PNG, JPEG ve WebP dosyaları kabul edilir.");
            var name=Guid.NewGuid().ToString("N")+ext; var folder=Path.Combine(env.WebRootPath,"uploads"); Directory.CreateDirectory(folder);
            await File.WriteAllBytesAsync(Path.Combine(folder,name),bytes); return Results.Ok(new {url="/uploads/"+name});
        });
        api.MapGet("/users",async(string? q,int? page,Database database)=>
        {
            using var db=database.Open();var args=new {search="%"+(q??"")+"%",offset=(Math.Clamp(page??1,1,100000)-1)*20};
            const string where=" FROM Users WHERE FirstName LIKE @search OR LastName LIKE @search OR Email LIKE @search OR Company LIKE @search";
            return new {items=await db.QueryAsync("SELECT Id,FirstName,LastName,Email,Phone,Company,Role,IsActive,AuthVersion AS Version"+where+" ORDER BY Id DESC OFFSET @offset ROWS FETCH NEXT 20 ROWS ONLY",args),total=await db.ExecuteScalarAsync<int>("SELECT COUNT(*)"+where,args)};
        });
        api.MapPut("/users/{id:int}",async(int id,UserInput input,Database database,IPasswordHasher<User> hasher,HttpContext c)=>
        {
            if(input is null) throw new BusinessException("Kullanıcı bilgileri gerekli.");
            input.Company ??= "";
            Rules.Validate(input);using var db=database.Open();var u=await db.QuerySingleOrDefaultAsync<User>("SELECT * FROM Users WHERE Id=@id",new{id}) ?? throw new BusinessException("Kullanıcı bulunamadı.",404);
            if(input.Version<=0) throw new BusinessException("Kullanıcı sürümü gerekli.");
            if(u.Role=="Admin" && !input.IsActive) throw new BusinessException("Yönetici hesabı bu ekrandan pasifleştirilemez.");
            string? passwordHash=null;
            if(!string.IsNullOrEmpty(input.NewPassword))
            {
                if(input.NewPassword.Length<10 || input.NewPassword.Length>128) throw new BusinessException("Yeni şifre 10–128 karakter olmalı.");
                passwordHash=hasher.HashPassword(u,input.NewPassword);
            }
            var args=new {id,input.FirstName,input.LastName,Email=input.Email.Trim().ToLowerInvariant(),input.Phone,input.Company,input.IsActive,input.Version,passwordHash};
            var sql=passwordHash is null
                ? "UPDATE Users SET FirstName=@FirstName,LastName=@LastName,Email=@Email,Phone=@Phone,Company=@Company,IsActive=@IsActive,AuthVersion=AuthVersion+1 WHERE Id=@id AND AuthVersion=@Version"
                : "UPDATE Users SET FirstName=@FirstName,LastName=@LastName,Email=@Email,Phone=@Phone,Company=@Company,IsActive=@IsActive,PasswordHash=@passwordHash,AuthVersion=AuthVersion+1 WHERE Id=@id AND AuthVersion=@Version";
            if(await db.ExecuteAsync(sql,args)==0)
            {
                if(await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Users WHERE Id=@id",new{id})==0) throw new BusinessException("Kullanıcı bulunamadı.",404);
                throw new BusinessException("Kullanıcı başka bir işlemde değişti. Güncel bilgileri yeniden yükleyip tekrar deneyin.",409,"USER_CHANGED");
            }
            return Results.Ok();
        });
        api.MapGet("/orders",async(string? q,string? status,int? page,Database database)=>
        {
            using var db=database.Open();var args=new {q="%"+(q??"")+"%",status=status??"",offset=(Math.Clamp(page??1,1,100000)-1)*20};
            const string where=" FROM Orders o JOIN Users u ON u.Id=o.UserId WHERE (o.Number LIKE @q OR u.Company LIKE @q OR u.FirstName LIKE @q OR u.LastName LIKE @q) AND (@status='' OR o.Status=@status)";
            return new {items=await db.QueryAsync("SELECT o.Id,o.Number,o.CreatedAt,o.Status,o.Total,u.FirstName,u.LastName,u.Company"+where+" ORDER BY o.Id DESC OFFSET @offset ROWS FETCH NEXT 20 ROWS ONLY",args),total=await db.ExecuteScalarAsync<int>("SELECT COUNT(*)"+where,args)};
        });
        api.MapPut("/orders/{id:int}/status",async(int id,StatusInput input,OrderService orders)=> {if(input is null)throw new BusinessException("Sipariş durumu gerekli.");await orders.ChangeStatus(id,input.Status);return Results.Ok();});
        api.MapGet("/grid",async(Database database)=> {using var db=database.Open();return await db.QueryAsync<GridColumn>("SELECT * FROM GridColumns ORDER BY Position,Id");});
        api.MapPut("/grid",async(GridColumn[] columns,Database database)=>
        {
            if(columns is null || columns.Length is <1 or >20 || columns.Any(c=>c is null) || columns.Select(c=>c.Id).Distinct().Count()!=columns.Length) throw new BusinessException("Kolon yapılandırması geçersiz.");
            using var db=database.Open();await db.OpenAsync();using var tx=db.BeginTransaction();
            var saved=(await db.QueryAsync<GridColumn>("SELECT * FROM GridColumns",transaction:tx)).ToList();
            if(saved.Count!=columns.Length) throw new BusinessException("Tüm kolonları kaydedin.");
            foreach(var col in columns)
            {
                Rules.Validate(col);
                var original=saved.SingleOrDefault(x=>x.Id==col.Id);
                if(original is null || original.Field!=col.Field || col.Align is not("left" or "center" or "right")) throw new BusinessException("Kolon bilgileri geçersiz.");
                var types=col.Field switch {"imageUrl"=>new[]{"image","text"},"stock"=>new[]{"stock","text"},"price"=>new[]{"money","text"},"quantity"=>new[]{"purchase"},"name"=>new[]{"product","text"},_=>new[]{"text"}};
                if(!types.Contains(col.RenderType)) throw new BusinessException("Bu alan için render tipi geçersiz.");
                if(col.Field is "name" or "price" or "quantity" && !(col.Desktop && col.Tablet && col.Mobile)) throw new BusinessException("Ürün adı, fiyat ve satın alma kolonları tüm cihazlarda açık kalmalıdır.");
                await db.ExecuteAsync("UPDATE GridColumns SET Label=@Label,RenderType=@RenderType,Position=@Position,Width=@Width,Align=@Align,Desktop=@Desktop,Tablet=@Tablet,Mobile=@Mobile WHERE Id=@Id",col,tx);
            }
            tx.Commit();return Results.Ok();
        });
        api.MapGet("/banners",async(Database database)=> {using var db=database.Open();return await db.QueryAsync("SELECT * FROM Banners ORDER BY Position,Id");});
        api.MapPost("/banners",async(BannerInput input,Database database)=>
        {
            if(input is null) throw new BusinessException("Duyuru bilgileri gerekli.");
            input.SearchTerm ??= "";
            Rules.Validate(input);using var db=database.Open();return new {id=await db.ExecuteScalarAsync<int>("INSERT INTO Banners(Title,Subtitle,ButtonText,SearchTerm,IsActive,Position) OUTPUT INSERTED.Id VALUES(@Title,@Subtitle,@ButtonText,@SearchTerm,@IsActive,@Position)",input)};
        });
        api.MapPut("/banners/{id:int}",async(int id,BannerInput input,Database database)=>
        {
            if(input is null) throw new BusinessException("Duyuru bilgileri gerekli.");
            input.SearchTerm ??= "";
            Rules.Validate(input);using var db=database.Open();var args=new DynamicParameters(input);args.Add("Id",id);
            if(await db.ExecuteAsync("UPDATE Banners SET Title=@Title,Subtitle=@Subtitle,ButtonText=@ButtonText,SearchTerm=@SearchTerm,IsActive=@IsActive,Position=@Position WHERE Id=@Id",args)==0) throw new BusinessException("Duyuru bulunamadı.",404);
            return Results.Ok();
        });
    }
    private static async Task ValidateProduct(ProductInput input,Database database)
    {
        if(input is null) throw new BusinessException("Ürün bilgileri gerekli.");
        input.SpecialCode1 ??= ""; input.SpecialCode2 ??= "";
        Rules.Validate(input);
        if (decimal.Round(input.Price, 2) != input.Price)
            throw new BusinessException("Fiyat en fazla iki ondalık basamak içermeli.");
        if(string.IsNullOrWhiteSpace(input.ImageUrl) || !input.ImageUrl.StartsWith("/images/") && !input.ImageUrl.StartsWith("/uploads/") && !(Uri.TryCreate(input.ImageUrl,UriKind.Absolute,out var uri) && uri.Scheme=="https")) throw new BusinessException("Görsel adresi bir yükleme yolu veya HTTPS adresi olmalı.");
        using var db=database.Open();if(await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Categories WHERE Id=@CategoryId",input)==0) throw new BusinessException("Geçerli bir kategori seçin.");
    }
}

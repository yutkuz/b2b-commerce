using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using U1.Business.Data;
using U1.Business.Domain;

namespace U1.Business.Services;

public sealed class OrderService(Database database)
{
    private const int MaxDeadlockAttempts = 3;

    // The cart row is a per-user mutex. Product locks are taken in Id order.
    public async Task<object> Checkout(int userId, CheckoutInput input)
    {
        if (input is null || input.RequestId == Guid.Empty || input.Note?.Length > 1000 || input.Lines is null || input.Lines.Length == 0 ||
            input.Lines.Any(x => x is null || x.ProductId <= 0 || x.Quantity <= 0 || x.UnitPrice <= 0) ||
            input.Lines.Select(x => x.ProductId).Distinct().Count() != input.Lines.Length)
            throw new BusinessException("Sipariş onayındaki ürün bilgileri geçersiz.");

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await CheckoutOnce(userId, input);
            }
            catch (SqlException ex) when (ex.Number == 1205 && attempt < MaxDeadlockAttempts)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(40 * attempt));
            }
        }
    }

    private async Task<object> CheckoutOnce(int userId, CheckoutInput input)
    {
        using var db = database.Open(); await db.OpenAsync(); using var tx = db.BeginTransaction(IsolationLevel.Serializable);
        var cart = await db.ExecuteScalarAsync<int>("SELECT Id FROM Carts WITH(UPDLOCK,HOLDLOCK) WHERE UserId=@userId", new { userId }, tx);
        var requestLock = await db.ExecuteScalarAsync<int>("EXEC sp_getapplock @Resource=@resource, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=30000",new { resource="U1Business.Order."+input.RequestId.ToString("N") },tx);
        if(requestLock<0) throw new BusinessException("Sipariş yeniden denenemedi. Biraz sonra tekrar deneyin.",409,"REQUEST_BUSY");
        var existing = await db.QuerySingleOrDefaultAsync("SELECT Id,Number,Total FROM Orders WITH(READCOMMITTEDLOCK) WHERE RequestId=@RequestId AND UserId=@userId", new { input.RequestId, userId }, tx);
        if (existing is not null) { tx.Commit(); return existing; }
        if (await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Orders WITH(READCOMMITTEDLOCK) WHERE RequestId=@RequestId", new { input.RequestId }, tx) > 0)
            throw new BusinessException("Bu sipariş anahtarı kullanılamaz.",409,"REQUEST_ID_CONFLICT");
        var lines = (await db.QueryAsync<CartLine>("SELECT ProductId,Quantity FROM CartItems WHERE CartId=@cart ORDER BY ProductId", new { cart }, tx)).ToList();
        if (lines.Count == 0) throw new BusinessException("Sepetiniz onaydan sonra değişti. Güncel tutarı yeniden onaylayın.",409,"CART_CHANGED");
        foreach (var line in lines)
        {
            var p = await db.QuerySingleAsync<Snapshot>("SELECT Code,Name,Price,Stock FROM Products WITH(UPDLOCK,HOLDLOCK) WHERE Id=@ProductId", line, tx);
            line.Product = p;
        }
        if(lines.Count != input.Lines.Length || lines.Any(line =>
            !input.Lines.Any(approved => approved.ProductId == line.ProductId && approved.Quantity == line.Quantity && approved.UnitPrice == line.Product.Price)))
            throw new BusinessException("Sepetiniz onaydan sonra değişti. Güncel tutarı yeniden onaylayın.",409,"CART_CHANGED");
        foreach (var line in lines)
            if (line.Quantity > line.Product.Stock) throw new BusinessException($"{line.Product.Name} için yeterli stok bulunmamaktadır. Mevcut stok: {line.Product.Stock}.",409,"INSUFFICIENT_STOCK");
        var total = lines.Sum(l => l.Quantity*l.Product.Price);
        var number = "U1-"+DateTime.UtcNow.ToString("yyyyMMdd")+"-"+Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
        var id = await db.ExecuteScalarAsync<int>("INSERT INTO Orders(Number,UserId,Total,RequestId,Note) OUTPUT INSERTED.Id VALUES(@number,@userId,@total,@RequestId,@note)",new {number,userId,total,input.RequestId,note=input.Note?.Trim() ?? ""},tx);
        foreach (var line in lines)
            await db.ExecuteAsync("INSERT INTO OrderItems(OrderId,ProductId,ProductCode,ProductName,Quantity,UnitPrice,Total) VALUES(@id,@ProductId,@Code,@Name,@Quantity,@Price,@total); UPDATE Products SET Stock=Stock-@Quantity WHERE Id=@ProductId;", new {id,line.ProductId,line.Product.Code,line.Product.Name,line.Quantity,line.Product.Price,total=line.Quantity*line.Product.Price},tx);
        await db.ExecuteAsync("DELETE FROM CartItems WHERE CartId=@cart",new {cart},tx); tx.Commit();
        return new { Id=id, Number=number, Total=total };
    }

    public async Task ChangeStatus(int id, string status)
    {
        if (status is not ("Onaylandı" or "Reddedildi")) throw new BusinessException("Geçersiz sipariş durumu.");
        using var db = database.Open(); await db.OpenAsync(); using var tx = db.BeginTransaction(IsolationLevel.Serializable);
        var old = await db.QuerySingleOrDefaultAsync<string>("SELECT Status FROM Orders WITH(UPDLOCK,HOLDLOCK) WHERE Id=@id",new {id},tx) ?? throw new BusinessException("Sipariş bulunamadı.",404);
        if (old == status) { tx.Commit(); return; }
        if (old == "Reddedildi") throw new BusinessException("Reddedilen sipariş yeniden açılamaz. Yeni sipariş oluşturun.",409);
        if (status == "Reddedildi")
        {
            var lines = await db.QueryAsync<CartLine>("SELECT ProductId,Quantity FROM OrderItems WHERE OrderId=@id ORDER BY ProductId",new {id},tx);
            foreach (var line in lines)
                if (await db.ExecuteAsync("UPDATE Products SET Stock=Stock+@Quantity WHERE Id=@ProductId AND Stock<=2147483647-@Quantity",line,tx) != 1)
                    throw new BusinessException("Stok iadesi tam sayı sınırını aşıyor. Fiziksel stoğu kontrol edin.",409,"STOCK_LIMIT");
        }
        await db.ExecuteAsync("UPDATE Orders SET Status=@status WHERE Id=@id",new {id,status},tx); tx.Commit();
    }
    private sealed class CartLine { public int ProductId {get;set;} public int Quantity {get;set;} public Snapshot Product {get;set;} = new(); }
    private sealed class Snapshot { public string Code {get;set;}=""; public string Name {get;set;}=""; public decimal Price {get;set;} public int Stock {get;set;} }
}

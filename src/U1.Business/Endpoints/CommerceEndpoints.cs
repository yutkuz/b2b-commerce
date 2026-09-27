using System.Data;
using Dapper;
using U1.Business.Data;
using U1.Business.Domain;
using U1.Business.Services;

namespace U1.Business.Endpoints;

public static class CommerceEndpoints
{
    public static void MapCommerce(this WebApplication app)
    {
        var api = app.MapGroup("/api").RequireAuthorization();
        api.MapGet("/cart", async (Database database, HttpContext c) =>
        {
            using var db = database.Open();
            var items = (await db.QueryAsync("SELECT p.Id,p.Code,p.Name,p.ImageUrl,p.Price,p.Stock,ci.Quantity,p.Price*ci.Quantity AS Total FROM Carts ca JOIN CartItems ci ON ca.Id=ci.CartId JOIN Products p ON p.Id=ci.ProductId WHERE ca.UserId=@id ORDER BY p.Id",new {id=c.UserId()})).ToList();
            return new {items,total=items.Sum(i=>(decimal)i.Total),count=items.Sum(i=>(int)i.Quantity)};
        });
        api.MapPost("/cart", async (CartInput? input, Database database, HttpContext c) => await SetCart(input,true,database,c));
        api.MapPut("/cart", async (CartInput? input, Database database, HttpContext c) => await SetCart(input,false,database,c));
        api.MapDelete("/cart/{productId:int}", async (int productId, Database database, HttpContext c) => await SetCart(new CartInput(productId,0),false,database,c));
        api.MapPost("/orders", async (CheckoutInput input, OrderService orders, HttpContext c) => await orders.Checkout(c.UserId(),input));
        api.MapGet("/orders", async (Database database,HttpContext c,int? page) =>
        {
            using var db=database.Open(); var id=c.UserId(); var offset=(Math.Clamp(page??1,1,100000)-1)*20;
            return new {items=await db.QueryAsync("SELECT Id,Number,CreatedAt,Status,Total FROM Orders WHERE UserId=@id ORDER BY Id DESC OFFSET @offset ROWS FETCH NEXT 20 ROWS ONLY",new {id,offset}),total=await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Orders WHERE UserId=@id",new {id})};
        });
        api.MapGet("/orders/{id:int}",async (int id,Database database,HttpContext c)=>
        {
            using var db=database.Open();
            var order=await db.QuerySingleOrDefaultAsync("SELECT o.*,u.FirstName,u.LastName,u.Company FROM Orders o JOIN Users u ON u.Id=o.UserId WHERE o.Id=@id AND (o.UserId=@userId OR @admin=1)",new {id,userId=c.UserId(),admin=c.User.IsInRole("Admin")}) ?? throw new BusinessException("Sipariş bulunamadı.",404);
            return new {order,items=await db.QueryAsync("SELECT ProductId,ProductCode,ProductName,Quantity,UnitPrice,Total FROM OrderItems WHERE OrderId=@id ORDER BY Id",new {id})};
        });
    }
    private static async Task<IResult> SetCart(CartInput? input,bool add,Database database,HttpContext c)
    {
        if (input is null || input.ProductId <= 0 || input.Quantity<0 || input.Quantity>1000000 || (add && input.Quantity==0))
            throw new BusinessException("Geçerli bir ürün ve adet girin.");
        using var db=database.Open(); await db.OpenAsync(); using var tx=db.BeginTransaction(IsolationLevel.Serializable);
        var cart=await db.ExecuteScalarAsync<int>("SELECT Id FROM Carts WITH(UPDLOCK,HOLDLOCK) WHERE UserId=@id",new {id=c.UserId()},tx);
        var stock=await db.QuerySingleOrDefaultAsync<int?>("SELECT Stock FROM Products WHERE Id=@ProductId",input,tx) ?? throw new BusinessException("Ürün bulunamadı.",404);
        var current=await db.ExecuteScalarAsync<int>("SELECT Quantity FROM CartItems WHERE CartId=@cart AND ProductId=@ProductId",new {cart,input.ProductId},tx);
        var quantity=add? (long)current+input.Quantity:input.Quantity;
        if (quantity>1000000) throw new BusinessException("Sepette bir ürün en fazla 1.000.000 adet olabilir.",400,"QUANTITY_LIMIT");
        if (quantity>stock) throw new BusinessException($"Yeterli stok yok. Mevcut stok: {stock}. Sepetinizde: {current}.",409);
        if(quantity==0) await db.ExecuteAsync("DELETE FROM CartItems WHERE CartId=@cart AND ProductId=@ProductId",new {cart,input.ProductId},tx);
        else if(current>0) await db.ExecuteAsync("UPDATE CartItems SET Quantity=@quantity WHERE CartId=@cart AND ProductId=@ProductId",new {cart,input.ProductId,quantity},tx);
        else await db.ExecuteAsync("INSERT INTO CartItems(CartId,ProductId,Quantity) VALUES(@cart,@ProductId,@quantity)",new {cart,input.ProductId,quantity},tx);
        tx.Commit(); return Results.Ok();
    }
}

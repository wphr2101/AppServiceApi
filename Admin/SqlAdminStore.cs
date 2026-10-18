using System.Data;
using Microsoft.Data.SqlClient;
using CustomerOperations.Api.Orders;

namespace CustomerOperations.Api.Admin;

public sealed class SqlAdminStore(IConfiguration configuration, ILogger<SqlAdminStore> logger) : IAdminStore
{
    private async Task<T> Execute<T>(int actor, Func<SqlConnection, SqlTransaction, Task<T>> action, CancellationToken ct)
    {
        try
        {
            var value = configuration.GetConnectionString("DefaultConnection");
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException();
            var settings = new SqlConnectionStringBuilder(value) { ConnectTimeout = 15 };
            await using var db = new SqlConnection(settings.ConnectionString);
            await db.OpenAsync(ct);
            using var tx = db.BeginTransaction();
            // Check the current database role on every operation, including after a cookie was issued.
            await using var role = new SqlCommand("SELECT UserType FROM logistics.Users WITH (HOLDLOCK) WHERE UserId=@id", db, tx);
            role.Parameters.Add("@id", SqlDbType.Int).Value = actor;
            if ((string?)await role.ExecuteScalarAsync(ct) != "Admin")
                throw new OrderRejectedException("admin_required", "Administrator access is required.", 403);
            var result = await action(db, tx);
            await tx.CommitAsync(ct);
            return result;
        }
        catch (SqlException e)
        {
            logger.LogWarning("Admin storage error number {Number}; details suppressed.", e.Number);
            throw new OrderRejectedException("admin_unavailable", "Administration service temporarily unavailable. Retry the same action.", 503);
        }
        catch (ArgumentException)
        { throw new OrderRejectedException("admin_unavailable", "Administration service configuration is unavailable.", 503); }
    }

    public Task<IReadOnlyList<InventoryItem>> InventoryAsync(int actor, CancellationToken ct) => Execute<IReadOnlyList<InventoryItem>>(actor, async (db, tx) =>
    {
        await using var cmd = new SqlCommand("SELECT ProductId,Name,StockQuantity FROM logistics.Products ORDER BY ProductId", db, tx);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var items = new List<InventoryItem>();
        while (await r.ReadAsync(ct)) items.Add(new(r.GetInt32(0), r.GetString(1), r.GetInt32(2)));
        return items;
    }, ct);

    public Task<IReadOnlyList<StockMovement>> MovementsAsync(int actor, CancellationToken ct) => Execute<IReadOnlyList<StockMovement>>(actor, async (db, tx) =>
    {
        await using var cmd = new SqlCommand("""
            SELECT TOP (50) m.MovementId,m.ProductId,p.Name,m.QuantityChange,m.StockAfter,m.Kind,m.Reason,u.UserName,m.CreatedAt,m.OrderId
            FROM logistics.StockMovements m JOIN logistics.Products p ON p.ProductId=m.ProductId
            JOIN logistics.Users u ON u.UserId=m.ActorUserId ORDER BY m.MovementId DESC
            """, db, tx);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var items = new List<StockMovement>();
        while (await r.ReadAsync(ct)) items.Add(new(r.GetInt32(0),r.GetInt32(1),r.GetString(2),r.GetInt32(3),r.GetInt32(4),r.GetString(5),r.GetString(6),r.GetString(7),
            DateTime.SpecifyKind(r.GetDateTime(8),DateTimeKind.Utc),r.IsDBNull(9)?null:r.GetInt32(9)));
        return items;
    }, ct);

    public Task<InventoryItem> RestockAsync(int actor, int productId, RestockRequest request, CancellationToken ct)
    {
        request = AdminRules.Restock(request);
        return Execute(actor, async (db, tx) =>
        {
            await using var prior = new SqlCommand("SELECT ProductId,QuantityChange,Reason,ActorUserId FROM logistics.StockMovements WITH(UPDLOCK,HOLDLOCK) WHERE RequestId=@request",db,tx);
            prior.Parameters.Add("@request",SqlDbType.UniqueIdentifier).Value=request.RequestId;
            bool duplicate;
            await using (var r=await prior.ExecuteReaderAsync(ct))
            {
                duplicate=await r.ReadAsync(ct);
                if(duplicate && (r.GetInt32(0)!=productId || r.GetInt32(1)!=request.Quantity || r.GetString(2)!=request.Reason || r.GetInt32(3)!=actor))
                    throw new OrderRejectedException("request_conflict","This restock ID has already been used with different details.",409);
            }
            await using var product=new SqlCommand("SELECT Name,StockQuantity FROM logistics.Products WITH(UPDLOCK,HOLDLOCK) WHERE ProductId=@id",db,tx);
            product.Parameters.Add("@id",SqlDbType.Int).Value=productId;
            string name; int stock;
            await using(var r=await product.ExecuteReaderAsync(ct))
            {
                if(!await r.ReadAsync(ct)) throw new OrderRejectedException("not_found","Product not found.",404);
                name=r.GetString(0); stock=r.GetInt32(1);
            }
            if(duplicate) return new InventoryItem(productId,name,stock);
            if(stock > 1000000-request.Quantity) throw new OrderRejectedException("stock_limit","Stock cannot exceed 1,000,000 units.",409);
            var after=stock+request.Quantity;
            await using var update=new SqlCommand("""
                UPDATE logistics.Products SET StockQuantity=@after WHERE ProductId=@id;
                INSERT INTO logistics.StockMovements(ProductId,QuantityChange,StockAfter,Kind,Reason,ActorUserId,RequestId)
                VALUES(@id,@qty,@after,'Restock',@reason,@actor,@request);
                """,db,tx);
            update.Parameters.Add("@id",SqlDbType.Int).Value=productId;
            update.Parameters.Add("@after",SqlDbType.Int).Value=after;
            update.Parameters.Add("@qty",SqlDbType.Int).Value=request.Quantity;
            update.Parameters.Add("@reason",SqlDbType.NVarChar,250).Value=request.Reason;
            update.Parameters.Add("@actor",SqlDbType.Int).Value=actor;
            update.Parameters.Add("@request",SqlDbType.UniqueIdentifier).Value=request.RequestId;
            await update.ExecuteNonQueryAsync(ct);
            return new InventoryItem(productId,name,after);
        },ct);
    }

    public Task<AdminOrderPage> OrdersAsync(int actor, int? beforeId, CancellationToken ct) => Execute(actor,async(db,tx)=>
    {
        await using var cmd=new SqlCommand("""
            SELECT TOP(21) o.OrderId,o.OrderDate,o.Status,o.PaymentStatus,c.Name,COALESCE(SUM(i.Quantity*i.UnitPrice),0),o.StockDeducted
            FROM logistics.Orders o JOIN logistics.Customers c ON c.CustomerId=o.CustomerId
            LEFT JOIN logistics.OrderItems i ON i.OrderId=o.OrderId
            WHERE @before IS NULL OR o.OrderId<@before
            GROUP BY o.OrderId,o.OrderDate,o.Status,o.PaymentStatus,c.Name,o.StockDeducted ORDER BY o.OrderId DESC
            """,db,tx);
        cmd.Parameters.Add("@before",SqlDbType.Int).Value=(object?)beforeId??DBNull.Value;
        await using var r=await cmd.ExecuteReaderAsync(ct); var items=new List<AdminOrder>();
        while(await r.ReadAsync(ct)) items.Add(new(r.GetInt32(0),DateTime.SpecifyKind(r.GetDateTime(1),DateTimeKind.Utc),r.GetString(2),r.GetString(3),r.GetString(4),r.GetDecimal(5),r.GetBoolean(6)));
        return new AdminOrderPage(items.Take(20).ToArray(),items.Count>20);
    },ct);

    public Task<AdminOrderDetail?> DetailAsync(int actor,int orderId,CancellationToken ct)=>Execute<AdminOrderDetail?>(actor,async(db,tx)=>
    {
        var receipt=await SqlOrderStore.Read(db,tx,actor,orderId,ct,admin:true);
        if(receipt is null) return null;
        await using var flag=new SqlCommand("SELECT StockDeducted FROM logistics.Orders WHERE OrderId=@id",db,tx);
        flag.Parameters.Add("@id",SqlDbType.Int).Value=orderId;
        var deducted=(bool)(await flag.ExecuteScalarAsync(ct))!;
        await using var cmd=new SqlCommand("""
            SELECT e.EventType,u.UserName,e.CreatedAt FROM logistics.OrderEvents e
            JOIN logistics.Users u ON u.UserId=e.ActorUserId WHERE e.OrderId=@id ORDER BY e.EventId
            """,db,tx);
        cmd.Parameters.Add("@id",SqlDbType.Int).Value=orderId;
        var events=new List<OrderEvent>(); await using var r=await cmd.ExecuteReaderAsync(ct);
        while(await r.ReadAsync(ct)) events.Add(new(r.GetString(0),r.GetString(1),DateTime.SpecifyKind(r.GetDateTime(2),DateTimeKind.Utc)));
        return new(receipt,events,deducted);
    },ct);

    public async Task ChangeOrderAsync(int actor,int orderId,string action,CancellationToken ct)=>await Execute(actor,async(db,tx)=>
    {
        await using var cmd=new SqlCommand("SELECT Status,PaymentStatus,StockDeducted FROM logistics.Orders WITH(UPDLOCK,HOLDLOCK) WHERE OrderId=@id",db,tx);
        cmd.Parameters.Add("@id",SqlDbType.Int).Value=orderId;
        string status,payment; bool deducted;
        await using(var r=await cmd.ExecuteReaderAsync(ct))
        {
            if(!await r.ReadAsync(ct)) throw new OrderRejectedException("not_found","Order not found.",404);
            status=r.GetString(0); payment=r.GetString(1); deducted=r.GetBoolean(2);
        }
        if(!AdminRules.NeedsChange(action,status,payment)) return true;
        if(action=="shipped" && !deducted)
        {
            var lines=new List<(int ProductId,int Quantity)>();
            await using var items=new SqlCommand("SELECT ProductId,Quantity FROM logistics.OrderItems WHERE OrderId=@id ORDER BY ProductId",db,tx);
            items.Parameters.Add("@id",SqlDbType.Int).Value=orderId;
            await using(var r=await items.ExecuteReaderAsync(ct))
                while(await r.ReadAsync(ct)) lines.Add((r.GetInt32(0),r.GetInt32(1)));
            if(lines.Count==0) throw new OrderRejectedException("empty_order","An empty order cannot be shipped.",409);
            await InventorySql.Deduct(db,tx,orderId,actor,lines,ct);
        }
        var sql=action=="paid" ? "UPDATE logistics.Orders SET PaymentStatus='Paid',PaidAt=SYSUTCDATETIME() WHERE OrderId=@id;" :
            "UPDATE logistics.Orders SET Status='Shipped',ShippedAt=SYSUTCDATETIME() WHERE OrderId=@id;";
        await using var update=new SqlCommand(sql+"INSERT INTO logistics.OrderEvents(OrderId,EventType,ActorUserId) VALUES(@id,@event,@actor);",db,tx);
        update.Parameters.Add("@id",SqlDbType.Int).Value=orderId;
        update.Parameters.Add("@event",SqlDbType.VarChar,20).Value=action=="paid"?"Paid":"Shipped";
        update.Parameters.Add("@actor",SqlDbType.Int).Value=actor;
        await update.ExecuteNonQueryAsync(ct);
        return true;
    },ct);
}

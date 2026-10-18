using System.Data;
using Microsoft.Data.SqlClient;
using CustomerOperations.Api.Orders;

namespace CustomerOperations.Api.Admin;

internal static class InventorySql
{
    // Runs inside the order transaction; every stock decrement and movement rolls back together.
    public static async Task Deduct(SqlConnection db, SqlTransaction tx, int orderId, int actor,
        IEnumerable<(int ProductId, int Quantity)> lines, CancellationToken ct)
    {
        foreach (var line in lines.OrderBy(l => l.ProductId))
        {
            await using var command = new SqlCommand("""
                UPDATE logistics.Products SET StockQuantity=StockQuantity-@qty
                OUTPUT INSERTED.StockQuantity WHERE ProductId=@product AND StockQuantity>=@qty;
                """, db, tx);
            command.Parameters.Add("@product", SqlDbType.Int).Value = line.ProductId;
            command.Parameters.Add("@qty", SqlDbType.Int).Value = line.Quantity;
            if (await command.ExecuteScalarAsync(ct) is not int after)
                throw new OrderRejectedException("insufficient_stock", $"Insufficient stock for product #{line.ProductId}. Reduce the quantity or restock first.", 409);
            await using var movement = new SqlCommand("""
                INSERT INTO logistics.StockMovements(ProductId,QuantityChange,StockAfter,Kind,Reason,ActorUserId,OrderId)
                VALUES(@product,@delta,@after,'Order',N'Order stock deduction',@actor,@order);
                """, db, tx);
            movement.Parameters.Add("@product", SqlDbType.Int).Value = line.ProductId;
            movement.Parameters.Add("@delta", SqlDbType.Int).Value = -line.Quantity;
            movement.Parameters.Add("@after", SqlDbType.Int).Value = after;
            movement.Parameters.Add("@actor", SqlDbType.Int).Value = actor;
            movement.Parameters.Add("@order", SqlDbType.Int).Value = orderId;
            await movement.ExecuteNonQueryAsync(ct);
        }
        await using var flag = new SqlCommand("UPDATE logistics.Orders SET StockDeducted=1 WHERE OrderId=@id", db, tx);
        flag.Parameters.Add("@id", SqlDbType.Int).Value = orderId;
        await flag.ExecuteNonQueryAsync(ct);
    }
}

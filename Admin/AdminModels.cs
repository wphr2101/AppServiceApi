using CustomerOperations.Api.Orders;

namespace CustomerOperations.Api.Admin;

public sealed record InventoryItem(int ProductId, string Name, int StockQuantity);
public sealed record RestockRequest(Guid RequestId, int Quantity, string Reason);
public sealed record StockMovement(int MovementId, int ProductId, string ProductName, int QuantityChange,
    int StockAfter, string Kind, string Reason, string Actor, DateTime CreatedAt, int? OrderId);
public sealed record AdminOrder(int OrderId, DateTime OrderDate, string Status, string PaymentStatus,
    string CustomerName, decimal Subtotal, bool StockDeducted);
public sealed record AdminOrderPage(IReadOnlyList<AdminOrder> Items, bool HasMore);
public sealed record OrderEvent(string EventType, string Actor, DateTime CreatedAt);
public sealed record AdminOrderDetail(OrderReceipt Order, IReadOnlyList<OrderEvent> Events, bool StockDeducted);
public interface IAdminStore
{
    Task<IReadOnlyList<InventoryItem>> InventoryAsync(int actor, CancellationToken ct);
    Task<IReadOnlyList<StockMovement>> MovementsAsync(int actor, CancellationToken ct);
    Task<InventoryItem> RestockAsync(int actor, int productId, RestockRequest request, CancellationToken ct);
    Task<AdminOrderPage> OrdersAsync(int actor, int? beforeId, CancellationToken ct);
    Task<AdminOrderDetail?> DetailAsync(int actor, int orderId, CancellationToken ct);
    Task ChangeOrderAsync(int actor, int orderId, string action, CancellationToken ct);
}

public static class AdminRules
{
    public static RestockRequest Restock(RestockRequest request)
    {
        if (request.RequestId == Guid.Empty || request.Quantity is < 1 or > 100000 ||
            string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 250)
            throw new OrderRejectedException("invalid_stock", "Enter a quantity from 1 to 100,000 and a reason (up to 250 characters).");
        return request with { Reason = request.Reason.Trim() };
    }
    public static bool NeedsChange(string action, string status, string payment)
    {
        if (status == "Cancelled") throw new OrderRejectedException("cancelled_order", "Cancelled orders cannot be changed.", 409);
        if (action == "paid")
        {
            if (payment == "Paid") return false;
            if (status != "Pending") throw new OrderRejectedException("invalid_state", "Only pending orders can be marked paid.", 409);
            return true;
        }
        if (action == "shipped")
        {
            if (payment != "Paid") throw new OrderRejectedException("payment_required", "Mark the simulated payment before shipping.", 409);
            if (status is "Shipped" or "Delivered") return false;
            if (status != "Pending") throw new OrderRejectedException("invalid_state", "Only pending orders can be shipped.", 409);
            return true;
        }
        throw new OrderRejectedException("invalid_action", "Unknown order action.");
    }
}

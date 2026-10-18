using CustomerOperations.Api.Orders;
using CustomerOperations.Api.Services;

namespace CustomerOperations.Testing;

// Test-only storage. This does not verify SQL locking or transaction behavior.
public sealed class MemoryOrderStore : IOrderStore
{
    private readonly Dictionary<(int, Guid), (byte[] Hash, OrderReceipt Receipt)> orders = new();
    public decimal Price { get; set; } = 3.49m;
    public int Count => orders.Count;
    public Task<OrderReceipt> PlaceAsync(int userId, PlaceOrderRequest request, CancellationToken ct)
    {
        request = OrderRules.Normalize(request);
        lock (orders)
        {
            var key = (userId, request.RequestId);
            var hash = OrderRules.Hash(request);
            if (orders.TryGetValue(key, out var prior))
            {
                if (!prior.Hash.SequenceEqual(hash)) throw new OrderRejectedException("request_conflict", "Different payload for same request.", 409);
                return Task.FromResult(prior.Receipt);
            }
            var lines = OrderRules.Price(request, new[] { new Product(1, "Oranges - 1 kg bag", Price, 1m) });
            var receipt = new OrderReceipt(orders.Count + 1, DateTime.UtcNow, "Pending", request.RecipientName, request.ShippingAddress, lines);
            orders.Add(key, (hash, receipt));
            return Task.FromResult(receipt);
        }
    }
    public Task<OrderReceipt?> GetAsync(int userId, int orderId, CancellationToken ct)
    {
        lock (orders) return Task.FromResult(orders.Where(x => x.Key.Item1 == userId).Select(x => x.Value.Receipt).FirstOrDefault(x => x.OrderId == orderId));
    }
    public Task<OrderPage> ListAsync(int userId, int? beforeId, CancellationToken ct)
    {
        lock (orders)
        {
            var items = orders.Where(x => x.Key.Item1 == userId).Select(x => x.Value.Receipt)
                .Where(x => beforeId is null || x.OrderId < beforeId).OrderByDescending(x => x.OrderId).Take(21)
                .Select(x => new OrderSummary(x.OrderId, x.OrderDate, x.Status, x.Lines.Sum(l => l.Quantity), x.Subtotal)).ToArray();
            return Task.FromResult(new OrderPage(items.Take(20).ToArray(), items.Length > 20));
        }
    }
}

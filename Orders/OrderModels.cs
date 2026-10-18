using System.Security.Cryptography;
using System.Text.Json;
using CustomerOperations.Api.Services;

namespace CustomerOperations.Api.Orders;

public sealed record OrderItemRequest(int ProductId, int Quantity, decimal ExpectedUnitPrice);
public sealed record PlaceOrderRequest(Guid RequestId, string RecipientName, string ShippingAddress, OrderItemRequest[] Items);
public sealed record OrderLine(int ProductId, string Name, int Quantity, decimal UnitPrice)
{
    public decimal LineTotal => UnitPrice * Quantity;
}
public sealed record OrderReceipt(int OrderId, DateTime OrderDate, string Status, string RecipientName,
    string ShippingAddress, IReadOnlyList<OrderLine> Lines)
{
    public decimal Subtotal => Lines.Sum(line => line.LineTotal);
    public string Currency => "USD";
    public string PaymentStatus { get; init; } = "Unpaid";
    public DateTime? PaidAt { get; init; }
    public DateTime? ShippedAt { get; init; }
}
public sealed record OrderSummary(int OrderId, DateTime OrderDate, string Status, int ItemCount, decimal Subtotal)
{
    public string Currency => "USD";
    public string PaymentStatus { get; init; } = "Unpaid";
}
public sealed record OrderPage(IReadOnlyList<OrderSummary> Items, bool HasMore);
public interface IOrderStore
{
    Task<OrderReceipt> PlaceAsync(int userId, PlaceOrderRequest request, CancellationToken ct);
    Task<OrderReceipt?> GetAsync(int userId, int orderId, CancellationToken ct);
    Task<OrderPage> ListAsync(int userId, int? beforeId, CancellationToken ct);
}
public sealed class OrderRejectedException(string code, string message, int status = 400,
    IReadOnlyList<OrderLine>? lines = null) : Exception(message)
{
    public string Code { get; } = code;
    public int Status { get; } = status;
    public IReadOnlyList<OrderLine>? Lines { get; } = lines;
}

public static class OrderRules
{
    public static PlaceOrderRequest Normalize(PlaceOrderRequest request)
    {
        if (request.RequestId == Guid.Empty || string.IsNullOrWhiteSpace(request.RecipientName) ||
            request.RecipientName.Trim().Length > 150 || string.IsNullOrWhiteSpace(request.ShippingAddress) ||
            request.ShippingAddress.Trim().Length > 500 || request.Items is null || request.Items.Length is < 1 or > 100 ||
            request.Items.Any(i => i is null || i.ProductId <= 0 || i.Quantity is < 1 or > 99 || i.ExpectedUnitPrice < 0) ||
            request.Items.Select(i => i.ProductId).Distinct().Count() != request.Items.Length)
            throw new OrderRejectedException("invalid_order", "Check the delivery address and cart quantities (1-99 per product, up to 100 products).");
        return request with { RecipientName = request.RecipientName.Trim(), ShippingAddress = request.ShippingAddress.Trim(), Items = request.Items.OrderBy(i => i.ProductId).ToArray() };
    }
    public static byte[] Hash(PlaceOrderRequest request) => SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(Normalize(request)));
    public static IReadOnlyList<OrderLine> Price(PlaceOrderRequest request, IReadOnlyList<Product> products)
    {
        var lines = new List<OrderLine>();
        foreach (var item in request.Items)
        {
            var product = products.SingleOrDefault(p => p.ProductId == item.ProductId)
                ?? throw new OrderRejectedException("product_unavailable", "A product is no longer available. Remove it from the cart and try again.", 409);
            lines.Add(new(product.ProductId, product.Name, item.Quantity, product.Price));
        }
        if (lines.Sum(l => l.LineTotal) > 1_000_000m)
            throw new OrderRejectedException("order_too_large", "This demo accepts order subtotals up to USD 1,000,000.");
        if (lines.Any(l => l.UnitPrice != request.Items.Single(i => i.ProductId == l.ProductId).ExpectedUnitPrice))
            throw new OrderRejectedException("price_changed", "Prices have changed. Review the latest prices before placing your order.", 409, lines);
        return lines;
    }
}

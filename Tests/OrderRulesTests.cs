using CustomerOperations.Api.Orders;
using CustomerOperations.Api.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace CustomerOperations.Api.Tests;

public class OrderRulesTests
{
    private static PlaceOrderRequest Valid() => new(Guid.NewGuid(), " Recipient ", " Example address ", new[] { new OrderItemRequest(1, 2, 3.49m) });
    [Fact]
    public void NormalizesDeliveryAndUsesServerPrices()
    {
        var request = OrderRules.Normalize(Valid());
        Assert.Equal("Recipient", request.RecipientName);
        Assert.Equal("Example address", request.ShippingAddress);
        var lines = OrderRules.Price(request, new[] { new Product(1, "Oranges", 3.49m, 1m) });
        Assert.Equal(6.98m, Assert.Single(lines).LineTotal);
    }
    [Theory]
    [InlineData(0)] [InlineData(-1)] [InlineData(100)]
    public void InvalidQuantitiesRejected(int quantity) => Assert.Throws<OrderRejectedException>(() => OrderRules.Normalize(Valid() with { Items = new[] { new OrderItemRequest(1, quantity, 3.49m) } }));
    [Fact]
    public void EmptyDuplicateAndMissingDataRejected()
    {
        foreach (var invalid in new[] { Valid() with { Items = Array.Empty<OrderItemRequest>() }, Valid() with { RecipientName = " " }, Valid() with { ShippingAddress = " " }, Valid() with { RequestId = Guid.Empty }, Valid() with { Items = new[] { new OrderItemRequest(1, 1, 1), new OrderItemRequest(1, 2, 1) } } })
            Assert.Throws<OrderRejectedException>(() => OrderRules.Normalize(invalid));
    }
    [Fact]
    public void ChangedPriceReturnsLatestPriceInsteadOfAcceptingClientValue()
    {
        var error = Assert.Throws<OrderRejectedException>(() => OrderRules.Price(Valid(), new[] { new Product(1, "Oranges", 4m, 1m) }));
        Assert.Equal("price_changed", error.Code); Assert.Equal(409, error.Status);
        Assert.Equal(4m, Assert.Single(error.Lines!).UnitPrice);
    }
    [Fact]
    public void MissingProductIsRejected() => Assert.Equal("product_unavailable", Assert.Throws<OrderRejectedException>(() => OrderRules.Price(Valid(), Array.Empty<Product>())).Code);
    [Fact]
    public void HashDetectsChangesButIgnoresItemOrder()
    {
        var request = Valid() with { Items = new[] { new OrderItemRequest(1, 2, 3.49m), new OrderItemRequest(2, 1, 1m) } };
        Assert.Equal(OrderRules.Hash(request), OrderRules.Hash(request with { Items = request.Items.Reverse().ToArray() }));
        Assert.NotEqual(OrderRules.Hash(request), OrderRules.Hash(request with { ShippingAddress = "Different address" }));
    }
    [Fact]
    public async Task SqlMissingConfigurationFailsWithoutExposingSecrets()
    {
        var store = new SqlOrderStore(new ConfigurationBuilder().Build(), NullLogger<SqlOrderStore>.Instance);
        var e = await Assert.ThrowsAsync<OrderRejectedException>(() => store.PlaceAsync(1, Valid(), default));
        Assert.Equal(503, e.Status);
    }
}

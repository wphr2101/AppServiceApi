using CustomerOperations.Api.Admin;
using CustomerOperations.Api.Orders;

namespace CustomerOperations.Api.Tests;

public sealed class AdminRulesTests
{
    [Theory]
    [InlineData("shipped","Pending","Unpaid")]
    [InlineData("paid","Cancelled","Unpaid")]
    [InlineData("shipped","Cancelled","Paid")]
    [InlineData("paid","Shipped","Unpaid")]
    [InlineData("unknown","Pending","Unpaid")]
    public void RejectsInvalidTransitions(string action,string status,string payment) =>
        Assert.Throws<OrderRejectedException>(()=>AdminRules.NeedsChange(action,status,payment));
    [Theory]
    [InlineData("paid","Pending","Paid")]
    [InlineData("shipped","Shipped","Paid")]
    [InlineData("shipped","Delivered","Paid")]
    public void RepeatingCompletedActionDoesNotChangeAnything(string action,string status,string payment)=>
        Assert.False(AdminRules.NeedsChange(action,status,payment));
    [Fact]
    public void AllowsPaymentThenShipment()
    {
        Assert.True(AdminRules.NeedsChange("paid","Pending","Unpaid"));
        Assert.True(AdminRules.NeedsChange("shipped","Pending","Paid"));
    }
    [Theory]
    [InlineData(0,"Reason")][InlineData(-1,"Reason")][InlineData(100001,"Reason")][InlineData(1," ")]
    public void RejectsInvalidRestock(int quantity,string reason)=>
        Assert.Throws<OrderRejectedException>(()=>AdminRules.Restock(new(Guid.NewGuid(),quantity,reason)));
    [Fact]
    public void RestockRequiresRequestIdAndBoundedReason()
    {
        Assert.Throws<OrderRejectedException>(()=>AdminRules.Restock(new(Guid.Empty,1,"Reason")));
        Assert.Throws<OrderRejectedException>(()=>AdminRules.Restock(new(Guid.NewGuid(),1,new string('a',251))));
        Assert.Equal("Reason",AdminRules.Restock(new(Guid.NewGuid(),1," Reason ")).Reason);
    }
}

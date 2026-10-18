using System.Security.Claims;
using CustomerOperations.Api.Orders;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CustomerOperations.Api.Controllers;

[ApiController]
[Authorize(Roles = "Customer")]
[Route("api/orders")]
[AutoValidateAntiforgeryToken]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[RequestSizeLimit(32768)]
public sealed class OrdersController(IOrderStore store) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Place(PlaceOrderRequest request, CancellationToken ct)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return Unauthorized();
        try
        {
            var receipt = await store.PlaceAsync(userId, OrderRules.Normalize(request), ct);
            return CreatedAtAction(nameof(Get), new { orderId = receipt.OrderId }, receipt);
        }
        catch (OrderRejectedException e) { return StatusCode(e.Status, new { code = e.Code, message = e.Message, lines = e.Lines }); }
    }

    [HttpGet("{orderId:int}")]
    public async Task<IActionResult> Get(int orderId, CancellationToken ct)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return Unauthorized();
        try
        {
            var receipt = await store.GetAsync(userId, orderId, ct);
            return receipt is null ? NotFound() : Ok(receipt);
        }
        catch (OrderRejectedException e) { return StatusCode(e.Status, new { code = e.Code, message = e.Message }); }
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int? beforeId, CancellationToken ct)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return Unauthorized();
        if (beforeId is <= 0) return BadRequest(new { message = "Invalid order cursor." });
        try { return Ok(await store.ListAsync(userId, beforeId, ct)); }
        catch (OrderRejectedException e)
        {
            return StatusCode(e.Status, new { code = e.Code, message = "Order history is temporarily unavailable." });
        }
    }
}

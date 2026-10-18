using System.Security.Claims;
using CustomerOperations.Api.Admin;
using CustomerOperations.Api.Orders;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace CustomerOperations.Api.Controllers;

[ApiController, Route("api/admin"), Authorize(Roles="Admin"), AutoValidateAntiforgeryToken]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None), RequestSizeLimit(8192)]
public sealed class AdminController(IAdminStore store) : Controller
{
    private int Actor => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier),out var id)?id:0;
    [HttpGet("inventory")]
    public async Task<IActionResult> Inventory(CancellationToken ct)=>Ok(await store.InventoryAsync(Actor,ct));
    [HttpGet("movements")]
    public async Task<IActionResult> Movements(CancellationToken ct)=>Ok(await store.MovementsAsync(Actor,ct));
    [HttpPost("inventory/{productId:int}/restock")]
    public async Task<IActionResult> Restock(int productId,RestockRequest request,CancellationToken ct)=>
        Ok(await store.RestockAsync(Actor,productId,AdminRules.Restock(request),ct));
    [HttpGet("orders")]
    public async Task<IActionResult> Orders([FromQuery]int? beforeId,CancellationToken ct)
    {
        if(beforeId is <=0) return BadRequest();
        return Ok(await store.OrdersAsync(Actor,beforeId,ct));
    }
    [HttpGet("orders/{orderId:int}")]
    public async Task<IActionResult> Detail(int orderId,CancellationToken ct)
    {
        var detail=await store.DetailAsync(Actor,orderId,ct); return detail is null?NotFound():Ok(detail);
    }
    [HttpPost("orders/{orderId:int}/{operation}")]
    public async Task<IActionResult> Change(int orderId,string operation,CancellationToken ct)
    { await store.ChangeOrderAsync(Actor,orderId,operation,ct); return NoContent(); }
    public override async Task OnActionExecutionAsync(ActionExecutingContext context,ActionExecutionDelegate next)
    {
        var executed=await next();
        if(executed.Exception is OrderRejectedException e)
        { executed.Result=StatusCode(e.Status,new {code=e.Code,message=e.Message}); executed.ExceptionHandled=true; }
    }
}

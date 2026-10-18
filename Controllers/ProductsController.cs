using CustomerOperations.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace CustomerOperations.Api.Controllers;

[ApiController]
[Route("api/products")]
public class ProductsController : ControllerBase
{
    private readonly IProductCatalog _catalog;
    private readonly ILogger<ProductsController> _logger;

    public ProductsController(IProductCatalog catalog, ILogger<ProductsController> logger)
    {
        _catalog = catalog;
        _logger = logger;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<Product>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetProducts(CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _catalog.GetProductsAsync(cancellationToken));
        }
        catch (ProductCatalogUnavailableException exception)
        {
            _logger.LogError(exception, "Unable to read the product catalog.");
            return Problem(
                title: "The product catalog is temporarily unavailable.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }
}

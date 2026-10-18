using CustomerOperations.Api.Controllers;
using CustomerOperations.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace CustomerOperations.Api.Tests;

public class ProductsTests
{
    [Fact]
    public async Task GetProducts_ReturnsRowsAndPassesCancellationToken()
    {
        using var cancellation = new CancellationTokenSource();
        IReadOnlyList<Product> products = new[] { new Product(1, "Parcel box", 12.50m, 0.125m) };
        var catalog = new Mock<IProductCatalog>();
        catalog.Setup(x => x.GetProductsAsync(cancellation.Token)).ReturnsAsync(products);
        var controller = new ProductsController(catalog.Object, NullLogger<ProductsController>.Instance);

        var result = Assert.IsType<OkObjectResult>(await controller.GetProducts(cancellation.Token));

        Assert.Same(products, result.Value);
        catalog.VerifyAll();
    }

    [Fact]
    public async Task GetProducts_EmptyCatalogReturnsOk()
    {
        var catalog = new Mock<IProductCatalog>();
        catalog.Setup(x => x.GetProductsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Product>());
        var controller = new ProductsController(catalog.Object, NullLogger<ProductsController>.Instance);
        var result = Assert.IsType<OkObjectResult>(await controller.GetProducts(default));
        Assert.Empty(Assert.IsAssignableFrom<IReadOnlyList<Product>>(result.Value));
    }

    [Fact]
    public async Task GetProducts_UnavailableDoesNotExposeInternalDetails()
    {
        var catalog = new Mock<IProductCatalog>();
        catalog.Setup(x => x.GetProductsAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ProductCatalogUnavailableException("private database detail"));
        var controller = new ProductsController(catalog.Object, NullLogger<ProductsController>.Instance);
        var result = Assert.IsType<ObjectResult>(await controller.GetProducts(default));
        var problem = Assert.IsType<ProblemDetails>(result.Value);
        Assert.Equal(503, result.StatusCode);
        Assert.Equal("The product catalog is temporarily unavailable.", problem.Title);
        Assert.Null(problem.Detail);
    }

    [Fact]
    public async Task GetProducts_CancellationIsNotReportedAsDatabaseFailure()
    {
        var catalog = new Mock<IProductCatalog>();
        catalog.Setup(x => x.GetProductsAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());
        var controller = new ProductsController(catalog.Object, NullLogger<ProductsController>.Instance);
        await Assert.ThrowsAsync<OperationCanceledException>(() => controller.GetProducts(default));
    }

    [Fact]
    public async Task SqlCatalog_MissingConfigurationFailsWithoutConnecting()
    {
        var catalog = new SqlProductCatalog(new ConfigurationBuilder().Build());
        await Assert.ThrowsAsync<ProductCatalogUnavailableException>(() => catalog.GetProductsAsync(default));
    }

    [Fact]
    public async Task SqlCatalog_InvalidConfigurationDoesNotExposeItsValue()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = "secret-invalid-value" }).Build();
        var exception = await Assert.ThrowsAsync<ProductCatalogUnavailableException>(
            () => new SqlProductCatalog(configuration).GetProductsAsync(default));
        Assert.DoesNotContain("secret-invalid-value", exception.ToString());
    }

    [Fact]
    public async Task SqlCatalog_CancelledRequestDoesNotConnect()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var catalog = new SqlProductCatalog(new ConfigurationBuilder().Build());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => catalog.GetProductsAsync(cancellation.Token));
    }
}

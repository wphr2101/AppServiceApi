using System.Net;
using System.Net.Http.Json;
using CustomerOperations.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using System.Text.Json;
using CustomerOperations.Api.Controllers;

namespace CustomerOperations.Api.Tests;

public sealed class BrowserSecurityTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public BrowserSecurityTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.ConfigureServices(services =>
            {
                var catalog = new Mock<IProductCatalog>();
                catalog.Setup(x => x.GetProductsAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync(Array.Empty<Product>());
                services.AddSingleton(catalog.Object);
            });
        }).CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false,
        });
    }

    [Theory]
    [InlineData("/api/products")]
    [InlineData("/api/health")]
    [InlineData("/api/auth/csrf")]
    public async Task PublicBrowserEndpointsDoNotRequireAnApiKey(string path)
    {
        using var response = await _client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/auth/me")]
    [InlineData("/api/orders")]
    [InlineData("/api/admin/inventory")]
    [InlineData("/api/admin/orders")]
    public async Task PrivateEndpointsStillRequireAnAuthenticatedCookie(string path)
    {
        _client.DefaultRequestHeaders.Add("X-API-Key", "an-api-key-is-not-a-user-session");
        using var response = await _client.GetAsync(path);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/auth/login")]
    [InlineData("/api/auth/register")]
    [InlineData("/api/auth/logout")]
    [InlineData("/api/insertComment")]
    public async Task AnonymousWritesStillRequireCsrf(string path)
    {
        using var response = await _client.PostAsJsonAsync(path, new { });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public void PublicCommentContractContainsNoContactDetails()
    {
        var json = JsonSerializer.SerializeToElement(new PublicVisitorComment("Demo", "Hello"));
        Assert.Equal(new[] { "Name", "Comments" }, json.EnumerateObject().Select(x => x.Name));
    }

    [Fact]
    public async Task UnknownApiPathsReturn404RatherThanTheAngularShell()
    {
        using var response = await _client.GetAsync("/api/not-a-real-endpoint");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}

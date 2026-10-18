using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CustomerOperations.Api.Accounts;
using CustomerOperations.Api.Controllers;
using CustomerOperations.Api.Orders;
using CustomerOperations.Api.Admin;
using CustomerOperations.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CustomerOperations.Api.Tests;

public sealed class AccountsTests : IAsyncLifetime
{
    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private readonly MemoryAccounts _store = new();
    private readonly MemoryOrderStore _orders = new();
    private const string Password = "test-only long passphrase!";

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddControllersWithViews().AddApplicationPart(typeof(AuthController).Assembly);
        builder.Services.AddCustomerAccounts(true);
        builder.Services.AddSingleton<IAccountStore>(_store);
        builder.Services.AddSingleton<IOrderStore>(_orders);
        _app = builder.Build();
        _app.UseDeveloperExceptionPage();
        _app.UseRouting();
        _app.UseRateLimiter();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.MapControllers();
        await _app.StartAsync();
        _client = NewClient();
    }
    private HttpClient NewClient() => new(new HttpClientHandler { CookieContainer = new CookieContainer(), AllowAutoRedirect = false }) { BaseAddress = new Uri(_app.Urls.Single()) };
    private async Task<HttpResponseMessage> Post(string route, object body, HttpClient? client = null)
    {
        client ??= _client;
        using var csrfResponse = await client.GetAsync("/api/auth/csrf");
        Assert.True(csrfResponse.IsSuccessStatusCode, await csrfResponse.Content.ReadAsStringAsync());
        var csrf = await csrfResponse.Content.ReadFromJsonAsync<JsonElement>();
        using var message = new HttpRequestMessage(HttpMethod.Post, route.StartsWith('/') ? route : "/api/auth/" + route) { Content = JsonContent.Create(body) };
        message.Headers.Add("X-CSRF-TOKEN", csrf.GetProperty("requestToken").GetString());
        var response = await client.SendAsync(message);
        Assert.True(response.StatusCode != HttpStatusCode.InternalServerError, await response.Content.ReadAsStringAsync());
        return response;
    }
    private Task<HttpResponseMessage> Register(string name = "demo.user") => Post("register", new { userName = name, password = Password, name = "Demo Customer", email = "demo@example.test", phone = "", userType = "Admin" });
    private Task<HttpResponseMessage> Login(string name = "demo.user", string password = Password) => Post("login", new { userName = name, password });

    [Fact]
    public async Task RegistrationHashesPasswordAndDoesNotAutomaticallySignIn()
    {
        using var response = await Register();
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var stored = Assert.Single(_store.Items).Value;
        Assert.NotEqual(Password, stored.PasswordHash);
        Assert.Equal("Customer",stored.Profile.Role);
        using var scope = _app.Services.CreateScope();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<AccountProfile>>();
        Assert.NotEqual(PasswordVerificationResult.Failed, hasher.VerifyHashedPassword(stored.Profile, stored.PasswordHash, Password));
        Assert.DoesNotContain(stored.PasswordHash, await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task LoginCookieIsHttpOnlyAndMeUsesAuthenticatedIdentity()
    {
        await Register();
        await Register("second.user");
        using var login = await Login();
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var cookie = login.Headers.GetValues("Set-Cookie").Single(x => x.StartsWith("CustomerOperations.Session="));
        Assert.Contains("httponly", cookie.ToLowerInvariant());
        Assert.Contains("samesite=strict", cookie.ToLowerInvariant());
        var profile = await _client.GetFromJsonAsync<AccountProfile>("/api/auth/me?userId=2");
        Assert.Equal("demo.user", profile!.UserName);
        using var stranger = NewClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await stranger.GetAsync("/api/auth/me?userId=1")).StatusCode);
    }

    [Fact]
    public async Task WrongPasswordAndUnknownUsernameHaveSameResponse()
    {
        await Register();
        using var wrong = await Login(password: "wrong password");
        using var missing = await Login("missing.user", "wrong password");
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal(wrong.StatusCode, missing.StatusCode);
        Assert.Equal(await wrong.Content.ReadAsStringAsync(), await missing.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task StateChangingEndpointsRejectMissingCsrf()
    {
        foreach (var action in new[] { "register", "login", "logout" })
            Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync("/api/auth/" + action, new { })).StatusCode);
        Assert.Empty(_store.Items);
    }

    [Fact]
    public async Task RegistrationRejectsShortPasswordAndWhitespaceName()
    {
        foreach (var input in new[] {
            new { userName = "demo.user", password = "short", name = "Demo", email = "demo@example.test" },
            new { userName = "demo.user", password = Password, name = "   ", email = "demo@example.test" },
            new { userName = "demo.user", password = Password, name = "Demo", email = "not-email" } })
            Assert.Equal(HttpStatusCode.BadRequest, (await Post("register", input)).StatusCode);
        Assert.Empty(_store.Items);
    }

    [Fact]
    public async Task DuplicateUsernameReturnsConflict()
    {
        await Register();
        Assert.Equal(HttpStatusCode.Conflict, (await Register("DEMO.USER")).StatusCode);
        Assert.Single(_store.Items);
    }

    [Fact]
    public async Task LogoutClearsSessionAndOldCsrfCannotBeUsedAfterLogin()
    {
        await Register();
        var anonymousCsrf = await _client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        await Login();
        using var stale = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout") { Content = JsonContent.Create(new { }) };
        stale.Headers.Add("X-CSRF-TOKEN", anonymousCsrf.GetProperty("requestToken").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.SendAsync(stale)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Post("logout", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task LoginAttemptsAreRateLimited()
    {
        for (var i = 0; i < 10; i++) Assert.Equal(HttpStatusCode.Unauthorized, (await Login("unknown.user")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await Login("unknown.user")).StatusCode);
    }

    [Fact]
    public async Task StorageFailureReturnsSanitizedProblem()
    {
        _store.Unavailable = true;
        using var response = await Login();
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Account service temporarily unavailable.", problem.GetProperty("title").GetString());
        Assert.False(problem.TryGetProperty("detail", out _));
    }

    [Fact]
    public async Task OrdersRequireLoginAndCsrf()
    {
        var request = new PlaceOrderRequest(Guid.NewGuid(), "Demo", "Example street", new[] { new OrderItemRequest(1, 2, 3.49m) });
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post("/api/orders", request)).StatusCode);
        await Register(); await Login();
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync("/api/orders", request)).StatusCode);
        Assert.Equal(0, _orders.Count);
    }

    [Fact]
    public async Task OrderRetryUsesSameReceiptAndOtherCustomersCannotReadIt()
    {
        await Register(); await Register("other.user"); await Login();
        var request = new PlaceOrderRequest(Guid.NewGuid(), "Demo", "Example street", new[] { new OrderItemRequest(1, 2, 3.49m) });
        using var first = await Post("/api/orders", request);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var receipt = await first.Content.ReadFromJsonAsync<OrderReceipt>();
        Assert.Equal(6.98m, receipt!.Subtotal);
        using var retry = await Post("/api/orders", request);
        Assert.Equal(receipt.OrderId, (await retry.Content.ReadFromJsonAsync<OrderReceipt>())!.OrderId);
        Assert.Equal(1, _orders.Count);
        Assert.Equal(HttpStatusCode.Conflict, (await Post("/api/orders", request with { RecipientName = "Changed" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync($"/api/orders/{receipt.OrderId}")).StatusCode);
        await Post("logout", new { }); await Login("other.user");
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/orders/{receipt.OrderId}")).StatusCode);
    }

    [Fact]
    public async Task PriceChangeOrInvalidCartDoesNotCreateOrder()
    {
        await Register(); await Login();
        var request = new PlaceOrderRequest(Guid.NewGuid(), "Demo", "Example street", new[] { new OrderItemRequest(1, 1, 0.01m) });
        using var response = await Post("/api/orders", request);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("price_changed", body.GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await Post("/api/orders", request with { Items = Array.Empty<OrderItemRequest>() })).StatusCode);
        Assert.Equal(0, _orders.Count);
    }

    [Fact]
    public async Task OrderHistoryIsPrivatePaginatedAndUsesStoredPrices()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync("/api/orders")).StatusCode);
        await Register(); await Register("history.other"); await Login();
        for (var i = 0; i < 23; i++)
            await _orders.PlaceAsync(1, new(Guid.NewGuid(), "Demo", "Example", new[] { new OrderItemRequest(1, 2, 3.49m) }), default);
        await _orders.PlaceAsync(2, new(Guid.NewGuid(), "Other", "Example", new[] { new OrderItemRequest(1, 1, 3.49m) }), default);
        _orders.Price = 9m;
        var first = await _client.GetFromJsonAsync<OrderPage>("/api/orders?userId=2&customerId=2");
        Assert.Equal(20, first!.Items.Count);
        Assert.True(first.HasMore);
        Assert.Equal(23, first.Items[0].OrderId);
        Assert.All(first.Items, item => { Assert.Equal(6.98m, item.Subtotal); Assert.Equal(2, item.ItemCount); });
        var next = await _client.GetFromJsonAsync<OrderPage>($"/api/orders?beforeId={first.Items.Last().OrderId}");
        Assert.Equal(new[] { 3, 2, 1 }, next!.Items.Select(i => i.OrderId));
        Assert.False(next.HasMore);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync("/api/orders?beforeId=0")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync("/api/orders?beforeId=invalid")).StatusCode);
        await Post("logout", new { }); await Login("history.other");
        var other = await _client.GetFromJsonAsync<OrderPage>("/api/orders");
        Assert.Equal(24, Assert.Single(other!.Items).OrderId);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/orders/1")).StatusCode);
    }

    [Fact]
    public async Task NewCustomerHasEmptyHistory()
    {
        await Register(); await Login();
        var page = await _client.GetFromJsonAsync<OrderPage>("/api/orders");
        Assert.Empty(page!.Items);
        Assert.False(page.HasMore);
    }

    [Fact]
    public async Task AdminEndpointsRejectAnonymousAndCustomerEvenWhenRoleIsSpoofed()
    {
        foreach(var route in new[]{"inventory","orders","movements","orders/1"})
            Assert.Equal(HttpStatusCode.Unauthorized,(await _client.GetAsync("/api/admin/"+route)).StatusCode);
        await Register();await Login();
        foreach(var route in new[]{"inventory","orders","movements","orders/1"})
            Assert.Equal(HttpStatusCode.Forbidden,(await _client.GetAsync("/api/admin/"+route+"?role=Admin")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await Post("/api/admin/orders/1/paid",new{})).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await Post("/api/admin/inventory/1/restock",new{requestId=Guid.NewGuid(),quantity=5,reason="Demo",role="Admin"})).StatusCode);
    }

    [Fact]
    public async Task AdminLoginUsesStoredRoleAndMutationsRequireCsrf()
    {
        await Register();
        var stored=_store.Items["demo.user"];
        _store.Items["demo.user"]=stored with { Profile=stored.Profile with { Role="Admin" } };
        var login=await Login();
        Assert.Equal("Admin",(await login.Content.ReadFromJsonAsync<AccountProfile>())!.Role);
        Assert.Equal(HttpStatusCode.BadRequest,(await _client.PostAsJsonAsync("/api/admin/orders/1/paid",new{})).StatusCode);
    }

    public async Task DisposeAsync() { _client.Dispose(); await _app.DisposeAsync(); }

    private sealed class MemoryAccounts : IAccountStore
    {
        public Dictionary<string, AccountCredential> Items { get; } = new(StringComparer.OrdinalIgnoreCase);
        public bool Unavailable { get; set; }
        public Task<AccountCredential?> FindAsync(string userName, CancellationToken ct)
        {
            if (Unavailable) throw new AccountUnavailableException();
            return Task.FromResult(Items.GetValueOrDefault(userName));
        }
        public Task<AccountProfile?> GetAsync(int userId, CancellationToken ct) => Task.FromResult(Items.Values.FirstOrDefault(x => x.Profile.UserId == userId)?.Profile);
        public Task<AccountProfile> CreateAsync(RegisterRequest request, string hash, CancellationToken ct)
        {
            if (Items.ContainsKey(request.UserName)) throw new AccountConflictException();
            var id = Items.Count + 1;
            var profile = new AccountProfile(id, request.UserName, id, request.Name, request.Email, request.Phone);
            Items.Add(request.UserName, new(profile, hash));
            return Task.FromResult(profile);
        }
        public Task UpdateHashAsync(int userId, string oldHash, string newHash, CancellationToken ct) => Task.CompletedTask;
    }
}

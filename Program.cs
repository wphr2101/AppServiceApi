using CustomerOperations.Api.Security;
using CustomerOperations.Api.Services;
using Microsoft.OpenApi;
using CustomerOperations.Api.Accounts;
using CustomerOperations.Api.Orders;
using CustomerOperations.Api.Admin;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAngularApp", policy =>
    {
        policy
            .WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
                ?? (builder.Environment.IsDevelopment() ? new[] { "http://localhost:4200" } : Array.Empty<string>()))
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

builder.Services.AddControllersWithViews();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "CustomerOperations.Api",
        Version = "v1"
    });
});
builder.Services.AddSingleton<ApiKeyValidator>();
builder.Services.AddScoped<IProductCatalog, SqlProductCatalog>();
builder.Services.AddCustomerAccounts(builder.Environment.IsDevelopment());
builder.Services.AddScoped<IOrderStore, SqlOrderStore>();
builder.Services.AddScoped<IAdminStore, SqlAdminStore>();

var app = builder.Build();
 
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.DocumentTitle = "CustomerOperations.Api";
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "CustomerOperations.Api v1");
    });
}

app.UseRouting();
app.UseCors("AllowAngularApp");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.UseDefaultFiles();
app.UseStaticFiles();

app.Use(async (context, next) =>
{
    if (!context.Request.Path.StartsWithSegments("/api") ||
        context.Request.Path.StartsWithSegments("/api/health"))
    {
        await next();
        return;
    }

    var validator = context.RequestServices.GetRequiredService<ApiKeyValidator>();
    var configuredToken = app.Configuration["ApiAccess:Token"];
    context.Request.Headers.TryGetValue("X-API-Key", out var providedToken);
    var validation = validator.Validate(configuredToken, providedToken.ToString());

    if (validation.Status == ApiKeyValidationStatus.TokenNotConfigured)
    {
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        await context.Response.WriteAsJsonAsync(new { message = validation.PublicMessage });
        return;
    }

    if (!validation.IsValid)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new { message = validation.PublicMessage });
        return;
    }

    await next();
});

app.MapControllers();
app.MapFallbackToFile("index.html");

app.Run();

using AppServiceApi.Security;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAngularApp", policy =>
    {
        policy
            .AllowAnyOrigin()
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSingleton<ApiKeyValidator>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("AllowAngularApp");

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
        await context.Response.WriteAsJsonAsync(new { message = "API token is not configured." });
        return;
    }

    if (!validation.IsValid)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new { message = "Invalid or missing API token." });
        return;
    }

    await next();
});

app.MapControllers();
app.MapFallbackToFile("index.html");

app.Run();

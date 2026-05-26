using Microsoft.Data.SqlClient;

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

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("AllowAngularApp");

app.MapGet("/api/nations", async (IConfiguration configuration) =>
{
    var connectionString = configuration.GetConnectionString("DefaultConnection");

    if (string.IsNullOrWhiteSpace(connectionString))
    {
        return Results.Problem("Missing DefaultConnection connection string.");
    }

    try
    {
        var connectionBuilder = new SqlConnectionStringBuilder(connectionString)
        {
            ConnectTimeout = 15
        };

        var nations = new List<Nation>();

        await using var connection = new SqlConnection(connectionBuilder.ConnectionString);
        await connection.OpenAsync();

        const string sql = """
            SELECT
                [Name],
                Capital,
                FlagImage,
                MapImage,
                Pupulation,
                GDP,
                HDI
            FROM dbo.nations
            ORDER BY [Name];
            """;

        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            nations.Add(new Nation(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetInt32(4),
                Convert.ToDouble(reader.GetFloat(5)),
                Convert.ToDouble(reader.GetFloat(6))));
        }

        return Results.Ok(nations);
    }
    catch (SqlException ex)
    {
        return Results.Problem(
            title: "Could not connect to the Nations database.",
            detail: ex.Message,
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

app.Run();

record Nation(
    string Name,
    string Capital,
    string FlagImage,
    string MapImage,
    int Population,
    double Gdp,
    double Hdi
);

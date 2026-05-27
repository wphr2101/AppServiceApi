using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace NationsApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class NationsController : ControllerBase
{
    private readonly IConfiguration _configuration;

    public NationsController(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    [HttpGet]
    public async Task<IActionResult> getAllNations()
    {
        return await ReadNationsAsync(null);
    }

    [HttpGet("search/{result}")]
    public async Task<IActionResult> getNationsBySearch(string result)
    {
        return await ReadNationsAsync(result);
    }

    private async Task<IActionResult> ReadNationsAsync(string? search)
    {
        var connectionString = _configuration.GetConnectionString("DefaultConnection");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return Problem("Missing DefaultConnection connection string.");
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
                WHERE @Search IS NULL
                   OR [Name] LIKE @Search
                   OR Capital LIKE @Search
                ORDER BY [Name];
                """;

            await using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue(
                "@Search",
                string.IsNullOrWhiteSpace(search) ? DBNull.Value : $"%{search}%");

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

            return Ok(nations);
        }
        catch (SqlException ex)
        {
            return Problem(
                title: "Could not connect to the Nations database.",
                detail: ex.Message,
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }
}

public record Nation(
    string Name,
    string Capital,
    string FlagImage,
    string MapImage,
    int Population,
    double Gdp,
    double Hdi
);

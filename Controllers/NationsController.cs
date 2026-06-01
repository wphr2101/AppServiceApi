using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data;

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
        return await ReadNationsAsync("dbo.usp_get_all_nations");
    }

    [HttpGet("search/{result}")]
    public async Task<IActionResult> getNationsBySearch(string result)
    {
        return await ReadNationsAsync("dbo.usp_get_nation_capital_by_value", result);
    }

    private async Task<IActionResult> ReadNationsAsync(
        string storedProcedure,
        string? searchValue = null)
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

            await using var command = new SqlCommand(storedProcedure, connection)
            {
                CommandType = CommandType.StoredProcedure
            };

            if (searchValue is not null)
            {
                command.Parameters.Add("@Value", SqlDbType.VarChar, 50).Value = searchValue;
            }

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

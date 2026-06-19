using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace AppServiceApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HealthController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _environment;

    public HealthController(
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        _configuration = configuration;
        _environment = environment;
    }

    [HttpGet]
    public IActionResult GetHealth()
    {
        return Ok(new
        {
            status = "Healthy",
            service = "AppServiceApi",
            environment = _environment.EnvironmentName,
            utcTime = DateTime.UtcNow
        });
    }

    [HttpGet("database")]
    public async Task<IActionResult> GetDatabaseHealth()
    {
        var connectionString = _configuration.GetConnectionString("DefaultConnection");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return Problem(
                title: "Database configuration is missing.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        try
        {
            var connectionBuilder = new SqlConnectionStringBuilder(connectionString)
            {
                ConnectTimeout = 15
            };

            await using var connection = new SqlConnection(connectionBuilder.ConnectionString);
            await connection.OpenAsync();

            return Ok(new
            {
                status = "Healthy",
                database = "Connected",
                utcTime = DateTime.UtcNow
            });
        }
        catch (SqlException)
        {
            return Problem(
                title: "Database connection is unavailable.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }
}

using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data;

namespace AppServiceApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class InputFormController : ControllerBase
{
    private readonly IConfiguration _configuration;

    public InputFormController(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    [HttpGet]
    public async Task<IActionResult> getAllComments()
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

            var comments = new List<VisitorComment>();

            await using var connection = new SqlConnection(connectionBuilder.ConnectionString);
            await connection.OpenAsync();

            await using var command = new SqlCommand("dbo.usp_get_all_comments", connection)
            {
                CommandType = CommandType.StoredProcedure
            };

            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                comments.Add(new VisitorComment(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.GetString(5),
                    reader.GetString(6),
                    reader.GetString(7)));
            }

            return Ok(comments);
        }
        catch (SqlException ex)
        {
            return Problem(
                title: "Could not connect to the VisitorComments database.",
                detail: ex.Message,
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    [HttpPost("/api/insertComment")]
    public async Task<IActionResult> insertComment(VisitorComment comment)
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

            await using var connection = new SqlConnection(connectionBuilder.ConnectionString);
            await connection.OpenAsync();

            await using var command = new SqlCommand("dbo.usp_insert_comment", connection)
            {
                CommandType = CommandType.StoredProcedure
            };

            command.Parameters.Add("@Name", SqlDbType.VarChar, 100).Value = comment.Name;
            command.Parameters.Add("@Email", SqlDbType.VarChar, 100).Value = comment.Email;
            command.Parameters.Add("@Address", SqlDbType.VarChar, 250).Value = comment.Address;
            command.Parameters.Add("@City", SqlDbType.VarChar, 100).Value = comment.City;
            command.Parameters.Add("@State", SqlDbType.VarChar, 100).Value = comment.State;
            command.Parameters.Add("@Zipcode", SqlDbType.VarChar, 100).Value = comment.Zipcode;
            command.Parameters.Add("@PhoneNumber", SqlDbType.VarChar, 50).Value = comment.PhoneNumber;
            command.Parameters.Add("@Comments", SqlDbType.VarChar, -1).Value = comment.Comments;

            await command.ExecuteNonQueryAsync();

            return Ok(new { message = "Comment inserted successfully." });
        }
        catch (SqlException ex)
        {
            return Problem(
                title: "Could not insert the visitor comment.",
                detail: ex.Message,
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }
}

public record VisitorComment(
    string Name,
    string Email,
    string Address,
    string City,
    string State,
    string Zipcode,
    string PhoneNumber,
    string Comments
);

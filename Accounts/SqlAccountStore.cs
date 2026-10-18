using System.Data;
using Microsoft.Data.SqlClient;

namespace CustomerOperations.Api.Accounts;

public sealed class SqlAccountStore(IConfiguration configuration) : IAccountStore
{
    private const string ProfileColumns = "u.UserId, u.UserName, c.CustomerId, c.Name, c.Email, c.Phone, u.UserType";

    private async Task<T> ExecuteAsync<T>(Func<SqlConnection, Task<T>> action, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            var value = configuration.GetConnectionString("DefaultConnection");
            if (string.IsNullOrWhiteSpace(value)) throw new AccountUnavailableException();
            var settings = new SqlConnectionStringBuilder(value) { ConnectTimeout = 15 };
            await using var connection = new SqlConnection(settings.ConnectionString);
            await connection.OpenAsync(ct);
            return await action(connection);
        }
        catch (SqlException e) when (e.Number is 2601 or 2627) { throw new AccountConflictException(); }
        catch (SqlException) { throw new AccountUnavailableException(); }
        catch (ArgumentException) { throw new AccountUnavailableException(); }
    }

    private static AccountProfile ReadProfile(SqlDataReader r) => new(
        r.GetInt32(0), r.GetString(1), r.GetInt32(2), r.GetString(3), r.GetString(4), r.IsDBNull(5) ? null : r.GetString(5), r.GetString(6));

    public Task<AccountCredential?> FindAsync(string userName, CancellationToken ct) => ExecuteAsync<AccountCredential?>(async connection =>
    {
        await using var command = new SqlCommand($"SELECT {ProfileColumns}, u.PasswordHash FROM logistics.Users u JOIN logistics.Customers c ON c.UserId = u.UserId WHERE u.UserName = @name AND u.UserType IN ('Customer', 'Admin');", connection);
        command.Parameters.Add("@name", SqlDbType.NVarChar, 100).Value = userName;
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? new AccountCredential(ReadProfile(reader), reader.GetString(7)) : null;
    }, ct);

    public Task<AccountProfile?> GetAsync(int userId, CancellationToken ct) => ExecuteAsync<AccountProfile?>(async connection =>
    {
        await using var command = new SqlCommand($"SELECT {ProfileColumns} FROM logistics.Users u JOIN logistics.Customers c ON c.UserId = u.UserId WHERE u.UserId = @id AND u.UserType IN ('Customer', 'Admin');", connection);
        command.Parameters.Add("@id", SqlDbType.Int).Value = userId;
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadProfile(reader) : null;
    }, ct);

    public Task<AccountProfile> CreateAsync(RegisterRequest request, string passwordHash, CancellationToken ct) => ExecuteAsync(async connection =>
    {
        // Both rows commit together; the unique username constraint handles concurrent registration.
        using var transaction = connection.BeginTransaction();
        await using var user = new SqlCommand("INSERT INTO logistics.Users (UserName, PasswordHash, UserType) OUTPUT INSERTED.UserId VALUES (@user, @hash, 'Customer');", connection, transaction);
        user.Parameters.Add("@user", SqlDbType.NVarChar, 100).Value = request.UserName;
        user.Parameters.Add("@hash", SqlDbType.NVarChar, 512).Value = passwordHash;
        var userId = (int)(await user.ExecuteScalarAsync(ct))!;
        await using var customer = new SqlCommand("INSERT INTO logistics.Customers (UserId, Name, Email, Phone) OUTPUT INSERTED.CustomerId VALUES (@id, @name, @email, @phone);", connection, transaction);
        customer.Parameters.Add("@id", SqlDbType.Int).Value = userId;
        customer.Parameters.Add("@name", SqlDbType.NVarChar, 150).Value = request.Name;
        customer.Parameters.Add("@email", SqlDbType.NVarChar, 254).Value = request.Email;
        customer.Parameters.Add("@phone", SqlDbType.NVarChar, 30).Value = (object?)request.Phone ?? DBNull.Value;
        var customerId = (int)(await customer.ExecuteScalarAsync(ct))!;
        await transaction.CommitAsync(ct);
        return new AccountProfile(userId, request.UserName, customerId, request.Name, request.Email, request.Phone);
    }, ct);

    public async Task UpdateHashAsync(int userId, string oldHash, string newHash, CancellationToken ct) => await ExecuteAsync(async connection =>
    {
        await using var command = new SqlCommand("UPDATE logistics.Users SET PasswordHash = @new WHERE UserId = @id AND PasswordHash = @old;", connection);
        command.Parameters.Add("@id", SqlDbType.Int).Value = userId;
        command.Parameters.Add("@old", SqlDbType.NVarChar, 512).Value = oldHash;
        command.Parameters.Add("@new", SqlDbType.NVarChar, 512).Value = newHash;
        return await command.ExecuteNonQueryAsync(ct);
    }, ct);
}

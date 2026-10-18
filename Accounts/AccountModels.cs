using System.ComponentModel.DataAnnotations;

namespace CustomerOperations.Api.Accounts;

public sealed record RegisterRequest(
    [Required, RegularExpression(@"[A-Za-z0-9_.-]{3,100}")] string UserName,
    [Required, StringLength(128, MinimumLength = 15)] string Password,
    [Required, StringLength(150), RegularExpression(@".*\S.*")] string Name,
    [Required, EmailAddress, StringLength(254)] string Email,
    [StringLength(30)] string? Phone);

public sealed record LoginRequest(
    [Required, StringLength(100)] string UserName,
    [Required, StringLength(128)] string Password);

public sealed record AccountProfile(int UserId, string UserName, int CustomerId, string Name, string Email, string? Phone, string Role = "Customer");
public sealed record AccountCredential(AccountProfile Profile, string PasswordHash);

public interface IAccountStore
{
    Task<AccountCredential?> FindAsync(string userName, CancellationToken ct);
    Task<AccountProfile?> GetAsync(int userId, CancellationToken ct);
    Task<AccountProfile> CreateAsync(RegisterRequest request, string passwordHash, CancellationToken ct);
    Task UpdateHashAsync(int userId, string oldHash, string newHash, CancellationToken ct);
}

public sealed class AccountConflictException : Exception;
public sealed class AccountUnavailableException : Exception;

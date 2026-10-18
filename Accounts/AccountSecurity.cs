using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;

namespace CustomerOperations.Api.Accounts;

public static class AccountSecurity
{
    public static IServiceCollection AddCustomerAccounts(this IServiceCollection services, bool development)
    {
        services.AddScoped<IAccountStore, SqlAccountStore>();
        services.AddScoped<IPasswordHasher<AccountProfile>, PasswordHasher<AccountProfile>>();
        services.Configure<PasswordHasherOptions>(o => o.IterationCount = 210_000);
        services.AddSingleton<LoginTimingHash>();
        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(o =>
        {
            o.Cookie.Name = "CustomerOperations.Session";
            o.Cookie.HttpOnly = true;
            o.Cookie.SameSite = SameSiteMode.Strict;
            o.Cookie.SecurePolicy = development ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
            o.ExpireTimeSpan = TimeSpan.FromMinutes(30);
            o.SlidingExpiration = false;
            o.Events.OnRedirectToLogin = c => { c.Response.StatusCode = 401; return Task.CompletedTask; };
            o.Events.OnRedirectToAccessDenied = c => { c.Response.StatusCode = 403; return Task.CompletedTask; };
        });
        services.AddAuthorization(o => o.FallbackPolicy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser().Build());
        services.AddAntiforgery(o =>
        {
            o.HeaderName = "X-CSRF-TOKEN";
            o.Cookie.Name = "CustomerOperations.Csrf";
            o.Cookie.HttpOnly = true;
            o.Cookie.SameSite = SameSiteMode.Strict;
            o.Cookie.SecurePolicy = development ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
        });
        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.AddPolicy("account-attempts", context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
            o.AddPolicy("visitor-submissions", context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
        });
        return services;
    }
}

// Unknown usernames still perform a password verification, reducing account-enumeration timing differences.
public sealed class LoginTimingHash
{
    public AccountProfile User { get; } = new(0, "", 0, "", "", null);
    public string Hash { get; }
    public LoginTimingHash(Microsoft.Extensions.Options.IOptions<PasswordHasherOptions> options)
        => Hash = new PasswordHasher<AccountProfile>(options).HashPassword(User, Guid.NewGuid().ToString());
}

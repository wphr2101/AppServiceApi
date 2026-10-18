using System.Security.Claims;
using CustomerOperations.Api.Accounts;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.RateLimiting;

namespace CustomerOperations.Api.Controllers;

[ApiController]
[Route("api/auth")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[AutoValidateAntiforgeryToken]
[RequestSizeLimit(8192)]
public sealed class AuthController(IAccountStore store, IPasswordHasher<AccountProfile> hasher,
    LoginTimingHash timingHash, IAntiforgery antiforgery, ILogger<AuthController> logger) : Controller
{
    [HttpGet("csrf")]
    [AllowAnonymous]
    public IActionResult Csrf() => Ok(new { requestToken = antiforgery.GetAndStoreTokens(HttpContext).RequestToken });

    [HttpPost("register")]
    [AllowAnonymous]
    [EnableRateLimiting("account-attempts")]
    public async Task<IActionResult> Register(RegisterRequest request, CancellationToken ct)
    {
        if (User.Identity?.IsAuthenticated == true) return Conflict(new { message = "Sign out before creating another account." });
        var clean = request with { UserName = request.UserName.Trim(), Name = request.Name.Trim(), Email = request.Email.Trim(), Phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim() };
        var placeholder = new AccountProfile(0, clean.UserName, 0, clean.Name, clean.Email, clean.Phone);
        var profile = await store.CreateAsync(clean, hasher.HashPassword(placeholder, clean.Password), ct);
        // Registration does not automatically sign in; login is a separate, explicit action.
        return StatusCode(StatusCodes.Status201Created, new { message = "Account created. Sign in to continue." });
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("account-attempts")]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken ct)
    {
        if (User.Identity?.IsAuthenticated == true) return Conflict(new { message = "Sign out before switching accounts." });
        var account = await store.FindAsync(request.UserName.Trim(), ct);
        var verification = hasher.VerifyHashedPassword(account?.Profile ?? timingHash.User, account?.PasswordHash ?? timingHash.Hash, request.Password);
        if (account is null || verification == PasswordVerificationResult.Failed)
            return Unauthorized(new { message = "Invalid username or password." });
        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
            await store.UpdateHashAsync(account.Profile.UserId, account.PasswordHash, hasher.HashPassword(account.Profile, request.Password), ct);
        var identity = new ClaimsIdentity(new[] {
            new Claim(ClaimTypes.NameIdentifier, account.Profile.UserId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new Claim(ClaimTypes.Name, account.Profile.UserName),
            new Claim(ClaimTypes.Role, "Customer")
        }, CookieAuthenticationDefaults.AuthenticationScheme);
        if (account.Profile.Role == "Admin") identity.AddClaim(new Claim(ClaimTypes.Role, "Admin"));
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity), new AuthenticationProperties { IsPersistent = false, AllowRefresh = false });
        return Ok(account.Profile);
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)) return Unauthorized();
        var profile = await store.GetAsync(id, ct);
        if (profile is not null) return Ok(profile);
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Unauthorized();
    }

    [HttpPost("logout")]
    [AllowAnonymous]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return NoContent();
    }

    public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var executed = await next();
        if (executed.Exception is AccountConflictException)
        {
            executed.Result = Conflict(new { message = "Unable to register with those details. Try a different username." });
            executed.ExceptionHandled = true;
        }
        else if (executed.Exception is AccountUnavailableException)
        {
            logger.LogWarning("Account storage unavailable. TraceId: {TraceId}", HttpContext.TraceIdentifier);
            executed.Result = Problem(statusCode: 503, title: "Account service temporarily unavailable.");
            executed.ExceptionHandled = true;
        }
    }
}

using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProductHub.Infrastructure.Persistence;
using ProductHub.Infrastructure.Security;

namespace ProductHub.Api.Controllers;

[ApiController, Route("api/auth")]
public sealed class AuthController(ProductHubDbContext db) : ControllerBase
{
    [AllowAnonymous, HttpPost("login")]
    public async Task<ActionResult<CurrentUserResponse>> Login(
        LoginRequest request,
        CancellationToken cancellationToken
    )
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await db
            .AppUsers.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Email == email, cancellationToken);
        if (user is null || !PasswordService.Verify(request.Password, user.PasswordHash))
            return Unauthorized(
                new ProblemDetails
                {
                    Title = "Login failed",
                    Detail = "Email or password is incorrect.",
                }
            );
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.DisplayName),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Role, user.Role.ToString()),
        };
        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(
                new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)
            ),
            new AuthenticationProperties
            {
                IsPersistent = request.RememberMe,
                ExpiresUtc = DateTimeOffset.UtcNow.AddHours(12),
            }
        );
        return Ok(
            new CurrentUserResponse(user.Id, user.Email, user.DisplayName, user.Role.ToString())
        );
    }

    [Authorize, HttpGet("me")]
    public ActionResult<CurrentUserResponse> Me() =>
        Ok(
            new CurrentUserResponse(
                Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!),
                User.FindFirstValue(ClaimTypes.Email)!,
                User.Identity!.Name!,
                User.FindFirstValue(ClaimTypes.Role)!
            )
        );

    [Authorize, HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return NoContent();
    }
}

public sealed record LoginRequest(string Email, string Password, bool RememberMe = false);

public sealed record CurrentUserResponse(Guid Id, string Email, string DisplayName, string Role);

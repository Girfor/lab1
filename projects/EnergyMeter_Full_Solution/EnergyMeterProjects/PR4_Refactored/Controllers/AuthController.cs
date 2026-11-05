using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EnergyMeter.Refactored.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly IWebHostEnvironment _environment;
    private readonly IConfiguration _configuration;

    public AuthController(
        IWebHostEnvironment environment,
        IConfiguration configuration)
    {
        _environment = environment;
        _configuration = configuration;
    }

    /// <summary>Получить CSRF-токен для браузерной сессии.</summary>
    [AllowAnonymous]
    [HttpGet("csrf")]
    public IActionResult GetCsrf([FromServices] IAntiforgery antiforgery)
    {
        var tokens = antiforgery.GetAndStoreTokens(HttpContext);
        return Ok(new { token = tokens.RequestToken });
    }

    /// <summary>Демо-вход. Доступен только в Development.</summary>
    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<IActionResult> Login(
        [FromBody] LoginRequest request,
        [FromHeader(Name = "X-CSRF-TOKEN")] string? csrfToken)
    {
        if (!_environment.IsDevelopment())
            return NotFound();

        var expected = _configuration["Demo:Password"];
        if (string.IsNullOrWhiteSpace(expected))
            return StatusCode(503, "Задайте Demo__Password в переменных окружения.");

        var actualBytes = Encoding.UTF8.GetBytes(request.Password ?? "");
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var passwordOk = actualBytes.Length == expectedBytes.Length &&
            CryptographicOperations.FixedTimeEquals(actualBytes, expectedBytes);

        if (request.Username != "operator" || !passwordOk)
            return Unauthorized();

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "demo-operator"),
            new Claim(ClaimTypes.Name, "operator"),
            new Claim(ClaimTypes.Role, "Operator")
        };

        var principal = new ClaimsPrincipal(
            new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal);

        return Ok(new { message = "Демо-вход выполнен." });
    }

    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(
            CookieAuthenticationDefaults.AuthenticationScheme);
        return NoContent();
    }
}

public sealed record LoginRequest(string Username, string Password);

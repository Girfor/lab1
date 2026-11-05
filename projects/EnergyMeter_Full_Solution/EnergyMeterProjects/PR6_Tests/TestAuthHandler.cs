using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace EnergyMeter.PR6.Tests;

public sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string Scheme = "TestScheme";

    public TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "test-operator"),
            new Claim(ClaimTypes.Name, "operator"),
            new Claim(ClaimTypes.Role, "Operator")
        };

        var identity = new ClaimsIdentity(claims, Scheme);
        var ticket = new AuthenticationTicket(
            new ClaimsPrincipal(identity), Scheme);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

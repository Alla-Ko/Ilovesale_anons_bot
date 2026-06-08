using System.Security.Claims;
using System.Text.Encodings.Web;
using Announcement.Options;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Announcement.Authorization;

public class BotApiKeyAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "BotApiKey";
    public const string ApiKeyHeaderName = "X-Api-Key";

    private readonly BotApiOptions _botApi;

    public BotApiKeyAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IOptions<BotApiOptions> botApi)
        : base(options, logger, encoder)
    {
        _botApi = botApi.Value;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var configuredKey = (_botApi.ApiKey ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(configuredKey) || configuredKey.StartsWith("YOUR_", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(AuthenticateResult.Fail("Bot API key is not configured."));

        if (!TryGetProvidedKey(out var providedKey))
            return Task.FromResult(AuthenticateResult.Fail("Missing API key."));

        if (!string.Equals(providedKey, configuredKey, StringComparison.Ordinal))
            return Task.FromResult(AuthenticateResult.Fail("Invalid API key."));

        var identity = new ClaimsIdentity(SchemeName);
        identity.AddClaim(new Claim(ClaimTypes.Name, "bot-api"));
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    private bool TryGetProvidedKey(out string key)
    {
        key = string.Empty;
        if (Request.Headers.TryGetValue(ApiKeyHeaderName, out var headerValue))
        {
            var h = headerValue.ToString().Trim();
            if (!string.IsNullOrEmpty(h))
            {
                key = h;
                return true;
            }
        }

        var auth = Request.Headers.Authorization.ToString();
        if (auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            var token = auth["Bearer ".Length..].Trim();
            if (!string.IsNullOrEmpty(token))
            {
                key = token;
                return true;
            }
        }

        return false;
    }
}

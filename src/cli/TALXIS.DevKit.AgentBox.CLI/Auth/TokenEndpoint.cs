using System.Globalization;
using System.Text.RegularExpressions;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.Identity.Client;
using Microsoft.Extensions.Logging;
using TALXIS.DevKit.AgentBox.CLI.Cli;

namespace TALXIS.DevKit.AgentBox.CLI.Auth;

public interface ITokenProvider
{
    Task<IdentityToken> AcquireAsync(string resource, CancellationToken cancellationToken);
}

public sealed record IdentityToken(string AccessToken, DateTimeOffset ExpiresOn, string Resource, string TokenType, string CorrelationId);

public sealed record TokenResponse(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("refresh_token")] string RefreshToken,
    [property: JsonPropertyName("expires_in")] string ExpiresIn,
    [property: JsonPropertyName("expires_on")] string ExpiresOn,
    [property: JsonPropertyName("resource")] string Resource,
    [property: JsonPropertyName("token_type")] string TokenType);

public sealed record TokenErrorResponse(
    [property: JsonPropertyName("error")] string Error,
    [property: JsonPropertyName("error_code")] string? ErrorCode = null,
    [property: JsonPropertyName("error_description")] string? ErrorDescription = null,
    [property: JsonPropertyName("correlation_id")] string? CorrelationId = null);

public sealed record TokenRequest(
    string? MetadataHeader,
    IReadOnlyDictionary<string, string?> Query,
    IReadOnlyDictionary<string, string?> Form);

public sealed record TokenHttpResult(int StatusCode, object Body);

public sealed class TokenRequestHandler(ITokenProvider tokenProvider, ILogger logger)
{
    private static readonly HashSet<string> IdentitySelectors = new(StringComparer.OrdinalIgnoreCase)
    {
        "client_id", "clientid", "object_id", "objectid", "principal_id", "principalid",
        "resource_id", "resourceid", "msi_res_id", "mi_res_id",
    };

    public async Task<TokenHttpResult> HandleAsync(TokenRequest request, CancellationToken cancellationToken)
    {
        if (!string.Equals(request.MetadataHeader, "true", StringComparison.OrdinalIgnoreCase))
            return Error(StatusCodes.Status400BadRequest, "missing_metadata_header");

        if (ContainsIdentitySelector(request.Query) || ContainsIdentitySelector(request.Form))
            return Error(StatusCodes.Status400BadRequest, "unsupported_identity_selector");

        var resource = Value(request.Form, "resource") ?? Value(request.Query, "resource");
        if (string.IsNullOrWhiteSpace(resource))
            return Error(StatusCodes.Status400BadRequest, "missing_resource");
        if (resource.Length > 2048)
            return Error(StatusCodes.Status400BadRequest, "invalid_resource");
        try
        {
            _ = AadAuth.ResourceToScope(resource);
        }
        catch (InvalidInputException)
        {
            return Error(StatusCodes.Status400BadRequest, "invalid_resource");
        }

        try
        {
            var token = await tokenProvider.AcquireAsync(resource, cancellationToken).ConfigureAwait(false);
            var now = DateTimeOffset.UtcNow;
            var expiresIn = Math.Max(0, (long)Math.Floor((token.ExpiresOn - now).TotalSeconds));
            var body = new TokenResponse(
                token.AccessToken,
                "",
                expiresIn.ToString(CultureInfo.InvariantCulture),
                token.ExpiresOn.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
                token.Resource,
                token.TokenType);
            return new TokenHttpResult(StatusCodes.Status200OK, body);
        }
        catch (NoAuthenticatedAccountException)
        {
            return Error(StatusCodes.Status401Unauthorized, "not_authenticated");
        }
        catch (MsalUiRequiredException ex)
        {
            logger.LogWarning("Token acquisition requires user interaction. MSAL error {ErrorCode}; correlation {CorrelationId}.", ex.ErrorCode, ex.CorrelationId);
            return Error(
                StatusCodes.Status401Unauthorized,
                "interaction_required",
                ex.ErrorCode,
                SafeDescription($"{ex.Message} Reauthenticate with: agentbox auth aad login --resource <resource-uri>"),
                ex.CorrelationId?.ToString());
        }
        catch (MsalException ex)
        {
            logger.LogWarning("Token acquisition failed with MSAL error {ErrorCode}; correlation {CorrelationId}.", ex.ErrorCode, ex.CorrelationId);
            return Error(
                StatusCodes.Status500InternalServerError,
                "token_acquisition_failed",
                ex.ErrorCode,
                SafeDescription(ex.Message),
                ex.CorrelationId?.ToString());
        }
        catch (Exception ex)
        {
            logger.LogError("Unexpected token acquisition failure: {ErrorType}.", ex.GetType().Name);
            return Error(StatusCodes.Status500InternalServerError, "token_acquisition_failed");
        }
    }

    private static bool ContainsIdentitySelector(IReadOnlyDictionary<string, string?> values) =>
        values.Keys.Any(IdentitySelectors.Contains);

    private static string? Value(IReadOnlyDictionary<string, string?> values, string key) =>
        values.FirstOrDefault(pair => string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase)).Value;

    private static TokenHttpResult Error(
        int statusCode,
        string error,
        string? errorCode = null,
        string? description = null,
        string? correlationId = null) =>
        new(statusCode, new TokenErrorResponse(error, errorCode, description, correlationId));

    private static string SafeDescription(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return "";
        var safe = Regex.Replace(
            message,
            @"(?i)\b(access_token|refresh_token|client_secret|device_code|user_code)(\s*[:=]\s*)[^\s&,;]+",
            "$1$2[redacted]");
        safe = Regex.Replace(safe, @"(?i)\bBearer\s+\S+", "Bearer [redacted]");
        safe = new string(safe.Select(c => char.IsControl(c) ? ' ' : c).Take(1024).ToArray());
        return Regex.Replace(safe, @"\s+", " ").Trim();
    }
}

using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Identity.Client;
using TALXIS.DevKit.AgentBox.CLI.Auth;
using TALXIS.DevKit.AgentBox.CLI.Cli;
using TALXIS.DevKit.AgentBox.CLI.Host;

namespace TALXIS.DevKit.AgentBox.CLI.Tests;

public class AuthTests
{
    [Theory]
    [InlineData("https://management.azure.com/", "https://management.azure.com//.default")]
    [InlineData("https://graph.microsoft.com", "https://graph.microsoft.com/.default")]
    [InlineData("api://contoso/resource/", "api://contoso/resource//.default")]
    public void Resource_to_scope_preserves_the_resource_identifier(string resource, string expected) =>
        Assert.Equal(expected, AadAuth.ResourceToScope(resource));

    [Fact]
    public void Default_login_requests_the_ARM_default_scope() =>
        Assert.Equal("https://management.azure.com//.default", AadAuth.ScopeForLogin(null));

    [Theory]
    [InlineData("")]
    [InlineData("not a URI")]
    [InlineData("https://graph.microsoft.com/.default")]
    public void Invalid_resources_are_rejected(string resource) =>
        Assert.Throws<InvalidInputException>(() => AadAuth.ResourceToScope(resource));

    [Fact]
    public void Active_account_store_writes_and_reads_a_single_atomic_pointer()
    {
        var home = Directory.CreateTempSubdirectory("agentbox-auth-").FullName;
        var store = new ActiveAccountStore(home);
        var account = new ActiveAccount("home-id", "user@example.com", "tenant-id");

        Assert.Null(store.Read());
        store.Write(account);

        Assert.Equal(account, store.Read());
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(store.FilePath)!, "*.tmp"));
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(
                UnixFileMode.UserRead | UnixFileMode.UserWrite,
                File.GetUnixFileMode(store.FilePath));
            Assert.Equal(
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
                File.GetUnixFileMode(Path.GetDirectoryName(store.FilePath)!));
        }
    }

    [Fact]
    public async Task Token_endpoint_requires_metadata_and_resource()
    {
        var provider = new FakeTokenProvider();
        var handler = new TokenRequestHandler(provider, NullLogger.Instance);

        var missingHeader = await handler.HandleAsync(Request(null), CancellationToken.None);
        var missingResource = await handler.HandleAsync(Request("true"), CancellationToken.None);

        AssertError(missingHeader, 400, "missing_metadata_header");
        AssertError(missingResource, 400, "missing_resource");
        Assert.Equal(0, provider.Calls);
    }

    [Theory]
    [InlineData("client_id")]
    [InlineData("clientid")]
    [InlineData("object_id")]
    [InlineData("principal_id")]
    [InlineData("msi_res_id")]
    [InlineData("mi_res_id")]
    public async Task Token_endpoint_rejects_identity_selectors_instead_of_substituting_the_user(string selector)
    {
        var provider = new FakeTokenProvider();
        var request = new TokenRequest("true",
            new Dictionary<string, string?> { ["resource"] = "https://management.azure.com/", [selector] = "someone-else" },
            new Dictionary<string, string?>());

        var result = await new TokenRequestHandler(provider, NullLogger.Instance).HandleAsync(request, CancellationToken.None);

        AssertError(result, 400, "unsupported_identity_selector");
        Assert.Equal(0, provider.Calls);
    }

    [Fact]
    public async Task Token_endpoint_returns_the_MI_response_shape_without_inventing_not_before()
    {
        var provider = new FakeTokenProvider();
        var request = new TokenRequest(
            "true",
            new Dictionary<string, string?>(),
            new Dictionary<string, string?> { ["resource"] = "https://management.azure.com/" });

        var result = await new TokenRequestHandler(provider, NullLogger.Instance).HandleAsync(request, CancellationToken.None);

        Assert.Equal(200, result.StatusCode);
        var body = Assert.IsType<TokenResponse>(result.Body);
        Assert.Equal("synthetic-access-token", body.AccessToken);
        Assert.Equal("", body.RefreshToken);
        Assert.False(string.IsNullOrWhiteSpace(body.ExpiresIn));
        Assert.Matches(@"^\d+$", body.ExpiresIn);
        Assert.Equal("https://management.azure.com/", body.Resource);
        Assert.Equal("Bearer", body.TokenType);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(body));
        Assert.False(json.RootElement.TryGetProperty("not_before", out _));
    }

    [Fact]
    public async Task Token_endpoint_returns_not_authenticated_when_no_active_account_exists()
    {
        var provider = new FakeTokenProvider { Failure = new NoAuthenticatedAccountException() };
        var result = await new TokenRequestHandler(provider, NullLogger.Instance)
            .HandleAsync(Request("true", "https://management.azure.com/"), CancellationToken.None);

        AssertError(result, 401, "not_authenticated");
    }

    [Fact]
    public async Task Token_endpoint_distinguishes_interaction_required_and_sanitizes_the_description()
    {
        var provider = new FakeTokenProvider
        {
            Failure = new MsalUiRequiredException(
                "interaction_required",
                "Consent required\r\nfor this resource access_token=secret Bearer hidden"),
        };
        var request = Request("true", "https://graph.microsoft.com/");

        var result = await new TokenRequestHandler(provider, NullLogger.Instance).HandleAsync(request, CancellationToken.None);

        Assert.Equal(401, result.StatusCode);
        var error = Assert.IsType<TokenErrorResponse>(result.Body);
        Assert.Equal("interaction_required", error.Error);
        Assert.Equal("interaction_required", error.ErrorCode);
        Assert.Equal(
            "Consent required for this resource access_token=[redacted] Bearer [redacted] Reauthenticate with: agentbox auth aad login --resource <resource-uri>",
            error.ErrorDescription);
    }

    [Fact]
    public async Task Sidecar_status_and_stop_are_safe_when_no_pid_file_exists()
    {
        var host = new FakeHost();
        var auth = new AuthService(host, new FakeProcesses(), NullLogger.Instance);
        var lifecycle = new SidecarLifecycle(host, auth, NullLogger.Instance);

        Assert.False(await lifecycle.StopAsync(CancellationToken.None));
        var status = await lifecycle.StatusAsync(CancellationToken.None);

        Assert.False(status.Running);
        Assert.Null(status.Pid);
        Assert.False(status.Authenticated);
    }

    [Fact]
    public async Task Sidecar_status_removes_a_stale_pid_without_claiming_running()
    {
        var host = new FakeHost();
        var home = host.HomeDirectory;
        SidecarFiles.Write(home, new SidecarProcessInfo(int.MaxValue, 0, "missing-agentbox", "stale"));
        var lifecycle = new SidecarLifecycle(host, new AuthService(host, new FakeProcesses(), NullLogger.Instance), NullLogger.Instance);

        var status = await lifecycle.StatusAsync(CancellationToken.None);

        Assert.False(status.Running);
        Assert.False(File.Exists(SidecarFiles.PidPath(home)));
    }

    private static TokenRequest Request(string? metadata, string? resource = null) =>
        new(metadata,
            resource is null ? new Dictionary<string, string?>() : new Dictionary<string, string?> { ["resource"] = resource },
            new Dictionary<string, string?>());

    private static void AssertError(TokenHttpResult result, int statusCode, string error)
    {
        Assert.Equal(statusCode, result.StatusCode);
        Assert.Equal(error, Assert.IsType<TokenErrorResponse>(result.Body).Error);
    }

    private sealed class FakeTokenProvider : ITokenProvider
    {
        public int Calls { get; private set; }
        public Exception? Failure { get; init; }

        public Task<IdentityToken> AcquireAsync(string resource, CancellationToken cancellationToken)
        {
            Calls++;
            if (Failure is not null)
                return Task.FromException<IdentityToken>(Failure);
            return Task.FromResult(new IdentityToken(
                "synthetic-access-token",
                DateTimeOffset.UtcNow.AddMinutes(30),
                resource,
                "Bearer",
                "correlation"));
        }
    }
}

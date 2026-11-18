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
    [InlineData("00000003-0000-0000-C000-000000000000", "00000003-0000-0000-C000-000000000000/.default")]
    public void Resource_to_scope_preserves_the_resource_identifier(string resource, string expected)
    {
        Assert.Equal(expected, AadAuth.ResourceToScope(resource));
        Assert.Equal(expected, AadAuth.ScopeForLogin(resource));
    }

    [Fact]
    public void Default_login_requests_the_ARM_default_scope() =>
        Assert.Equal("https://management.azure.com//.default", AadAuth.ScopeForLogin(null));

    [Theory]
    [InlineData("")]
    [InlineData("not a URI")]
    [InlineData("00000003-0000-0000-c000-00000000000z")]
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
    public async Task Token_endpoint_requires_a_supported_header_and_resource()
    {
        var provider = new FakeTokenProvider();
        var handler = new TokenRequestHandler(provider, NullLogger.Instance);

        var missingHeader = await handler.HandleAsync(Request(null), CancellationToken.None);
        var missingResource = await handler.HandleAsync(Request("true"), CancellationToken.None);
        var missingResourceWithSecret = await handler.HandleAsync(Request(null, secret: "anything"), CancellationToken.None);

        AssertError(missingHeader, 400, "missing_metadata_header");
        AssertError(missingResource, 400, "missing_resource");
        AssertError(missingResourceWithSecret, 400, "missing_resource");
        Assert.Equal(0, provider.Calls);
    }

    [Theory]
    [InlineData("true", null, true)]
    [InlineData("TRUE", null, true)]
    [InlineData(null, "anything", true)]
    [InlineData(null, "another value", true)]
    [InlineData("false", "anything", true)]
    [InlineData("true", "", true)]
    [InlineData(null, null, false)]
    [InlineData(null, "", false)]
    [InlineData("false", "  ", false)]
    [InlineData("false", null, false)]
    public async Task Token_endpoint_accepts_metadata_true_or_any_nonempty_secret(string? metadata, string? secret, bool accepted)
    {
        var provider = new FakeTokenProvider();
        var result = await new TokenRequestHandler(provider, NullLogger.Instance)
            .HandleAsync(Request(metadata, "https://management.azure.com/", secret), CancellationToken.None);

        if (accepted)
            Assert.Equal(200, result.StatusCode);
        else
            AssertError(result, 400, "missing_metadata_header");
        Assert.Equal(accepted ? 1 : 0, provider.Calls);
    }

    [Theory]
    [InlineData("client_id")]
    [InlineData("clientid")]
    [InlineData("object_id")]
    [InlineData("principal_id")]
    [InlineData("msi_res_id")]
    [InlineData("mi_res_id")]
    public async Task Token_endpoint_ignores_identity_selectors_and_uses_the_signed_in_user(string selector)
    {
        var provider = new FakeTokenProvider();
        var request = new TokenRequest("true",
            new Dictionary<string, string?> { ["resource"] = "https://management.azure.com/", [selector] = "someone-else" },
            new Dictionary<string, string?>());

        var result = await new TokenRequestHandler(provider, NullLogger.Instance).HandleAsync(request, CancellationToken.None);

        Assert.Equal(200, result.StatusCode);
        Assert.Equal(1, provider.Calls);
    }

    [Fact]
    public async Task Token_endpoint_ignores_client_id_when_using_secret_header()
    {
        var provider = new FakeTokenProvider();
        var request = new TokenRequest(null,
            new Dictionary<string, string?> { ["resource"] = "https://management.azure.com/", ["client_id"] = "someone-else" },
            new Dictionary<string, string?>(),
            "anything");

        var result = await new TokenRequestHandler(provider, NullLogger.Instance).HandleAsync(request, CancellationToken.None);

        Assert.Equal(200, result.StatusCode);
        Assert.Equal(1, provider.Calls);
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Token_endpoint_passes_guid_resource_through_unchanged(bool inForm)
    {
        const string resource = "00000003-0000-0000-C000-000000000000";
        var values = new Dictionary<string, string?> { ["resource"] = resource };
        var empty = new Dictionary<string, string?>();
        var request = new TokenRequest("true", inForm ? empty : values, inForm ? values : empty);
        var provider = new FakeTokenProvider();

        var result = await new TokenRequestHandler(provider, NullLogger.Instance).HandleAsync(request, CancellationToken.None);

        Assert.Equal(200, result.StatusCode);
        Assert.Equal(resource, Assert.IsType<TokenResponse>(result.Body).Resource);
        Assert.Equal(1, provider.Calls);
    }

    [Fact]
    public async Task Token_endpoint_rejects_malformed_guid_resource()
    {
        var provider = new FakeTokenProvider();
        var result = await new TokenRequestHandler(provider, NullLogger.Instance)
            .HandleAsync(Request("true", "00000003-0000-0000-c000-00000000000z"), CancellationToken.None);

        AssertError(result, 400, "invalid_resource");
        Assert.Equal(0, provider.Calls);
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
            "Consent required for this resource access_token=[redacted] Bearer [redacted] Reauthenticate with: agentbox auth aad login --resource <resource>",
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

    private static TokenRequest Request(string? metadata, string? resource = null, string? secret = null) =>
        new(metadata,
            resource is null ? new Dictionary<string, string?>() : new Dictionary<string, string?> { ["resource"] = resource },
            new Dictionary<string, string?>(),
            secret);

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

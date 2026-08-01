using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using SwebKit.Core.Abstractions;
using SwebKit.Sidecar.Endpoints;

namespace SwebKit.Sidecar.Tests;

/// <summary>Controllable resolver double for exercising the preview endpoint's response mapping.</summary>
internal sealed class FakeKeyVaultSecretResolver : IKeyVaultSecretResolver
{
    private readonly string? _secretValue;

    public FakeKeyVaultSecretResolver(bool isAvailable, string? secretValue = null)
    {
        IsAvailable = isAvailable;
        _secretValue = secretValue;
    }

    public bool IsAvailable { get; }

    public Task<string> GetSecretAsync(string secretName, string? vaultName = null, CancellationToken cancellationToken = default)
        => Task.FromResult(_secretValue ?? $"[KV_ERROR:{secretName}]");
}

public class ApiClientEndpointsPreviewTests
{
    [Fact]
    public async Task MissingSecretName_ReturnsBadRequest()
    {
        var resolver = new FakeKeyVaultSecretResolver(isAvailable: true);
        var req = new PreviewKeyVaultSecretRequest(null, "   ");

        var result = await ApiClientEndpoints.PreviewKeyVaultSecretAsync(req, resolver, CancellationToken.None);

        // The body is an anonymous type (Results.BadRequest(new { error = ... })), so assert via the
        // status-code interface rather than the unnameable generic BadRequest<T>.
        var statusResult = Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
        Assert.Equal(400, statusResult.StatusCode);
    }

    [Fact]
    public async Task NoVaultsConfigured_ReturnsProblem()
    {
        var resolver = new FakeKeyVaultSecretResolver(isAvailable: false);
        var req = new PreviewKeyVaultSecretRequest(null, "my-secret");

        var result = await ApiClientEndpoints.PreviewKeyVaultSecretAsync(req, resolver, CancellationToken.None);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal("No key vaults are configured", problem.ProblemDetails.Detail);
    }

    [Fact]
    public async Task SecretFetchFails_ReturnsErrorStatus_WithoutMaskedValue()
    {
        var resolver = new FakeKeyVaultSecretResolver(isAvailable: true, secretValue: "[KV_ERROR:my-secret]");
        var req = new PreviewKeyVaultSecretRequest("kv1", "my-secret");

        var result = await ApiClientEndpoints.PreviewKeyVaultSecretAsync(req, resolver, CancellationToken.None);

        var ok = Assert.IsType<Ok<KeyVaultPreviewResponse>>(result);
        Assert.Equal("error", ok.Value!.Status);
        Assert.Null(ok.Value.MaskedValue);
        Assert.Equal("[KV_ERROR:my-secret]", ok.Value.Error);
    }

    [Fact]
    public async Task SecretUnavailable_ReturnsErrorStatus()
    {
        var resolver = new FakeKeyVaultSecretResolver(isAvailable: true, secretValue: "[KV_UNAVAILABLE:my-secret]");
        var req = new PreviewKeyVaultSecretRequest("kv1", "my-secret");

        var result = await ApiClientEndpoints.PreviewKeyVaultSecretAsync(req, resolver, CancellationToken.None);

        var ok = Assert.IsType<Ok<KeyVaultPreviewResponse>>(result);
        Assert.Equal("error", ok.Value!.Status);
    }

    [Fact]
    public async Task SecretPresent_ReturnsOkStatus_WithMaskedValue_NeverTheRawSecret()
    {
        var resolver = new FakeKeyVaultSecretResolver(isAvailable: true, secretValue: "super-secret-value");
        var req = new PreviewKeyVaultSecretRequest("kv1", "my-secret");

        var result = await ApiClientEndpoints.PreviewKeyVaultSecretAsync(req, resolver, CancellationToken.None);

        var ok = Assert.IsType<Ok<KeyVaultPreviewResponse>>(result);
        Assert.Equal("ok", ok.Value!.Status);
        Assert.Null(ok.Value.Error);
        Assert.NotNull(ok.Value.MaskedValue);
        Assert.DoesNotContain("super-secret-value", ok.Value.MaskedValue);
    }

    [Theory]
    [InlineData("", 0)]
    [InlineData("ab", 4)]
    [InlineData("a-normal-length-secret", 16)]
    [InlineData("a-very-very-very-very-long-secret-value-indeed", 16)]
    public void MaskSecret_ClampsDotCountInsteadOfExposingExactLength(string value, int expectedDots)
    {
        var masked = ApiClientEndpoints.MaskSecret(value);

        Assert.Equal(expectedDots, masked.Length);
    }
}

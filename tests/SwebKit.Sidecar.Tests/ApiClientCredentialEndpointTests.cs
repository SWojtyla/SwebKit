using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using SwebKit.Sidecar.Endpoints;

namespace SwebKit.Sidecar.Tests;

/// <summary>
/// Coverage for the environment-variable "Secret Store" endpoints. These are the only in-app
/// write path into the OS credential store that <c>VariableSubstitutionService</c> resolves
/// WindowsCredentialStore variables from — the endpoints must save, delete, and preview
/// (masked, never raw) against the same <c>ICredentialStore</c>.
/// </summary>
public class ApiClientCredentialEndpointTests
{
    [Fact]
    public void SaveCredential_StoresValueUnderKey()
    {
        var store = new FakeCredentialStore();

        var result = ApiClientEndpoints.SaveCredential(new SaveCredentialRequest("my-key", "s3cret"), store);

        Assert.Equal(200, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
        Assert.Equal("s3cret", store.Get("my-key"));
    }

    [Fact]
    public void SaveCredential_BlankKey_ReturnsBadRequest()
    {
        var store = new FakeCredentialStore();

        var result = ApiClientEndpoints.SaveCredential(new SaveCredentialRequest("  ", "x"), store);

        Assert.Equal(400, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
        Assert.Null(store.Get("  "));
    }

    [Fact]
    public void DeleteCredential_RemovesValue()
    {
        var store = new FakeCredentialStore();
        store.Save("my-key", "s3cret");

        var result = ApiClientEndpoints.DeleteCredential("my-key", store);

        Assert.Equal(200, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
        Assert.Null(store.Get("my-key"));
    }

    [Fact]
    public void PreviewCredential_MissingKey_ReturnsErrorStatus()
    {
        var store = new FakeCredentialStore();

        var result = ApiClientEndpoints.PreviewCredential(new PreviewCredentialRequest("nope"), store);

        var ok = Assert.IsType<Ok<KeyVaultPreviewResponse>>(result);
        Assert.Equal("error", ok.Value!.Status);
        Assert.Null(ok.Value.MaskedValue);
        Assert.NotNull(ok.Value.Error);
    }

    [Fact]
    public void PreviewCredential_PresentKey_ReturnsMasked_NeverRaw()
    {
        var store = new FakeCredentialStore();
        store.Save("my-key", "super-secret-value");

        var result = ApiClientEndpoints.PreviewCredential(new PreviewCredentialRequest("my-key"), store);

        var ok = Assert.IsType<Ok<KeyVaultPreviewResponse>>(result);
        Assert.Equal("ok", ok.Value!.Status);
        Assert.NotNull(ok.Value.MaskedValue);
        Assert.DoesNotContain("super-secret-value", ok.Value.MaskedValue);
        Assert.DoesNotContain("super-secret-value", System.Text.Json.JsonSerializer.Serialize(ok.Value));
    }

    [Fact]
    public void PreviewCredential_BlankKey_ReturnsBadRequest()
    {
        var store = new FakeCredentialStore();

        var result = ApiClientEndpoints.PreviewCredential(new PreviewCredentialRequest(""), store);

        Assert.Equal(400, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
    }
}

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using SwebKit.Core.Domain;
using SwebKit.Core.Services;
using SwebKit.Sidecar.Endpoints;

namespace SwebKit.Sidecar.Tests;

/// <summary>
/// Thin-adapter coverage for POST /api/api-client/import-curl — parsing itself is covered
/// in Core tests; here we only verify the success/error envelope the dialog consumes.
/// </summary>
public class ApiClientImportCurlEndpointTests
{
    private static ApiClientWorkflowService CreateWorkflow() => new(
        new VariableSubstitutionService(
            new FakeCredentialStore(),
            new FakeKeyVaultSecretResolver(isAvailable: false)));

    [Fact]
    public void ValidCurl_ReturnsParsedRequest()
    {
        var req = new ImportCurlRequest(
            "curl -X POST 'https://api.example.com/orders?p=1' -H 'Content-Type: application/json' --data-raw '{\"a\":1}'");

        var result = ApiClientEndpoints.ImportCurl(req, CreateWorkflow());

        var ok = Assert.IsType<Ok<HttpRequestEntry>>(result);
        Assert.Equal(ApiRequestMethod.Post, ok.Value!.Method);
        Assert.StartsWith("https://api.example.com/orders", ok.Value.Url);
    }

    [Fact]
    public void MalformedCurl_ReturnsErrorEnvelope()
    {
        var result = ApiClientEndpoints.ImportCurl(new ImportCurlRequest("curl -X"), CreateWorkflow());

        Assert.Equal(400, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
        Assert.Equal("cURL request method is missing.",
            Assert.IsType<ApiErrorResponse>(Assert.IsAssignableFrom<IValueHttpResult>(result).Value).Error);
    }

    [Fact]
    public void NullCommand_ReturnsBadRequest()
    {
        var result = ApiClientEndpoints.ImportCurl(new ImportCurlRequest(null), CreateWorkflow());

        Assert.Equal(400, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
    }
}

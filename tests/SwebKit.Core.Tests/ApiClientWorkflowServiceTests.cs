using SwebKit.Core.Domain;
using SwebKit.Core.Services;

namespace SwebKit.Core.Tests;

public sealed class ApiClientWorkflowServiceTests
{
    [Fact]
    public async Task BuildCurlAsync_MasksSecretBackedValues()
    {
        var creds = new StubCredentialStore();
        creds.Save("api-token", "real-secret-token");
        var service = Create(creds);
        var collection = new ApiCollection
        {
            Variables = [new CollectionVariable { Key = "baseUrl", Value = "https://api.example.com" }],
        };
        var environment = new ApiEnvironment
        {
            Variables =
            [
                new EnvironmentVariable
                {
                    Key = "token",
                    CredentialKey = "api-token",
                    SecretSource = EnvironmentVariableSecretSource.WindowsCredentialStore,
                },
            ],
        };
        var request = new HttpRequestEntry
        {
            Method = ApiRequestMethod.Get,
            Url = "{{baseUrl}}/orders",
            Headers = [new KeyValuePair<string> { Key = "Authorization", Value = "Bearer {{token}}" }],
        };

        var curl = await service.BuildCurlAsync(request, collection, environment);

        Assert.Contains("https://api.example.com/orders", curl);
        Assert.Contains("Bearer ********", curl);
        Assert.DoesNotContain("real-secret-token", curl);
    }

    [Fact]
    public void ImportCurl_MapsMethodUrlHeadersAndBody()
    {
        var result = Create().ImportCurl("curl -X POST https://api.example.com/orders -H 'Content-Type: application/json' --data-raw '{\"id\":1}'");

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.NotNull(result.Request);
        var request = result.Request;
        Assert.Equal(ApiRequestMethod.Post, request.Method);
        Assert.Equal("https://api.example.com/orders", request.Url);
        Assert.Contains(request.Headers, header => header.Key == "Content-Type" && header.Value == "application/json");
        Assert.Equal(RequestBodyMode.Json, request.Body.Mode);
        Assert.Equal("{\"id\":1}", request.Body.RawContent);
    }

    [Fact]
    public async Task InspectVariablesAsync_ReturnsSourceAndMaskedSecret()
    {
        var creds = new StubCredentialStore();
        creds.Save("api-token", "real-secret-token");
        var service = Create(creds);
        var collection = new ApiCollection
        {
            Variables = [new CollectionVariable { Key = "baseUrl", Value = "https://api.example.com" }],
        };
        var environment = new ApiEnvironment
        {
            Variables =
            [
                new EnvironmentVariable
                {
                    Key = "token",
                    CredentialKey = "api-token",
                    SecretSource = EnvironmentVariableSecretSource.WindowsCredentialStore,
                },
            ],
        };
        var request = new HttpRequestEntry
        {
            Url = "{{baseUrl}}/orders/{{missing}}",
            Headers = [new KeyValuePair<string> { Key = "Authorization", Value = "Bearer {{token}}" }],
        };

        var items = await service.InspectVariablesAsync(request, collection, environment);

        Assert.Contains(items, item => item.Key == "baseUrl" && item.Source == VariableInspectionSource.Collection && item.DisplayValue == "https://api.example.com");
        Assert.Contains(items, item => item.Key == "token" && item.Source == VariableInspectionSource.CredentialStore && item.DisplayValue == "********" && item.IsSecret);
        Assert.Contains(items, item => item.Key == "missing" && item.Source == VariableInspectionSource.Unresolved && !item.IsResolved);
    }

    [Fact]
    public void CreateResponseExample_ScrubsSecretHeadersAndJsonProperties()
    {
        var result = new HttpRequestResult
        {
            StatusCode = 200,
            StatusText = "200 OK",
            ContentType = "application/json",
            ResponseHeaders = [("Authorization", "Bearer secret")],
            ResponseBody = "{\"access_token\":\"secret\",\"name\":\"ok\"}",
        };

        var example = Create().CreateResponseExample(result, "Example", "dev");

        Assert.Equal("Example", example.Name);
        Assert.Contains(example.Headers, header => header.Key == "Authorization" && header.Value == "********");
        Assert.Contains("********", example.Body);
        Assert.DoesNotContain("\"secret\"", example.Body);
        Assert.Contains("\"name\": \"ok\"", example.Body);
    }

    private static ApiClientWorkflowService Create(StubCredentialStore? creds = null)
    {
        var substitution = new VariableSubstitutionService(creds ?? new StubCredentialStore(), new StubKeyVaultResolver(available: false));
        return new ApiClientWorkflowService(substitution);
    }
}

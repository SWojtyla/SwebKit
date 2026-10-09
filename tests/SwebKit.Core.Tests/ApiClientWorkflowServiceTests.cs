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
        var request = Assert.Single(result.Requests);
        Assert.Equal(ApiRequestMethod.Post, request.Method);
        Assert.Equal("https://api.example.com/orders", request.Url);
        Assert.Contains(request.Headers, header => header.Key == "Content-Type" && header.Value == "application/json");
        Assert.Equal(RequestBodyMode.Json, request.Body.Mode);
        Assert.Equal("{\"id\":1}", request.Body.RawContent);
    }

    [Fact]
    public void ImportCurl_UserFlag_MapsToBasicAuth()
    {
        var result = Create().ImportCurl("curl -u alice:s3cret https://api.example.com/me");

        Assert.True(result.IsSuccess, result.ErrorMessage);
        var auth = Assert.Single(result.Requests).Auth;
        Assert.NotNull(auth);
        Assert.Equal(AuthType.Basic, auth.Type);
        Assert.Equal("alice", auth.BasicUsername);
        Assert.Equal("s3cret", auth.CredentialKey);
    }

    [Fact]
    public void ImportCurl_UserFlagWithoutPassword_MapsUsernameOnly()
    {
        var result = Create().ImportCurl("curl --user alice https://api.example.com/me");

        Assert.True(result.IsSuccess, result.ErrorMessage);
        var request = Assert.Single(result.Requests);
        Assert.Equal("alice", request.Auth!.BasicUsername);
        Assert.Equal(string.Empty, request.Auth.CredentialKey);
    }

    [Fact]
    public void ImportCurl_MultipleCommandsSeparatedByAndAnd_ReturnsRequestPerCommand()
    {
        var result = Create().ImportCurl(
            "curl https://api.example.com/orders && curl -X POST https://api.example.com/users");

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(2, result.Requests.Count);
        Assert.Equal(ApiRequestMethod.Get, result.Requests[0].Method);
        Assert.Equal("https://api.example.com/orders", result.Requests[0].Url);
        Assert.Equal(ApiRequestMethod.Post, result.Requests[1].Method);
        Assert.Equal("https://api.example.com/users", result.Requests[1].Url);
    }

    [Fact]
    public void ImportCurl_MultipleCommandsSeparatedByNewlineAndSemicolon_ReturnsRequestPerCommand()
    {
        var result = Create().ImportCurl(
            "curl https://api.example.com/one\ncurl https://api.example.com/two ; curl -I https://api.example.com/three");

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(3, result.Requests.Count);
        Assert.Equal("https://api.example.com/one", result.Requests[0].Url);
        Assert.Equal("https://api.example.com/two", result.Requests[1].Url);
        Assert.Equal(ApiRequestMethod.Head, result.Requests[2].Method);
    }

    [Fact]
    public void ImportCurl_MultipleCommands_DuplicateNamesGetSuffix()
    {
        var result = Create().ImportCurl(
            "curl https://api.example.com/item && curl https://api.example.com/item");

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(2, result.Requests.Count);
        Assert.Equal("item", result.Requests[0].Name);
        Assert.Equal("item (2)", result.Requests[1].Name);
    }

    [Fact]
    public void ImportCurl_CurlOnlyCountsAsCommandStart_WhenStandaloneToken()
    {
        var result = Create().ImportCurl(
            "curl https://api.example.com/one\nscurl https://api.example.com/two");

        Assert.True(result.IsSuccess, result.ErrorMessage);
        // No second `curl` token → "scurl https://.../two" continues the first command;
        // the later URL wins, matching single-command behavior.
        var request = Assert.Single(result.Requests);
        Assert.Equal("https://api.example.com/two", request.Url);
    }

    [Fact]
    public void ImportCurl_BackslashContinuation_JoinsLines()
    {
        var result = Create().ImportCurl(
            "curl https://api.example.com/orders \\\r\n  -H \"X-Api-Key: abc\" \\\n  --data-raw '{\"id\":1}'");

        Assert.True(result.IsSuccess, result.ErrorMessage);
        var request = Assert.Single(result.Requests);
        Assert.Equal(ApiRequestMethod.Post, request.Method);
        Assert.Contains(request.Headers, header => header.Key == "X-Api-Key" && header.Value == "abc");
        Assert.Equal("{\"id\":1}", request.Body.RawContent);
    }

    [Fact]
    public void ImportCurl_CaretContinuation_JoinsLines()
    {
        var result = Create().ImportCurl(
            "curl https://api.example.com/orders ^\r\n  -H \"X-Api-Key: abc\" ^\r\n  -X POST");

        Assert.True(result.IsSuccess, result.ErrorMessage);
        var request = Assert.Single(result.Requests);
        Assert.Equal(ApiRequestMethod.Post, request.Method);
        Assert.Contains(request.Headers, header => header.Key == "X-Api-Key" && header.Value == "abc");
    }

    [Fact]
    public void ImportCurl_CaretInsideQuotes_IsPreserved()
    {
        var result = Create().ImportCurl(
            "curl -H \"X-Note: a^\r\nb\" https://api.example.com");

        Assert.True(result.IsSuccess, result.ErrorMessage);
        var request = Assert.Single(result.Requests);
        var header = Assert.Single(request.Headers);
        Assert.Equal("X-Note", header.Key);
        Assert.Equal("a^\r\nb", header.Value);
    }

    [Fact]
    public void ImportCurl_FormFields_MapToFormDataBody()
    {
        var result = Create().ImportCurl(
            "curl -F 'name=value' -F 'avatar=@/tmp/a.png' https://api.example.com/upload");

        Assert.True(result.IsSuccess, result.ErrorMessage);
        var request = Assert.Single(result.Requests);
        Assert.Equal(ApiRequestMethod.Post, request.Method);
        Assert.Equal(RequestBodyMode.FormData, request.Body.Mode);
        Assert.Equal(2, request.Body.FormData.Count);
        Assert.Equal("name", request.Body.FormData[0].Key);
        Assert.Equal("value", request.Body.FormData[0].Value);
        Assert.False(request.Body.FormData[0].IsFile);
        Assert.Equal("avatar", request.Body.FormData[1].Key);
        Assert.Equal("/tmp/a.png", request.Body.FormData[1].Value);
        Assert.True(request.Body.FormData[1].IsFile);
    }

    [Fact]
    public void ImportCurl_HeaderShortcuts_MapToHeaders()
    {
        var result = Create().ImportCurl(
            "curl -b 'session=abc' -A 'TestAgent/1.0' -e 'https://ref.example/page' https://api.example.com");

        Assert.True(result.IsSuccess, result.ErrorMessage);
        var request = Assert.Single(result.Requests);
        Assert.Contains(request.Headers, header => header.Key == "Cookie" && header.Value == "session=abc");
        Assert.Contains(request.Headers, header => header.Key == "User-Agent" && header.Value == "TestAgent/1.0");
        Assert.Contains(request.Headers, header => header.Key == "Referer" && header.Value == "https://ref.example/page");
    }

    [Fact]
    public void ImportCurl_Insecure_AddsWarning()
    {
        var result = Create().ImportCurl("curl -k https://api.example.com");

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Single(result.Requests);
        Assert.Contains(result.Warnings, warning => warning.Contains("-k/--insecure"));
    }

    [Fact]
    public void ImportCurl_UnknownFlag_AddsWarning_AndKeepsUrl()
    {
        var result = Create().ImportCurl("curl --frobnicate https://api.example.com/orders");

        Assert.True(result.IsSuccess, result.ErrorMessage);
        var request = Assert.Single(result.Requests);
        Assert.Equal("https://api.example.com/orders", request.Url);
        Assert.Contains("Ignored cURL flag: --frobnicate", result.Warnings);
    }

    [Fact]
    public void ImportCurl_UnknownFlag_DoesNotSwallowUrlValue()
    {
        var result = Create().ImportCurl(
            "curl --proxy2 https://proxy.example.com:8080 https://api.example.com/orders");

        Assert.True(result.IsSuccess, result.ErrorMessage);
        var request = Assert.Single(result.Requests);
        Assert.Equal("https://api.example.com/orders", request.Url);
        Assert.Contains("Ignored cURL flag: --proxy2", result.Warnings);
    }

    [Fact]
    public void ImportCurl_KnownIgnorableFlags_ConsumeValuesSilently()
    {
        var result = Create().ImportCurl(
            "curl --max-time 5 -o https://not-the-url.example -sL https://api.example.com/orders");

        Assert.True(result.IsSuccess, result.ErrorMessage);
        var request = Assert.Single(result.Requests);
        Assert.Equal("https://api.example.com/orders", request.Url);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void ImportCurl_WithoutCurlPrefix_ParsesAsSingleCommand()
    {
        var result = Create().ImportCurl("-X PUT https://api.example.com/items/1 -d 'a=1'");

        Assert.True(result.IsSuccess, result.ErrorMessage);
        var request = Assert.Single(result.Requests);
        Assert.Equal(ApiRequestMethod.Put, request.Method);
        Assert.Equal("https://api.example.com/items/1", request.Url);
        Assert.Equal("a=1", request.Body.RawContent);
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

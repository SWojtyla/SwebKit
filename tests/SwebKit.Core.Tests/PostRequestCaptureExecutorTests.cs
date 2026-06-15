using Microsoft.Extensions.Logging.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Core.Services;

namespace SwebKit.Core.Tests;

// ── Factory helper ────────────────────────────────────────────────────────────

file static class CaptureExecutorFactory
{
    public static async Task<(PostRequestCaptureExecutor Executor, CollectionRepository CollRepo, EnvironmentRepository EnvRepo)>
        CreateAsync()
    {
        var collRepo = new CollectionRepository();
        await collRepo.LoadAsync();

        var envRepo = new EnvironmentRepository();
        await envRepo.LoadAsync();

        var executor = new PostRequestCaptureExecutor(
            collRepo,
            envRepo,
            NullLogger<PostRequestCaptureExecutor>.Instance);

        return (executor, collRepo, envRepo);
    }
}

// ── PostRequestCaptureExecutor ────────────────────────────────────────────────

public sealed class PostRequestCaptureExecutorTests
{
    // ── Disabled rules are skipped ─────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_DisabledRule_IsSkipped()
    {
        using var _ = new AppDataSandbox();
        var (executor, collRepo, envRepo) = await CaptureExecutorFactory.CreateAsync();

        var collection = await collRepo.AddCollectionAsync("Test");
        var request = new HttpRequestEntry { Name = "R1", Url = "https://test.io" };
        request.CaptureRules.Add(new CaptureRule
        {
            Source = CaptureSource.StatusCode,
            TargetVariable = "status",
            TargetScope = "collection",
            IsEnabled = false,
        });

        var result = new HttpRequestResult { StatusCode = 200 };
        var warnings = await executor.ExecuteAsync(result, request, collection, null);

        Assert.Empty(warnings);
        Assert.Empty(collection.Variables);
    }

    // ── StatusCode capture ─────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_StatusCode_WritesToCollectionVariable()
    {
        using var _ = new AppDataSandbox();
        var (executor, collRepo, _) = await CaptureExecutorFactory.CreateAsync();

        var collection = await collRepo.AddCollectionAsync("Test");
        var request = new HttpRequestEntry { Name = "R1", Url = "https://test.io" };
        request.CaptureRules.Add(new CaptureRule
        {
            Source = CaptureSource.StatusCode,
            TargetVariable = "last_status",
            TargetScope = "collection",
            IsEnabled = true,
        });

        var result = new HttpRequestResult { StatusCode = 201 };
        var warnings = await executor.ExecuteAsync(result, request, collection, null);

        Assert.Empty(warnings);
        var v = Assert.Single(collection.Variables);
        Assert.Equal("last_status", v.Key);
        Assert.Equal("201", v.Value);
    }

    // ── ResponseHeader capture ────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_ResponseHeader_ExtractsCaseInsensitive()
    {
        using var _ = new AppDataSandbox();
        var (executor, collRepo, _) = await CaptureExecutorFactory.CreateAsync();

        var collection = await collRepo.AddCollectionAsync("Test");
        var request = new HttpRequestEntry { Name = "R1", Url = "https://test.io" };
        request.CaptureRules.Add(new CaptureRule
        {
            Source = CaptureSource.ResponseHeader,
            HeaderName = "Location",
            TargetVariable = "redirect_url",
            TargetScope = "collection",
            IsEnabled = true,
        });

        var result = new HttpRequestResult
        {
            StatusCode = 302,
            ResponseHeaders = [("location", "/api/v2/items/42")],
        };
        var warnings = await executor.ExecuteAsync(result, request, collection, null);

        Assert.Empty(warnings);
        var v = Assert.Single(collection.Variables);
        Assert.Equal("/api/v2/items/42", v.Value);
    }

    [Fact]
    public async Task ExecuteAsync_ResponseHeader_MissingHeader_ReturnsWarning()
    {
        using var _ = new AppDataSandbox();
        var (executor, collRepo, _) = await CaptureExecutorFactory.CreateAsync();

        var collection = await collRepo.AddCollectionAsync("Test");
        var request = new HttpRequestEntry { Name = "R1", Url = "https://test.io" };
        request.CaptureRules.Add(new CaptureRule
        {
            Source = CaptureSource.ResponseHeader,
            HeaderName = "X-Missing-Header",
            TargetVariable = "missing",
            TargetScope = "collection",
            IsEnabled = true,
        });

        var result = new HttpRequestResult { StatusCode = 200, ResponseHeaders = [] };
        var warnings = await executor.ExecuteAsync(result, request, collection, null);

        Assert.Single(warnings);
        Assert.Contains("missing", warnings[0]);
    }

    // ── BodyJsonPath capture ──────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_BodyJsonPath_ExtractsStringValue()
    {
        using var _ = new AppDataSandbox();
        var (executor, collRepo, _) = await CaptureExecutorFactory.CreateAsync();

        var collection = await collRepo.AddCollectionAsync("Test");
        var request = new HttpRequestEntry { Name = "R1", Url = "https://test.io" };
        request.CaptureRules.Add(new CaptureRule
        {
            Source = CaptureSource.BodyJsonPath,
            JsonPath = "$.token",
            TargetVariable = "access_token",
            TargetScope = "collection",
            IsEnabled = true,
        });

        var result = new HttpRequestResult
        {
            StatusCode = 200,
            ResponseBody = """{"token":"abc-123","expires":3600}""",
        };
        var warnings = await executor.ExecuteAsync(result, request, collection, null);

        Assert.Empty(warnings);
        var v = Assert.Single(collection.Variables);
        Assert.Equal("abc-123", v.Value);
    }

    [Fact]
    public async Task ExecuteAsync_BodyJsonPath_NoMatch_ReturnsWarning()
    {
        using var _ = new AppDataSandbox();
        var (executor, collRepo, _) = await CaptureExecutorFactory.CreateAsync();

        var collection = await collRepo.AddCollectionAsync("Test");
        var request = new HttpRequestEntry { Name = "R1", Url = "https://test.io" };
        request.CaptureRules.Add(new CaptureRule
        {
            Source = CaptureSource.BodyJsonPath,
            JsonPath = "$.nonexistent",
            TargetVariable = "missing_token",
            TargetScope = "collection",
            IsEnabled = true,
        });

        var result = new HttpRequestResult
        {
            StatusCode = 200,
            ResponseBody = """{"token":"abc-123"}""",
        };
        var warnings = await executor.ExecuteAsync(result, request, collection, null);

        Assert.Single(warnings);
        Assert.Contains("missing_token", warnings[0]);
    }

    [Fact]
    public async Task ExecuteAsync_BodyJsonPath_InvalidJson_ReturnsWarning()
    {
        using var _ = new AppDataSandbox();
        var (executor, collRepo, _) = await CaptureExecutorFactory.CreateAsync();

        var collection = await collRepo.AddCollectionAsync("Test");
        var request = new HttpRequestEntry { Name = "R1", Url = "https://test.io" };
        request.CaptureRules.Add(new CaptureRule
        {
            Source = CaptureSource.BodyJsonPath,
            JsonPath = "$.token",
            TargetVariable = "token",
            TargetScope = "collection",
            IsEnabled = true,
        });

        var result = new HttpRequestResult { StatusCode = 200, ResponseBody = "not-json" };
        var warnings = await executor.ExecuteAsync(result, request, collection, null);

        Assert.Single(warnings);
    }

    // ── Environment scope write ───────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_EnvironmentScope_WritesToEnvironment()
    {
        using var _ = new AppDataSandbox();
        var (executor, collRepo, envRepo) = await CaptureExecutorFactory.CreateAsync();

        var collection = await collRepo.AddCollectionAsync("Test");
        var env = await envRepo.AddEnvironmentAsync("Staging");

        var request = new HttpRequestEntry { Name = "R1", Url = "https://test.io" };
        request.CaptureRules.Add(new CaptureRule
        {
            Source = CaptureSource.StatusCode,
            TargetVariable = "last_status",
            TargetScope = env.Id,   // ID, not name
            IsEnabled = true,
        });

        var result = new HttpRequestResult { StatusCode = 200 };
        var warnings = await executor.ExecuteAsync(result, request, collection, env);

        Assert.Empty(warnings);
        var v = Assert.Single(env.Variables);
        Assert.Equal("last_status", v.Key);
        Assert.Equal("200", v.Value);
    }

    [Fact]
    public async Task ExecuteAsync_EnvironmentScope_ByName_NoLongerMatches()
    {
        // Regression: before the fix, scope was matched by name and could silently
        // break on rename. Now it matches by ID only.
        using var _ = new AppDataSandbox();
        var (executor, collRepo, envRepo) = await CaptureExecutorFactory.CreateAsync();

        var collection = await collRepo.AddCollectionAsync("Test");
        var env = await envRepo.AddEnvironmentAsync("Staging");

        var request = new HttpRequestEntry { Name = "R1", Url = "https://test.io" };
        request.CaptureRules.Add(new CaptureRule
        {
            Source = CaptureSource.StatusCode,
            TargetVariable = "status",
            TargetScope = "Staging",   // name, not ID — should produce a warning
            IsEnabled = true,
        });

        var result = new HttpRequestResult { StatusCode = 200 };
        var warnings = await executor.ExecuteAsync(result, request, collection, env);

        Assert.Single(warnings);
        Assert.Contains("Staging", warnings[0]);
    }

    [Fact]
    public async Task ExecuteAsync_UnknownScope_ReturnsWarning()
    {
        using var _ = new AppDataSandbox();
        var (executor, collRepo, _) = await CaptureExecutorFactory.CreateAsync();

        var collection = await collRepo.AddCollectionAsync("Test");
        var request = new HttpRequestEntry { Name = "R1", Url = "https://test.io" };
        request.CaptureRules.Add(new CaptureRule
        {
            Source = CaptureSource.StatusCode,
            TargetVariable = "status",
            TargetScope = "UnknownEnv",
            IsEnabled = true,
        });

        var result = new HttpRequestResult { StatusCode = 200 };
        var warnings = await executor.ExecuteAsync(result, request, collection, null);

        Assert.Single(warnings);
        Assert.Contains("UnknownEnv", warnings[0]);
    }

    // ── Upsert: existing variable updated ─────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_ExistingCollectionVar_UpdatedInPlace()
    {
        using var _ = new AppDataSandbox();
        var (executor, collRepo, _) = await CaptureExecutorFactory.CreateAsync();

        var collection = await collRepo.AddCollectionAsync("Test");
        collection.Variables.Add(new CollectionVariable { Key = "token", Value = "old", IsEnabled = true });

        var request = new HttpRequestEntry { Name = "R1", Url = "https://test.io" };
        request.CaptureRules.Add(new CaptureRule
        {
            Source = CaptureSource.BodyJsonPath,
            JsonPath = "$.token",
            TargetVariable = "token",
            TargetScope = "collection",
            IsEnabled = true,
        });

        var result = new HttpRequestResult
        {
            StatusCode = 200,
            ResponseBody = """{"token":"new-value"}""",
        };
        await executor.ExecuteAsync(result, request, collection, null);

        var v = Assert.Single(collection.Variables);
        Assert.Equal("new-value", v.Value);
    }

    // ── JSONPath: numeric value ────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_BodyJsonPath_NumericValue_ExtractedAsString()
    {
        using var _ = new AppDataSandbox();
        var (executor, collRepo, _) = await CaptureExecutorFactory.CreateAsync();

        var collection = await collRepo.AddCollectionAsync("Test");
        var request = new HttpRequestEntry { Name = "R1", Url = "https://test.io" };
        request.CaptureRules.Add(new CaptureRule
        {
            Source = CaptureSource.BodyJsonPath,
            JsonPath = "$.count",
            TargetVariable = "item_count",
            TargetScope = "collection",
            IsEnabled = true,
        });

        var result = new HttpRequestResult
        {
            StatusCode = 200,
            ResponseBody = """{"count":42}""",
        };
        var warnings = await executor.ExecuteAsync(result, request, collection, null);

        Assert.Empty(warnings);
        var v = Assert.Single(collection.Variables);
        Assert.Equal("42", v.Value);
    }

    // ── Multiple rules: partial failure ────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_MultipleRules_FirstSucceeds_SecondWarns()
    {
        using var _ = new AppDataSandbox();
        var (executor, collRepo, _) = await CaptureExecutorFactory.CreateAsync();

        var collection = await collRepo.AddCollectionAsync("Test");
        var request = new HttpRequestEntry { Name = "R1", Url = "https://test.io" };
        request.CaptureRules.Add(new CaptureRule
        {
            Source = CaptureSource.BodyJsonPath,
            JsonPath = "$.token",
            TargetVariable = "token",
            TargetScope = "collection",
            IsEnabled = true,
        });
        request.CaptureRules.Add(new CaptureRule
        {
            Source = CaptureSource.BodyJsonPath,
            JsonPath = "$.missing",
            TargetVariable = "missing_field",
            TargetScope = "collection",
            IsEnabled = true,
        });

        var result = new HttpRequestResult
        {
            StatusCode = 200,
            ResponseBody = """{"token":"abc"}""",
        };
        var warnings = await executor.ExecuteAsync(result, request, collection, null);

        Assert.Single(warnings);
        Assert.Contains("missing_field", warnings[0]);
        Assert.Single(collection.Variables);
        Assert.Equal("abc", collection.Variables[0].Value);
    }

    // ── ResponseHeader with blank/null HeaderName ──────────────────────────────

    [Fact]
    public async Task ExecuteAsync_ResponseHeader_BlankHeaderName_ReturnsWarning()
    {
        using var _ = new AppDataSandbox();
        var (executor, collRepo, _) = await CaptureExecutorFactory.CreateAsync();

        var collection = await collRepo.AddCollectionAsync("Test");
        var request = new HttpRequestEntry { Name = "R1", Url = "https://test.io" };
        request.CaptureRules.Add(new CaptureRule
        {
            Source = CaptureSource.ResponseHeader,
            HeaderName = "",   // blank — extractor returns null
            TargetVariable = "result",
            TargetScope = "collection",
            IsEnabled = true,
        });

        var result = new HttpRequestResult
        {
            StatusCode = 200,
            ResponseHeaders = [("Content-Type", "application/json")],
        };
        var warnings = await executor.ExecuteAsync(result, request, collection, null);

        Assert.Single(warnings);
        Assert.Contains("result", warnings[0]);
    }
}

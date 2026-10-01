using System.Net;
using System.Text;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;
using SwebKit.Core.Services;

namespace SwebKit.Core.Tests;

// ── Form-data bodies (text + file fields) ─────────────────────────────────────
//
// Covers the multipart body builder: text fields go out as plain parts, fields
// flagged IsFile attach real file bytes with a filename part, missing files fall
// back to a text part (Postman-compatible), and disabled fields are skipped.

public sealed class HttpRequestExecutorFormDataTests : IDisposable
{
    private readonly List<string> _tempFiles = [];

    public void Dispose()
    {
        foreach (var f in _tempFiles)
            try { File.Delete(f); } catch { /* best effort */ }
    }

    private string TempFile(string name, string contents)
    {
        var path = Path.Combine(Path.GetTempPath(), $"swebkit-fd-{Guid.NewGuid():N}", name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents);
        _tempFiles.Add(path);
        return path;
    }

    [Fact]
    public async Task ExecuteAsync_FormData_FileField_SendsBytesWithFilename()
    {
        var file = TempFile("doc.pdf", "pdf-bytes");
        var (executor, handler) = CreateExecutor();
        var request = new HttpRequestEntry
        {
            Name = "R",
            Url = "https://example.test/upload",
            Method = ApiRequestMethod.Post,
            Body =
            {
                Mode = RequestBodyMode.FormData,
                FormData = [new FormDataField { Key = "file", Value = file, IsFile = true }],
            },
        };

        await executor.ExecuteAsync(request, new ApiCollection { Name = "C" }, null);

        var sent = handler.LastBody!;
        Assert.Contains("filename=doc.pdf", sent);
        Assert.Contains("pdf-bytes", sent);
    }

    [Fact]
    public async Task ExecuteAsync_FormData_TextField_SendsValueOnly()
    {
        var (executor, handler) = CreateExecutor();
        var request = new HttpRequestEntry
        {
            Name = "R",
            Url = "https://example.test/upload",
            Method = ApiRequestMethod.Post,
            Body =
            {
                Mode = RequestBodyMode.FormData,
                FormData = [new FormDataField { Key = "document", Value = "{}" }],
            },
        };

        await executor.ExecuteAsync(request, new ApiCollection { Name = "C" }, null);

        var sent = handler.LastBody!;
        Assert.Contains("name=document", sent);
        Assert.Contains("{}", sent);
        Assert.DoesNotContain("filename=", sent);
    }

    [Fact]
    public async Task ExecuteAsync_FormData_MissingFile_FallsBackToTextPart()
    {
        var (executor, handler) = CreateExecutor();
        var request = new HttpRequestEntry
        {
            Name = "R",
            Url = "https://example.test/upload",
            Method = ApiRequestMethod.Post,
            Body =
            {
                Mode = RequestBodyMode.FormData,
                FormData = [new FormDataField { Key = "file", Value = "C:/nope/missing.pdf", IsFile = true }],
            },
        };

        await executor.ExecuteAsync(request, new ApiCollection { Name = "C" }, null);

        var sent = handler.LastBody!;
        Assert.Contains("name=file", sent);
        Assert.DoesNotContain("filename=", sent);
    }

    [Fact]
    public async Task ExecuteAsync_FormData_DisabledField_NotSent()
    {
        var (executor, handler) = CreateExecutor();
        var request = new HttpRequestEntry
        {
            Name = "R",
            Url = "https://example.test/upload",
            Method = ApiRequestMethod.Post,
            Body =
            {
                Mode = RequestBodyMode.FormData,
                FormData =
                [
                    new FormDataField { Key = "on", Value = "yes" },
                    new FormDataField { Key = "off", Value = "no", IsEnabled = false },
                ],
            },
        };

        await executor.ExecuteAsync(request, new ApiCollection { Name = "C" }, null);

        var sent = handler.LastBody!;
        Assert.Contains("name=on", sent);
        Assert.DoesNotContain("name=off", sent);
    }

    [Fact]
    public async Task ExecuteAsync_FormData_SubstitutesVariablesInValues()
    {
        var (executor, handler) = CreateExecutor();
        var env = new ApiEnvironment
        {
            Variables = [new EnvironmentVariable { Key = "pkg", Value = "abc-123", IsEnabled = true }],
        };
        var request = new HttpRequestEntry
        {
            Name = "R",
            Url = "https://example.test/upload",
            Method = ApiRequestMethod.Post,
            Body =
            {
                Mode = RequestBodyMode.FormData,
                FormData = [new FormDataField { Key = "meta", Value = "pkg={{pkg}}" }],
            },
        };

        await executor.ExecuteAsync(request, new ApiCollection { Name = "C" }, env);

        Assert.Contains("pkg=abc-123", handler.LastBody);
    }

    // ── Harness ───────────────────────────────────────────────────────────────

    private static (HttpRequestExecutor Executor, CapturingHandler Handler) CreateExecutor()
    {
        var handler = new CapturingHandler();
        var executor = new HttpRequestExecutor(
            new StubFactory(new HttpClient(handler)),
            new VariableSubstitutionService(new StubCredentialStore(), new StubKeyVaultResolver(available: false)),
            new NoOpCaptureExecutor(),
            new NoOpAuthResolver(),
            new NoOpAuthHeaderBuilder());
        return (executor, handler);
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.Content is not null)
            {
                await request.Content.LoadIntoBufferAsync(cancellationToken);
                LastBody = await request.Content.ReadAsStringAsync(cancellationToken);
            }

            LastRequest = request;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json"),
            };
        }
    }

    private sealed class StubFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class NoOpCaptureExecutor : IPostRequestCaptureExecutor
    {
        public Task<IReadOnlyList<string>> ExecuteAsync(
            HttpRequestResult result,
            HttpRequestEntry request,
            ApiCollection collection,
            ApiEnvironment? activeEnvironment,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<string>>([]);
    }

    private sealed class NoOpAuthResolver : IAuthInheritanceResolver
    {
        public (AuthConfig ResolvedAuth, string? InheritedFromName) Resolve(
            HttpRequestEntry request,
            ApiCollection collection)
            => (new AuthConfig { Type = AuthType.None }, null);
    }

    private sealed class NoOpAuthHeaderBuilder : IAuthHeaderBuilder
    {
        public Task<IReadOnlyList<string>> ApplyAsync(
            HttpRequestMessage message,
            AuthConfig? auth,
            IReadOnlyDictionary<string, string?>? scope = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<string>>([]);
    }
}

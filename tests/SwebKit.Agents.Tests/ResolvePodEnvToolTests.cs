using System.Text.Json;
using Moq;
using SwebKit.Agents.Tools;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;
using SwebKit.Core.Services;
using Xunit;

namespace SwebKit.Agents.Tests;

public class ResolvePodEnvToolTests
{
    private const string PodYaml = """
        apiVersion: v1
        kind: Pod
        metadata:
          name: api-abc123
          namespace: team-a
          ownerReferences:
          - kind: ReplicaSet
            name: api-abc123
        spec:
          containers:
          - name: api
            env:
            - name: ASPNETCORE_ENVIRONMENT
              value: Production
            - name: FEATURE__X
              valueFrom:
                configMapKeyRef:
                  name: app-settings
                  key: Feature__X
            - name: DB__PASSWORD
              valueFrom:
                secretKeyRef:
                  name: db-credentials
                  key: password
            - name: MISSING
              valueFrom:
                configMapKeyRef:
                  name: app-settings
                  key: NotThere
            - name: POD_IP
              valueFrom:
                fieldRef:
                  fieldPath: status.podIP
            envFrom:
            - configMapRef:
                name: tracing
              prefix: OTEL_
            - secretRef:
                name: db-credentials
        """;

    private static JsonElement Args(object value) => JsonSerializer.SerializeToElement(value);

    private static (Mock<IAksClientFactory> Factory, Mock<IAksClient> Client, AppStateService State) Build()
    {
        var client = new Mock<IAksClient>();
        var factory = new Mock<IAksClientFactory>();
        factory.Setup(f => f.Create(It.IsAny<string?>(), It.IsAny<string?>())).Returns(client.Object);

        client.Setup(c => c.GetResourceYamlAsync("team-a", "Pod", "api-abc123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(PodYaml);
        client.Setup(c => c.GetConfigMapValuesAsync("team-a", "app-settings", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string> { ["Feature__X"] = "true" });
        client.Setup(c => c.GetConfigMapValuesAsync("team-a", "tracing", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string>
            {
                ["ENDPOINT"] = "http://otel:4317",
                ["SAMPLE_RATE"] = "0.1",
            });
        client.Setup(c => c.GetSecretValuesAsync("team-a", "db-credentials", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string> { ["password"] = "s3cret" });
        client.Setup(c => c.GetSecretValuesAsync("team-a", "missing-secret", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("not found"));

        var state = TestSupport.CreateAppState(config => config.AksConfig = new AksConfig { DefaultNamespace = "team-a" });
        return (factory, client, state);
    }

    [Fact]
    public async Task ResolvesLiteralConfigMapAndMaskedSecretInOneCall()
    {
        var (factory, client, state) = Build();
        var tool = new ResolvePodEnvTool(factory.Object, new DemoAksClient(), state);

        var result = JsonDocument.Parse(await tool.ExecuteAsync(
            Args(new { pod_name = "api-abc123", @namespace = "team-a" }), CancellationToken.None)).RootElement;

        Assert.Equal("api-abc123", result.GetProperty("pod").GetString());
        Assert.Equal("ReplicaSet", result.GetProperty("owner").GetProperty("kind").GetString());

        var env = result.GetProperty("containers")[0].GetProperty("env").EnumerateArray().ToArray();

        var literal = env.Single(e => e.GetProperty("name").GetString() == "ASPNETCORE_ENVIRONMENT");
        Assert.Equal("Production", literal.GetProperty("value").GetString());

        var cmRef = env.Single(e => e.GetProperty("name").GetString() == "FEATURE__X");
        Assert.Equal("true", cmRef.GetProperty("value").GetString());
        Assert.Contains("app-settings/Feature__X", cmRef.GetProperty("source").GetString());

        var secretRef = env.Single(e => e.GetProperty("name").GetString() == "DB__PASSWORD");
        Assert.Equal("***", secretRef.GetProperty("value").GetString());
        Assert.DoesNotContain("s3cret", result.GetRawText());

        var missing = env.Single(e => e.GetProperty("name").GetString() == "MISSING");
        Assert.False(missing.GetProperty("resolved").GetBoolean());
        Assert.True(missing.GetProperty("missingKey").GetBoolean());

        var fieldRef = env.Single(e => e.GetProperty("name").GetString() == "POD_IP");
        Assert.Contains("fieldRef", fieldRef.GetProperty("source").GetString());
        Assert.Contains("runtime-resolved", fieldRef.GetProperty("note").GetString());
    }

    [Fact]
    public async Task ExpandsEnvFromWithPrefixAndMasksSecretRefKeys()
    {
        var (factory, client, state) = Build();
        var tool = new ResolvePodEnvTool(factory.Object, new DemoAksClient(), state);

        var result = JsonDocument.Parse(await tool.ExecuteAsync(
            Args(new { pod_name = "api-abc123", @namespace = "team-a" }), CancellationToken.None)).RootElement;

        var envFrom = result.GetProperty("containers")[0].GetProperty("envFrom").EnumerateArray().ToArray();

        var cmBlock = envFrom.Single(b => b.GetProperty("source").GetString()!.Contains("configMapRef"));
        var injected = cmBlock.GetProperty("injected").EnumerateArray().ToArray();
        Assert.Equal(2, injected.Length);
        Assert.Equal("OTEL_ENDPOINT", injected[0].GetProperty("name").GetString());
        Assert.Equal("http://otel:4317", injected[0].GetProperty("value").GetString());

        var secBlock = envFrom.Single(b => b.GetProperty("source").GetString()!.Contains("secretRef"));
        var secInjected = secBlock.GetProperty("injected").EnumerateArray().Single();
        Assert.Equal("password", secInjected.GetProperty("name").GetString());
        Assert.Equal("***", secInjected.GetProperty("value").GetString());
    }

    [Fact]
    public async Task EnvNameFilterNarrowsEntriesAndEnvFromExpansion()
    {
        var (factory, client, state) = Build();
        var tool = new ResolvePodEnvTool(factory.Object, new DemoAksClient(), state);

        var result = JsonDocument.Parse(await tool.ExecuteAsync(
            Args(new { pod_name = "api-abc123", @namespace = "team-a", env_name = "FEATURE__X" }),
            CancellationToken.None)).RootElement;

        var container = result.GetProperty("containers")[0];
        var env = container.GetProperty("env").EnumerateArray().ToArray();
        Assert.Single(env);
        Assert.Equal("FEATURE__X", env[0].GetProperty("name").GetString());
        // envFrom expands to OTEL_* / secret keys — none match the filter, so blocks stay empty.
        foreach (var block in container.GetProperty("envFrom").EnumerateArray())
            Assert.Equal(0, block.GetProperty("injected").GetArrayLength());
    }

    [Fact]
    public async Task FetchesAReferencedObjectOncePerCall()
    {
        var (factory, client, state) = Build();
        // Second env entry referencing the same ConfigMap — one cluster read must serve both.
        var yaml = PodYaml.Replace(
            "            - name: POD_IP\n",
            "            - name: FEATURE__Y\n              valueFrom:\n                configMapKeyRef:\n                  name: app-settings\n                  key: Feature__X\n            - name: POD_IP\n");
        client.Setup(c => c.GetResourceYamlAsync("team-a", "Pod", "api-abc123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(yaml);
        var tool = new ResolvePodEnvTool(factory.Object, new DemoAksClient(), state);

        await tool.ExecuteAsync(Args(new { pod_name = "api-abc123", @namespace = "team-a" }), CancellationToken.None);

        client.Verify(c => c.GetConfigMapValuesAsync("team-a", "app-settings", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DefaultsPodNameAndNamespaceToUiSelection()
    {
        var (factory, client, state) = Build();
        var tool = new ResolvePodEnvTool(factory.Object, new DemoAksClient(), state);

        using var sel = AgentExecutionContext.Push(new Dictionary<string, string>
        {
            ["pod"] = "api-abc123",
            ["namespace"] = "team-a",
        });
        var result = JsonDocument.Parse(await tool.ExecuteAsync(Args(new { }), CancellationToken.None)).RootElement;

        Assert.Equal("api-abc123", result.GetProperty("pod").GetString());
        Assert.Equal("team-a", result.GetProperty("namespace_name").GetString());
    }

    [Fact]
    public async Task NoPodAnywhere_ReturnsErrorWithoutClusterCall()
    {
        var (factory, client, state) = Build();
        var tool = new ResolvePodEnvTool(factory.Object, new DemoAksClient(), state);

        var result = JsonDocument.Parse(await tool.ExecuteAsync(Args(new { }), CancellationToken.None)).RootElement;

        Assert.Contains("pod", result.GetProperty("error").GetString());
        client.Verify(c => c.GetResourceYamlAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UnreadableSecret_WarnsInsteadOfFailingTheWholeCall()
    {
        var (factory, client, state) = Build();
        var yaml = PodYaml.Replace("db-credentials", "missing-secret");
        client.Setup(c => c.GetResourceYamlAsync("team-a", "Pod", "api-abc123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(yaml);
        var tool = new ResolvePodEnvTool(factory.Object, new DemoAksClient(), state);

        var result = JsonDocument.Parse(await tool.ExecuteAsync(
            Args(new { pod_name = "api-abc123", @namespace = "team-a" }), CancellationToken.None)).RootElement;

        Assert.Contains("missing-secret", result.GetProperty("warnings").EnumerateArray().Single().GetString());
        // The ConfigMap-backed entries still resolved — one bad ref doesn't sink the report.
        var env = result.GetProperty("containers")[0].GetProperty("env").EnumerateArray().ToArray();
        Assert.Contains(env, e => e.GetProperty("name").GetString() == "FEATURE__X" && e.GetProperty("resolved").GetBoolean());
    }
}

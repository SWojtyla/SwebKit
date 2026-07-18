using Moq;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;
using SwebKit.Core.Models;
using Xunit;

namespace SwebKit.Agents.Tests;

public class AgentContextBuilderTests
{
    private static AgentContextBuilder Build(
        Mock<ISelectionContext>? selection = null,
        IReadOnlyList<AlertFiredEvent>? alerts = null)
    {
        selection ??= new Mock<ISelectionContext>();
        var alertMonitor = new Mock<IAlertMonitorService>();
        alertMonitor.Setup(a => a.RecentAlerts).Returns(alerts ?? []);
        return new AgentContextBuilder(selection.Object, alertMonitor.Object);
    }

    [Fact]
    public void BuildContext_NoServices_ReportsKubernetesNotConfigured()
    {
        var context = Build().BuildContext(TestSupport.CreateAppState());
        Assert.Contains("Kubernetes: (not configured)", context);
    }

    [Fact]
    public void BuildContext_WithAksContext_IncludesContextAndKubeconfig()
    {
        var appState = TestSupport.CreateAppState(c => c.AksConfig = new AksConfig
        {
            KubeconfigContext = "prod-cluster",
            KubeconfigPath = "/home/user/.kube/config",
        });

        var context = Build().BuildContext(appState);

        Assert.Contains("Kubernetes context: prod-cluster", context);
        Assert.Contains("kubeconfig: /home/user/.kube/config", context);
    }

    [Fact]
    public void BuildContext_AksWithoutContext_DefaultsToDefault()
    {
        var appState = TestSupport.CreateAppState(c => c.AksConfig = new AksConfig());
        var context = Build().BuildContext(appState);
        Assert.Contains("Kubernetes context: default", context);
    }

    [Fact]
    public void BuildContext_WithServiceBusNamespaces_ListsAliases()
    {
        var ns = new ServiceBusNamespace { Alias = "orders-dev", FullyQualifiedNamespace = "orders.servicebus.windows.net" };
        var appState = TestSupport.CreateAppState(serviceBusNamespaces: [ns]);

        var context = Build().BuildContext(appState);

        Assert.Contains("Service Bus: orders-dev", context);
    }

    [Fact]
    public void BuildContext_WithObservabilityAndDevOpsAndStorage_IncludesAll()
    {
        var appState = TestSupport.CreateAppState(c =>
        {
            c.ObservabilityConfig = new ObservabilityConfig { SelectedResourceId = "res", SelectedResourceName = "AppInsightsProd" };
            c.DevOpsConfig = new DevOpsConfig { Organization = "contoso" };
            c.StorageAccounts.Add(new StorageConfig());
        });

        var context = Build().BuildContext(appState);

        Assert.Contains("Observability: AppInsightsProd", context);
        Assert.Contains("DevOps: contoso", context);
        Assert.Contains("Storage: configured", context);
    }

    [Fact]
    public void BuildContext_WithRedisConnectionString_ReportsConfigured()
    {
        var appState = TestSupport.CreateAppState(c => c.RedisConfig = new RedisConfig { ConnectionString = "localhost:6379" });
        var context = Build().BuildContext(appState);
        Assert.Contains("Redis: configured", context);
    }

    [Fact]
    public void BuildContext_WithSelection_IncludesSelectedResources()
    {
        var selection = new Mock<ISelectionContext>();
        selection.Setup(s => s.GetSelection<object>("aks")).Returns("pod/orders-1");

        var context = Build(selection).BuildContext(TestSupport.CreateAppState());

        Assert.Contains("Selected: aks=pod/orders-1", context);
    }

    [Fact]
    public void BuildContext_WithRecentAlerts_AppendsAlertLines()
    {
        var alert = new AlertFiredEvent(
            RuleId: "r1",
            RuleName: "High CPU",
            Source: AlertRuleSource.AksPodHealth,
            Severity: AlertSeverity.Critical,
            Message: "cpu > 90%",
            Detail: "node-1",
            FiredAt: DateTimeOffset.UtcNow,
            ProfileName: "prod");

        var context = Build(alerts: [alert]).BuildContext(TestSupport.CreateAppState());

        Assert.Contains("Recent alerts (last 3):", context);
        Assert.Contains("High CPU", context);
    }

    [Fact]
    public void AgentContext_ToString_JoinsConfiguredPartsOnly()
    {
        var ctx = new AgentContext
        {
            KubernetesContext = "prod",
            KubeconfigPath = "/cfg",
            ServiceBusNamespace = "orders",
            RedisConfigured = true,
            DevOpsOrganization = "contoso",
        };

        var text = ctx.ToString();

        Assert.Contains("Kubernetes context: prod | kubeconfig: /cfg", text);
        Assert.Contains("Service Bus: orders", text);
        Assert.Contains("Redis: configured", text);
        Assert.Contains("DevOps: contoso", text);
        Assert.DoesNotContain("Observability", text);
        Assert.DoesNotContain("Storage", text);
    }

    [Fact]
    public void AgentContext_ToString_EmptyContext_ReturnsEmptyString()
    {
        Assert.Equal(string.Empty, new AgentContext().ToString());
    }
}

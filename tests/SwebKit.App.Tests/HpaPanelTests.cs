using Bunit;
using Microsoft.Extensions.DependencyInjection;
using SwebKit.App.Components.Aks;
using SwebKit.Core.Models;

namespace SwebKit.App.Tests;

public class HpaPanelTests : TestContext
{
    public HpaPanelTests()
    {
        var libConfigType = Type.GetType(
            "Microsoft.FluentUI.AspNetCore.Components.LibraryConfiguration, Microsoft.FluentUI.AspNetCore.Components");
        if (libConfigType is not null)
            Services.AddSingleton(libConfigType, Activator.CreateInstance(libConfigType)!);
    }

    [Fact]
    public void HpaPanel_RendersInactiveStateAndConditionMessage()
    {
        var hpa = CreateHpa();

        var cut = RenderComponent<HpaPanel>(ps => ps.Add(p => p.Hpa, hpa));

        Assert.Contains("Inactive", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("ScalingDisabled", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("autoscaling paused by workload annotation", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("cpu", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void HpaPanel_RendersReplicaBounds()
    {
        var hpa = CreateHpa();

        var cut = RenderComponent<HpaPanel>(ps => ps.Add(p => p.Hpa, hpa));

        Assert.Contains("min 2", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("max 10", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void HpaPanel_RendersDisabledStateForFrozenPlainHpa()
    {
        var hpa = CreateHpa();
        hpa.IsScalingDisabled = true;
        hpa.CurrentReplicas = 3;

        var cut = RenderComponent<HpaPanel>(ps => ps.Add(p => p.Hpa, hpa));

        Assert.Contains("Disabled", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("frozen at 3 replicas", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void HpaPanel_RendersKedaChipAndPausedCopy()
    {
        var hpa = CreateHpa();
        hpa.IsKedaManaged = true;
        hpa.ScaledObjectName = "sign-engine-scaler";
        hpa.IsScalingDisabled = true;

        var cut = RenderComponent<HpaPanel>(ps => ps.Add(p => p.Hpa, hpa));

        Assert.Contains("KEDA", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("KEDA autoscaling is paused", cut.Markup, StringComparison.Ordinal);
    }

    private static HpaInfo CreateHpa() => new()
    {
        Name = "sign-engine",
        Namespace = "dev-sign",
        TargetKind = "Deployment",
        TargetName = "sign-engine",
        MinReplicas = 2,
        MaxReplicas = 10,
        CurrentReplicas = 0,
        DesiredReplicas = 0,
        Metrics =
        [
            new HpaMetricStatus
            {
                Name = "cpu",
                Type = "Resource",
                CurrentValue = 42,
                TargetValue = 75
            }
        ],
        Conditions =
        [
            new HpaCondition
            {
                Type = "AbleToScale",
                Status = "True",
                Reason = "SucceededGetScale"
            },
            new HpaCondition
            {
                Type = "ScalingActive",
                Status = "False",
                Reason = "ScalingDisabled",
                Message = "autoscaling paused by workload annotation"
            }
        ]
    };
}

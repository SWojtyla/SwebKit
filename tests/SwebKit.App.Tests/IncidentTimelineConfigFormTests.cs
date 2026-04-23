using Bunit;
using Bunit.JSInterop;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;
using SwebKit.App.Components.Pages;
using SwebKit.Core.Domain;
using SwebKit.Core.Models;

namespace SwebKit.App.Tests;

public sealed class IncidentTimelineConfigFormTests : TestContext
{
    public IncidentTimelineConfigFormTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        var libConfigType = Type.GetType(
            "Microsoft.FluentUI.AspNetCore.Components.LibraryConfiguration, Microsoft.FluentUI.AspNetCore.Components");
        if (libConfigType is not null)
        {
            Services.AddSingleton(libConfigType, Activator.CreateInstance(libConfigType)!);
        }

        Services.AddFluentUIComponents();
    }

    [Fact]
    public void SuggestedScope_CanSeedANewMapping()
    {
        var environment = new AppConfig();

        var cut = RenderComponent<IncidentTimelineConfigForm>(parameters => parameters
            .Add(component => component.Environment, environment)
            .Add(component => component.SuggestedNamespace, "prd-phonotif")
            .Add(component => component.SuggestedWorkloadKind, "Deployment")
            .Add(component => component.SuggestedWorkloadName, "phonotif-api"));

        cut.Find("[data-testid='incident-mapping-add-suggested']").Click();

        Assert.Single(environment.IncidentTimeline.WorkloadMappings);
        var mapping = environment.IncidentTimeline.WorkloadMappings[0];
        Assert.Equal("prd-phonotif", mapping.Namespace);
        Assert.Equal(IncidentWorkloadKind.Deployment, mapping.WorkloadKind);
        Assert.Equal("phonotif-api", mapping.WorkloadName);
        Assert.Equal("phonotif-api", mapping.DisplayName);
        Assert.Contains("Current incident scope", cut.Markup);
    }

    [Fact]
    public void SaveButton_NormalizesMappingsBeforePersisting()
    {
        var saveCalls = 0;
        var environment = new AppConfig();
        environment.IncidentTimeline.WorkloadMappings.Add(new IncidentTimelineWorkloadMapping
        {
            Namespace = " prd-phonotif ",
            WorkloadKind = IncidentWorkloadKind.Deployment,
            WorkloadName = " phonotif-api ",
            DisplayName = " Phonotif API ",
            Observability = new IncidentTimelineObservabilityMapping
            {
                CloudRoleNames = [" phonotif-api ", "", "phonotif-api"],
                OperationNames = [" POST /notifications ", "  "],
            },
            ServiceBusEntities =
            [
                new SbEntityLink
                {
                    EntityPath = " orders/deadletter ",
                    Alias = " Order DLQ ",
                },
            ],
            DevOps = new IncidentTimelineDevOpsMapping
            {
                Pipelines =
                [
                    new IncidentTimelinePipelineBinding
                    {
                        ProjectName = " platform-services ",
                        PipelineId = 101,
                        Alias = " phonotif-api-ci-cd ",
                    },
                ],
                EnvironmentNames = [" Production ", "", "production"],
            },
        });

        var cut = RenderComponent<IncidentTimelineConfigForm>(parameters => parameters
            .Add(component => component.Environment, environment)
            .Add(component => component.OnSaved, EventCallback.Factory.Create(this, () => saveCalls++)));

        cut.Find("[data-testid='incident-mapping-save-button']").Click();

        Assert.Equal(1, saveCalls);
        var mapping = Assert.Single(environment.IncidentTimeline.WorkloadMappings);
        Assert.Equal("prd-phonotif", mapping.Namespace);
        Assert.Equal("phonotif-api", mapping.WorkloadName);
        Assert.Equal("Phonotif API", mapping.DisplayName);
        Assert.Equal(["phonotif-api"], mapping.Observability!.CloudRoleNames);
        Assert.Equal(["POST /notifications"], mapping.Observability.OperationNames);
        Assert.Equal("orders/deadletter", mapping.ServiceBusEntities[0].EntityPath);
        Assert.Equal("Order DLQ", mapping.ServiceBusEntities[0].Alias);
        Assert.Equal("platform-services", mapping.DevOps!.Pipelines[0].ProjectName);
        Assert.Equal("phonotif-api-ci-cd", mapping.DevOps.Pipelines[0].Alias);
        Assert.Equal(["Production"], mapping.DevOps.EnvironmentNames);
    }
}
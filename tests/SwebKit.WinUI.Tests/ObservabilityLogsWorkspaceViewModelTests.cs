using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;
using SwebKit.Core.Models;
using SwebKit.WinUI.ViewModels.Observability;

namespace SwebKit.WinUI.Tests;

public sealed class ObservabilityLogsWorkspaceViewModelTests
{
    [Fact]
    public void ApplyConfig_LoadsGuidedDraftAndSavedQueries()
    {
        var viewModel = new ObservabilityLogsWorkspaceViewModel(new SuccessfulGuidedCompiler());
        var config = new ObservabilityConfig
        {
            LogsQueryMode = GuidedLogsQueryMode.Guided,
            GuidedLogsDraft = new GuidedKqlQueryDefinition
            {
                Table = "requests",
                Limit = 25,
                Filters =
                [
                    new GuidedKqlFilter
                    {
                        Column = "cloud_RoleName",
                        Operator = GuidedKqlFilterOperator.Equals,
                        Value = "orders-api",
                    },
                ],
            },
            SavedQueries =
            [
                new SavedQuery
                {
                    Name = "Errors",
                    Query = "exceptions | take 5",
                    CreatedAt = DateTimeOffset.Parse("2026-04-25T10:00:00Z"),
                },
            ],
        };

        viewModel.ApplyConfig(config);

        Assert.True(viewModel.UseGuidedLogsMode);
        Assert.Equal("requests", viewModel.GuidedTableName);
        Assert.Equal("cloud_RoleName", viewModel.GuidedFilterColumn);
        Assert.Equal("orders-api", viewModel.GuidedFilterValue);
        Assert.Equal("25", viewModel.GuidedLimitText);
        Assert.Single(viewModel.SavedQueries);
        Assert.Equal("Errors", viewModel.SavedQueries[0].Name);
    }

    [Fact]
    public void QueuePresetRestore_LoadQueryPresets_RestoresSelectedPresetIntoAdvancedQuery()
    {
        var viewModel = new ObservabilityLogsWorkspaceViewModel(new SuccessfulGuidedCompiler());
        viewModel.QueuePresetRestore("errors");

        viewModel.LoadQueryPresets(new PresetOnlyObservabilityProvider(
        [
            new QueryPreset("errors", "Errors", "Recent errors", "exceptions | take 10"),
        ]));

        Assert.Equal("errors", viewModel.SelectedQueryPreset?.Id);
        Assert.Equal("exceptions | take 10", viewModel.AdvancedQueryText);
    }

    [Fact]
    public void TryPrepareQueryForExecution_WhenGuidedQueryIsInvalid_BlocksExecution()
    {
        var viewModel = new ObservabilityLogsWorkspaceViewModel(new InvalidGuidedCompiler());
        viewModel.RestoreLogsMode("guided");
        viewModel.GuidedTableName = "requests";

        var executable = viewModel.TryPrepareQueryForExecution(out var queryText);

        Assert.False(executable);
        Assert.Equal(string.Empty, queryText);
        Assert.Contains("validation issues", viewModel.LogsResultSummary, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class SuccessfulGuidedCompiler : IGuidedKqlCompiler
    {
        public GuidedKqlCompileResult Compile(GuidedKqlQueryDefinition definition)
        {
            return GuidedKqlCompileResult.Success($"{definition.Table} | take {definition.Limit}");
        }
    }

    private sealed class InvalidGuidedCompiler : IGuidedKqlCompiler
    {
        public GuidedKqlCompileResult Compile(GuidedKqlQueryDefinition definition)
        {
            return GuidedKqlCompileResult.Invalid(
            [
                new GuidedKqlCompileIssue(GuidedKqlCompileIssueSeverity.Error, "TABLE", "A table is required.", nameof(GuidedKqlQueryDefinition.Table)),
            ]);
        }
    }

    private sealed class PresetOnlyObservabilityProvider(IReadOnlyList<QueryPreset> presets) : IObservabilityProvider
    {
        public string ProviderType => "Test";

        public Task<OverviewMetrics> GetOverviewAsync(TimeRange range, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<ExceptionGroup>> GetTopExceptionsAsync(TimeRange range, int top = 20, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<LogRow>> GetExceptionSamplesAsync(string exceptionType, TimeRange range, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<OperationPerformance>> GetOperationPerformanceAsync(TimeRange range, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<LogQueryResult> RunQueryAsync(string query, TimeRange range, int maxRows = 500, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<AvailabilityResult>> GetAvailabilityAsync(TimeRange range, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<LatencyDataPoint>> GetOperationLatencyTrendAsync(string operationName, TimeRange range, CancellationToken ct = default) => throw new NotSupportedException();

        public IReadOnlyList<QueryPreset> GetPresets() => presets;

        public Task<DependencyHealthSummary> GetDependencyHealthAsync(TimeRange range, int maxDependencies = 20, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<DimensionBreakdown> GetDimensionBreakdownAsync(TimeRange range, string dimensionKey, int topN = 15, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
using SwebKit.Core.Models;
using SwebKit.Observability;

namespace SwebKit.Core.Tests;

public class GuidedKqlCompilerTests
{
    private readonly GuidedKqlCompiler _compiler = new();

    [Fact]
    public void Compile_DefaultDefinition_ProducesDeterministicKql()
    {
        var definition = GuidedKqlQueryDefinition.CreateDefault();

        var result = _compiler.Compile(definition);

        Assert.True(result.CanExecute);
        Assert.False(result.HasErrors);
        Assert.Equal(
            "traces\n| order by timestamp desc\n| take 100",
            result.Query);
    }

    [Fact]
    public void Compile_WithFiltersProjectionSort_ProducesExpectedClauseOrder()
    {
        var definition = new GuidedKqlQueryDefinition
        {
            Table = "requests",
            Filters =
            [
                new GuidedKqlFilter { Column = "success", Operator = GuidedKqlFilterOperator.Equals, Value = "false" },
                new GuidedKqlFilter { Column = "name", Operator = GuidedKqlFilterOperator.Contains, Value = "api" },
            ],
            Projections = ["timestamp", "name", "duration"],
            Sort = new GuidedKqlSort { Column = "duration", Descending = true },
            Limit = 250,
        };

        var result = _compiler.Compile(definition);

        Assert.True(result.CanExecute);
        Assert.Equal(
            "requests\n| where success == false\n| where name contains 'api'\n| project timestamp, name, duration\n| order by duration desc\n| take 250",
            result.Query);
    }

    [Fact]
    public void Compile_InvalidTable_ReturnsError()
    {
        var definition = new GuidedKqlQueryDefinition
        {
            Table = "notATable",
        };

        var result = _compiler.Compile(definition);

        Assert.False(result.CanExecute);
        Assert.Contains(result.Issues, issue => issue.Code == "TABLE_INVALID" && issue.IsError);
    }

    [Fact]
    public void Compile_InvalidFilterColumn_ReturnsError()
    {
        var definition = new GuidedKqlQueryDefinition
        {
            Table = "requests",
            Filters = [new GuidedKqlFilter { Column = "badColumn", Operator = GuidedKqlFilterOperator.Equals, Value = "x" }],
        };

        var result = _compiler.Compile(definition);

        Assert.False(result.CanExecute);
        Assert.Contains(result.Issues, issue => issue.Code == "FILTER_COLUMN_INVALID" && issue.IsError);
    }

    [Fact]
    public void Compile_InvalidFilterOperator_ReturnsError()
    {
        var definition = new GuidedKqlQueryDefinition
        {
            Table = "requests",
            Filters = [new GuidedKqlFilter { Column = "name", Operator = (GuidedKqlFilterOperator)999, Value = "api" }],
        };

        var result = _compiler.Compile(definition);

        Assert.False(result.CanExecute);
        Assert.Contains(result.Issues, issue => issue.Code == "FILTER_OPERATOR_INVALID" && issue.IsError);
    }

    [Fact]
    public void Compile_EmptyFilterValue_ReturnsError()
    {
        var definition = new GuidedKqlQueryDefinition
        {
            Table = "requests",
            Filters = [new GuidedKqlFilter { Column = "name", Operator = GuidedKqlFilterOperator.Equals, Value = "  " }],
        };

        var result = _compiler.Compile(definition);

        Assert.False(result.CanExecute);
        Assert.Contains(result.Issues, issue => issue.Code == "FILTER_VALUE_REQUIRED" && issue.IsError);
    }

    [Fact]
    public void Compile_EscapesStringLiteralsSafely()
    {
        var definition = new GuidedKqlQueryDefinition
        {
            Table = "requests",
            Filters = [new GuidedKqlFilter { Column = "name", Operator = GuidedKqlFilterOperator.Equals, Value = "O'Brien" }],
        };

        var result = _compiler.Compile(definition);

        Assert.True(result.CanExecute);
        Assert.Contains("name == 'O''Brien'", result.Query);
    }

    [Fact]
    public void Compile_BroadLimit_AddsWarning()
    {
        var definition = new GuidedKqlQueryDefinition
        {
            Table = "traces",
            Limit = 2500,
        };

        var result = _compiler.Compile(definition);

        Assert.True(result.CanExecute);
        Assert.True(result.HasWarnings);
        Assert.Contains(result.Issues, issue => issue.Code == "LIMIT_BROAD" && issue.IsWarning);
    }

    [Fact]
    public void Compile_NullDraftCollectionsAndSort_UsesFallbacks()
    {
        var definition = new GuidedKqlQueryDefinition
        {
            Table = "traces",
            Filters = null!,
            Projections = null!,
            Sort = null!,
            Limit = 10,
        };

        var result = _compiler.Compile(definition);

        Assert.True(result.CanExecute);
        Assert.Equal(
            "traces\n| order by timestamp desc\n| take 10",
            result.Query);
    }
}

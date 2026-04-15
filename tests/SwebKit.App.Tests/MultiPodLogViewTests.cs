using System.Collections;
using System.Reflection;
using SwebKit.App.Components.Aks;
using SwebKit.Core.Models;

namespace SwebKit.App.Tests;

public sealed class MultiPodLogViewTests
{
    [Fact]
    public async Task FilteredLines_DoesNotThrow_WhenLinesAppendDuringFiltering()
    {
        var view = new MultiPodLogView();
        var viewType = typeof(MultiPodLogView);

        var textFilterField = viewType.GetField("TextFilter", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("TextFilter field not found.");
        var filterDirtyField = viewType.GetField("_filterDirty", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("_filterDirty field not found.");
        var appendLineMethod = viewType.GetMethod("AppendLine", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("AppendLine method not found.");
        var filteredLinesProperty = viewType.GetProperty("FilteredLines", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("FilteredLines property not found.");

        textFilterField.SetValue(view, "error");

        for (var index = 0; index < 1200; index++)
        {
            appendLineMethod.Invoke(view, [new AggregatedLogLine { PodName = $"api-{index % 3}", Line = $"error seed {index}" }]);
        }

        using var cts = new CancellationTokenSource();
        var mutator = Task.Run(() =>
        {
            var counter = 0;
            while (!cts.IsCancellationRequested)
            {
                appendLineMethod.Invoke(view,
                [
                    new AggregatedLogLine
                    {
                        PodName = $"api-{counter % 4}",
                        Line = $"error live {counter++}"
                    }
                ]);
            }
        }, cts.Token);

        Exception? captured = null;

        try
        {
            for (var iteration = 0; iteration < 200; iteration++)
            {
                filterDirtyField.SetValue(view, true);
                _ = filteredLinesProperty.GetValue(view);
                await Task.Yield();
            }
        }
        catch (Exception ex)
        {
            captured = ex.InnerException ?? ex;
        }
        finally
        {
            cts.Cancel();

            try
            {
                await mutator.WaitAsync(TimeSpan.FromSeconds(1));
            }
            catch (OperationCanceledException)
            {
            }
            catch (TimeoutException)
            {
            }
        }

        Assert.Null(captured);
    }

    [Fact]
    public void FilteredLines_RespectsFocusedPodSelection()
    {
        var view = new MultiPodLogView();
        var viewType = typeof(MultiPodLogView);

        var focusedPodField = viewType.GetField("_focusedPodName", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("_focusedPodName field not found.");
        var filterDirtyField = viewType.GetField("_filterDirty", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("_filterDirty field not found.");
        var appendLineMethod = viewType.GetMethod("AppendLine", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("AppendLine method not found.");
        var filteredLinesProperty = viewType.GetProperty("FilteredLines", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("FilteredLines property not found.");

        appendLineMethod.Invoke(view, [new AggregatedLogLine { PodName = "api-blue", Line = "blue line" }]);
        appendLineMethod.Invoke(view, [new AggregatedLogLine { PodName = "api-green", Line = "green line" }]);
        appendLineMethod.Invoke(view, [new AggregatedLogLine { PodName = "api-green", Line = "second green line" }]);

        focusedPodField.SetValue(view, "api-green");
        filterDirtyField.SetValue(view, true);

        var filtered = filteredLinesProperty.GetValue(view) as IEnumerable
            ?? throw new InvalidOperationException("Filtered lines did not return an enumerable result.");

        var podNames = filtered
            .Cast<object>()
            .Select(entry => entry.GetType().GetProperty("PodName")?.GetValue(entry)?.ToString())
            .ToList();

        Assert.Equal(2, podNames.Count);
        Assert.All(podNames, podName => Assert.Equal("api-green", podName));
    }
}
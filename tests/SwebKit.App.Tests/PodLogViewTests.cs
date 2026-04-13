using System.Reflection;
using SwebKit.App.Components.Aks;

namespace SwebKit.App.Tests;

public sealed class PodLogViewTests
{
    [Fact]
    public async Task FilteredLines_DoesNotThrow_WhenLinesAppendDuringFiltering()
    {
        var view = new PodLogView();
        var podLogViewType = typeof(PodLogView);

        var textFilterField = podLogViewType.GetField("TextFilter", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("TextFilter field not found.");
        var filterDirtyField = podLogViewType.GetField("_filterDirty", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("_filterDirty field not found.");
        var appendLineMethod = podLogViewType.GetMethod("AppendLine", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("AppendLine method not found.");
        var filteredLinesProperty = podLogViewType.GetProperty("FilteredLines", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("FilteredLines property not found.");

        textFilterField.SetValue(view, "error");

        for (var index = 0; index < 1200; index++)
        {
            appendLineMethod.Invoke(view, [$"error seed {index}"]);
        }

        using var cts = new CancellationTokenSource();
        var mutator = Task.Run(() =>
        {
            var counter = 0;
            while (!cts.IsCancellationRequested)
            {
                appendLineMethod.Invoke(view, [$"error live {counter++}"]);
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
}
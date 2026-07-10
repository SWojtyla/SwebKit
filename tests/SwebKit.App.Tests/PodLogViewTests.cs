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

    [Theory]
    [InlineData("2024-01-01T00:00:00Z ERROR something failed", "log-level-error")]
    [InlineData("FATAL: out of memory", "log-level-error")]
    [InlineData("[CRIT] system overload", "log-level-error")]
    public void GetLineClass_ErrorVariants_ReturnErrorClass(string line, string expectedClass)
    {
        var cls = PodLogView.GetLineClass(line);
        Assert.Equal(expectedClass, cls);
    }

    [Theory]
    [InlineData("2024-01-01T00:00:00Z WARN connection slow")]
    [InlineData("[WRN] disk space low")]
    [InlineData("WARNING: retry limit approaching")]
    public void GetLineClass_WarnVariants_ReturnWarnClass(string line)
    {
        Assert.Equal("log-level-warn", PodLogView.GetLineClass(line));
    }

    [Theory]
    [InlineData("DEBUG initializing component")]
    [InlineData("[DBG] entering handler")]
    [InlineData("[TRC] span opened")]
    public void GetLineClass_DebugVariants_ReturnDebugClass(string line)
    {
        Assert.Equal("log-level-debug", PodLogView.GetLineClass(line));
    }

    [Fact]
    public void GetLineClass_JsonLine_ReturnsDefault()
    {
        Assert.Equal("log-level-default", PodLogView.GetLineClass("{\"level\":\"error\",\"msg\":\"oops\"}"));
    }

    [Fact]
    public void GetLineClass_PlainLine_ReturnsDefault()
    {
        Assert.Equal("log-level-default", PodLogView.GetLineClass("starting up server on port 8080"));
    }

    [Fact]
    public void GetLineClass_EmptyLine_ReturnsDefault()
    {
        Assert.Equal("log-level-default", PodLogView.GetLineClass(""));
    }

    [Theory]
    // pageIndexFromNewest > 0 → Historical regardless of live/paused.
    [InlineData(true, true, false, 2, "Historical")]
    [InlineData(false, false, true, 1, "Historical")]
    // At newest, actively streaming and not paused → Live.
    [InlineData(true, true, false, 0, "Live")]
    // Paused at newest → Paused.
    [InlineData(true, true, true, 0, "Paused")]
    // Stopped (not loading) at newest → Paused (idle).
    [InlineData(false, false, false, 0, "Paused")]
    [InlineData(true, false, false, 0, "Paused")]
    public void ResolveFollowState_MapsStateCorrectly(
        bool isLive, bool isLoading, bool paused, int pageIndexFromNewest, string expected)
    {
        Assert.Equal(expected, PodLogView.ResolveFollowState(isLive, isLoading, paused, pageIndexFromNewest).ToString());
    }
}
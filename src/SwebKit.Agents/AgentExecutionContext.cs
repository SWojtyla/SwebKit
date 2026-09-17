namespace SwebKit.Agents;

public static class AgentExecutionContext
{
    private static readonly AsyncLocal<IReadOnlyDictionary<string, string>?> CurrentSelection = new();

    public static IReadOnlyDictionary<string, string>? Selection => CurrentSelection.Value;

    public static IDisposable Push(IReadOnlyDictionary<string, string>? selection)
    {
        var previous = CurrentSelection.Value;
        CurrentSelection.Value = selection;
        return new Scope(previous);
    }

    private sealed class Scope(IReadOnlyDictionary<string, string>? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            CurrentSelection.Value = previous;
            _disposed = true;
        }
    }
}

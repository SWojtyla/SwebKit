namespace SwebKit.Sidecar.Tests;

/// <summary>
/// Redirects SwebKit app data to a temp dir for the duration of a test to isolate file I/O.
/// The override is a process-wide environment variable, and xUnit runs different test classes in
/// parallel by default, so construction takes a static lock held until <see cref="Dispose"/> —
/// otherwise two sandboxes racing on different threads can clobber each other's environment variable
/// mid-test (one test's file I/O silently landing in, or reading from, another test's temp dir).
/// A <see cref="SemaphoreSlim"/> (not <c>lock</c>/<see cref="Monitor"/>) is used deliberately: async
/// test methods can resume a continuation on a different thread pool thread after an <c>await</c>, and
/// <see cref="Monitor"/> requires the same thread to enter and exit. This serializes every test that
/// uses this sandbox relative to every other such test, which is the correct (if conservative)
/// trade-off for a shared piece of process-global state.
/// </summary>
internal sealed class AppDataSandbox : IDisposable
{
    private const string AppDataRootOverrideVariable = "SWEBKIT_APPDATA_ROOT";
    private static readonly SemaphoreSlim Gate = new(1, 1);

    private readonly string? _originalRoot;
    private readonly string _tempRoot;
    private bool _disposed;

    public AppDataSandbox()
    {
        Gate.Wait();
        try
        {
            _originalRoot = Environment.GetEnvironmentVariable(AppDataRootOverrideVariable);
            _tempRoot = Path.Combine(Path.GetTempPath(), "SwebKit.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempRoot);
            Environment.SetEnvironmentVariable(AppDataRootOverrideVariable, _tempRoot);
        }
        catch
        {
            Gate.Release();
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        try
        {
            Environment.SetEnvironmentVariable(AppDataRootOverrideVariable, _originalRoot);
            // Journal writes can still be flushing when a test observes an op's terminal state
            // (the in-memory state flips before the file write lands) — retry the delete past
            // the brief window instead of failing on a locked tmp file.
            if (Directory.Exists(_tempRoot))
            {
                for (var attempt = 0; attempt < 20; attempt++)
                {
                    try
                    {
                        Directory.Delete(_tempRoot, recursive: true);
                        break;
                    }
                    catch (IOException) when (attempt < 19)
                    {
                        Thread.Sleep(25);
                    }
                }
            }
        }
        finally
        {
            Gate.Release();
        }
    }
}

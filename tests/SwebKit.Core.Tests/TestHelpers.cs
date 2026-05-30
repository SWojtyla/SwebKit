namespace SwebKit.Core.Tests;

/// <summary>Redirects SwebKit app data to a temp dir for the duration of a test to isolate file I/O.</summary>
internal sealed class AppDataSandbox : IDisposable
{
    private const string AppDataRootOverrideVariable = "SWEBKIT_APPDATA_ROOT";

    private readonly string? _originalRoot;
    private readonly string _tempRoot;

    public AppDataSandbox()
    {
        _originalRoot = Environment.GetEnvironmentVariable(AppDataRootOverrideVariable);
        _tempRoot = Path.Combine(Path.GetTempPath(), "SwebKit.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
        Environment.SetEnvironmentVariable(AppDataRootOverrideVariable, _tempRoot);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(AppDataRootOverrideVariable, _originalRoot);
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }
}

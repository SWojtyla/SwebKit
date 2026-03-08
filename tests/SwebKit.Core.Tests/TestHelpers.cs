namespace SwebKit.Core.Tests;

/// <summary>Redirects APPDATA to a temp dir for the duration of a test to isolate file I/O.</summary>
internal sealed class AppDataSandbox : IDisposable
{
    private readonly string? _originalAppData;
    private readonly string _tempRoot;

    public AppDataSandbox()
    {
        _originalAppData = Environment.GetEnvironmentVariable("APPDATA");
        _tempRoot = Path.Combine(Path.GetTempPath(), "SwebKit.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
        Environment.SetEnvironmentVariable("APPDATA", _tempRoot);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("APPDATA", _originalAppData);
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }
}

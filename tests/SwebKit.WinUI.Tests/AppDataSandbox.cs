namespace SwebKit.WinUI.Tests;

internal sealed class AppDataSandbox : IDisposable
{
    private readonly string? _originalOverrideRoot;
    private readonly string? _originalAppData;
    private readonly string _tempRoot;

    public AppDataSandbox()
    {
        _originalOverrideRoot = Environment.GetEnvironmentVariable("SWEBKIT_APPDATA_ROOT");
        _originalAppData = Environment.GetEnvironmentVariable("APPDATA");
        _tempRoot = Path.Combine(Path.GetTempPath(), "SwebKit.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
        Environment.SetEnvironmentVariable("SWEBKIT_APPDATA_ROOT", _tempRoot);
        Environment.SetEnvironmentVariable("APPDATA", _tempRoot);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("SWEBKIT_APPDATA_ROOT", _originalOverrideRoot);
        Environment.SetEnvironmentVariable("APPDATA", _originalAppData);

        if (!Directory.Exists(_tempRoot))
        {
            return;
        }

        try
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
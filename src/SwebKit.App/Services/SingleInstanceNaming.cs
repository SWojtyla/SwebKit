namespace SwebKit.App.Services;

/// <summary>
/// Pure, platform-neutral composition of the single-instance guard's OS object names
/// (named <see cref="System.Threading.Mutex"/> and activation named pipe). Kept free of any
/// Win32/WinRT types so it compiles for every target framework and is unit-testable off Windows.
/// The Windows-only guard supplies a per-user scope (SID) so names are isolated per interactive user.
/// </summary>
internal static class SingleInstanceNaming
{
    /// <summary>Stable, app-scoped base identifier. Changing this resets single-instance detection.</summary>
    internal const string BaseName = "SwebKit.App.SingleInstance";

    /// <summary>
    /// Builds the mutex name in the session-local namespace (<c>Local\</c>) so the guard does not
    /// require the global-object privilege. The user scope keeps distinct interactive users isolated.
    /// </summary>
    public static string ComposeMutexName(string userScope)
        => $@"Local\{BaseName}.Mutex.{Sanitize(userScope)}";

    /// <summary>
    /// Builds the activation pipe name. .NET prefixes <c>\\.\pipe\</c>; the pipe stays on the local
    /// machine and is further ACL-restricted to the current user by the Windows guard.
    /// </summary>
    public static string ComposePipeName(string userScope)
        => $"{BaseName}.Activation.{Sanitize(userScope)}";

    private static string Sanitize(string userScope)
        => string.IsNullOrWhiteSpace(userScope) ? "default" : userScope.Trim();
}

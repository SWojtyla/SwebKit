using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using SwebKit.App.Services;

namespace SwebKit.App.Platforms.Windows;

/// <summary>
/// Windows single-instance guard (A2 / DEC-2) for the unpackaged (<c>WindowsPackageType=None</c>)
/// build, where WinAppSDK <c>AppInstance</c> redirection is not the right fit.
///
/// Mechanism:
/// <list type="bullet">
///   <item>A named <see cref="Mutex"/> (session-local, per-user) is created as early as possible in
///   Windows startup. The first process creates it (primary); a later launch sees it already exists
///   (secondary).</item>
///   <item>The secondary sends a single, fixed activation signal over a per-user named pipe to ask the
///   primary to restore + focus its window, then exits before MAUI initializes.</item>
///   <item>The primary runs a background listener that invokes a supplied restore callback.</item>
/// </list>
///
/// Security: the activation pipe is a local IPC surface. It is created with a DACL that grants access
/// to <b>only the current user</b> (no Everyone/network), and the listener accepts <b>only</b> the fixed
/// <see cref="ActivationSignal"/> token — the payload is never interpreted as a command. Any pipe/listener
/// failure is swallowed so a failed signal can never crash either process (worst case the secondary
/// simply exits).
///
/// All Win32/pipe types stay inside this platform-specific type; shared code only sees
/// <see cref="SingleInstanceNaming"/> (pure strings).
/// </summary>
internal static class SingleInstanceGuard
{
    private const string ActivationSignal = "ACTIVATE";
    private const int ConnectTimeoutMs = 2000;

    private static readonly string UserScope = ResolveUserScope();
    private static readonly string MutexName = SingleInstanceNaming.ComposeMutexName(UserScope);
    private static readonly string PipeName = SingleInstanceNaming.ComposePipeName(UserScope);

    private static readonly object Sync = new();
    private static Mutex? _mutex;
    private static CancellationTokenSource? _listenerCts;

    /// <summary>
    /// Attempts to become the primary instance. Returns <c>true</c> if this is the first instance,
    /// <c>false</c> if another instance already owns the guard. Fails open (returns <c>true</c>) if the
    /// mutex cannot be created, so a guard fault never blocks the app from launching.
    /// </summary>
    public static bool TryAcquire()
    {
        lock (Sync)
        {
            try
            {
                // initiallyOwned:false — we only use the mutex as an existence flag and never call
                // ReleaseMutex (which is thread-affine). The named object lives while any handle is
                // open and is freed when we Dispose the handle on exit, so a later relaunch starts clean.
                _mutex = new Mutex(initiallyOwned: false, MutexName, out var createdNew);
                return createdNew;
            }
            catch
            {
                _mutex = null;
                return true;
            }
        }
    }

    /// <summary>
    /// Secondary instance: best-effort signal to the primary to restore + focus. Never throws.
    /// </summary>
    public static void SignalPrimaryInstance()
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(ConnectTimeoutMs);
            using var writer = new StreamWriter(client) { AutoFlush = true };
            writer.WriteLine(ActivationSignal);
        }
        catch
        {
            // A failed signal must not crash the secondary — it will just exit.
        }
    }

    /// <summary>
    /// Primary instance: start listening for activation signals. <paramref name="onActivate"/> is invoked
    /// on the listener thread when a valid signal arrives; the caller marshals to the UI thread.
    /// Safe to call once after the window/tray exists. Never throws.
    /// </summary>
    public static void StartActivationListener(Action onActivate)
    {
        ArgumentNullException.ThrowIfNull(onActivate);

        lock (Sync)
        {
            if (_listenerCts is not null)
            {
                return;
            }

            _listenerCts = new CancellationTokenSource();
        }

        var token = _listenerCts.Token;
        _ = Task.Run(() => ListenLoopAsync(onActivate, token));
    }

    /// <summary>
    /// Releases the guard on true exit: stops the listener and closes the mutex handle so the next
    /// launch can become primary. Idempotent and safe from any thread.
    /// </summary>
    public static void Release()
    {
        CancellationTokenSource? cts;
        Mutex? mutex;

        lock (Sync)
        {
            cts = _listenerCts;
            _listenerCts = null;
            mutex = _mutex;
            _mutex = null;
        }

        try { cts?.Cancel(); } catch { /* teardown best-effort */ }
        try { cts?.Dispose(); } catch { /* teardown best-effort */ }
        // Closing the handle frees the named mutex; no ReleaseMutex (would require the acquiring thread).
        try { mutex?.Dispose(); } catch { /* teardown best-effort */ }
    }

    private static async Task ListenLoopAsync(Action onActivate, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                using var server = CreateCurrentUserPipeServer();
                await server.WaitForConnectionAsync(token).ConfigureAwait(false);

                using var reader = new StreamReader(server);
                var line = await reader.ReadLineAsync(token).ConfigureAwait(false);

                // Only the fixed activation token is honored; any other payload is ignored (no command exec).
                if (string.Equals(line?.Trim(), ActivationSignal, StringComparison.Ordinal))
                {
                    onActivate();
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // Never let a pipe fault crash the primary. Back off briefly to avoid a hot loop.
                try
                {
                    await Task.Delay(500, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private static NamedPipeServerStream CreateCurrentUserPipeServer()
    {
        var security = new PipeSecurity();
        using var identity = WindowsIdentity.GetCurrent();
        var owner = (SecurityIdentifier)identity.User!;

        // Grant the current user only — no Everyone, no network principals.
        security.AddAccessRule(new PipeAccessRule(
            owner,
            PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance,
            AccessControlType.Allow));

        return NamedPipeServerStreamAcl.Create(
            PipeName,
            PipeDirection.In,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            inBufferSize: 0,
            outBufferSize: 0,
            security);
    }

    private static string ResolveUserScope()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return identity.User?.Value ?? "default";
        }
        catch
        {
            return "default";
        }
    }
}

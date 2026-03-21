# Backend Plan — AKS Port-Forward Sessions Panel

## New types

### `PortForwardSession` (in `SwebKit.Core/Models/AksModels.cs` or new file)

```csharp
public record PortForwardSession
{
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public string PodName { get; init; } = "";
    public string Namespace { get; init; } = "";
    public int RemotePort { get; init; }
    public int LocalPort { get; init; }
    public DateTime StartedAt { get; init; } = DateTime.UtcNow;
    public PortForwardStatus Status { get; set; } = PortForwardStatus.Starting;
    public string? ErrorMessage { get; set; }
}

public enum PortForwardStatus { Starting, Active, Stopping, Stopped, Error }
```

### `IPortForwardSessionService` (in `SwebKit.Core/Services/`)

```csharp
public interface IPortForwardSessionService
{
    IReadOnlyList<PortForwardSession> Sessions { get; }
    IReadOnlyList<PortForwardSession> ActiveSessions { get; }
    event Action? SessionsChanged;

    Task<PortForwardSession> StartAsync(string podName, string ns, int remotePort, int localPort);
    Task StopAsync(string sessionId);
    void StopAll();
}
```

### `PortForwardSessionService` (in `SwebKit.Kubernetes/Services/`)

- Holds a `ConcurrentDictionary<string, (PortForwardSession Session, CancellationTokenSource Cts)>`
- `StartAsync`: creates session, fires `SessionsChanged`, calls `IAksClient.PortForwardAsync` on a background task, updates status to `Active` on success or `Error` on failure
- `StopAsync`: sets status to `Stopping`, cancels the CTS, waits for process to exit, sets status to `Stopped`, fires `SessionsChanged`
- `StopAll`: calls `StopAsync` on every non-stopped session — called from `MauiProgram` application lifecycle

## `IAksClient` changes

Current signature (likely):
```csharp
Task PortForwardAsync(string podName, string ns, int remotePort, int localPort);
```

Updated:
```csharp
Task PortForwardAsync(string podName, string ns, int remotePort, int localPort, CancellationToken cancellationToken = default);
```

`KubernetesAksClient.PortForwardAsync` must:
- Start the `kubectl port-forward` process
- Monitor stdout for "Forwarding from" confirmation → signal `Active`
- Monitor stderr and process exit → signal `Error` with last stderr line
- Honour cancellation: `cancellationToken.Register(() => process.Kill(entireProcessTree: true))`

## `DemoAksClient` changes

`PortForwardAsync` in demo mode: simulate a 500ms delay then return (no real process). The session service sets status to `Active` after the delay.

## Registration (`MauiProgram.cs`)

```csharp
builder.Services.AddSingleton<IPortForwardSessionService, PortForwardSessionService>();
```

App lifecycle teardown:
```csharp
var sessions = app.Services.GetRequiredService<IPortForwardSessionService>();
app.Lifetime.ApplicationStopping.Register(() => sessions.StopAll());
```

## Affected files

- `src/SwebKit.Core/Models/AksModels.cs` — new `PortForwardSession`, `PortForwardStatus`
- `src/SwebKit.Core/Services/IPortForwardSessionService.cs` — new
- `src/SwebKit.Kubernetes/Services/PortForwardSessionService.cs` — new
- `src/SwebKit.Kubernetes/KubernetesAksClient.cs` — update `PortForwardAsync` signature
- `src/SwebKit.Azure/Demo/DemoAksClient.cs` — update `PortForwardAsync`
- `src/SwebKit.App/MauiProgram.cs` — register service + lifecycle teardown

## Tasks

- [ ] Add `PortForwardSession` and `PortForwardStatus` models
- [ ] Define `IPortForwardSessionService`
- [ ] Implement `PortForwardSessionService` with process lifecycle management
- [ ] Update `IAksClient.PortForwardAsync` signature
- [ ] Update `KubernetesAksClient.PortForwardAsync` (process monitoring, kill on cancel)
- [ ] Update `DemoAksClient.PortForwardAsync`
- [ ] Register in `MauiProgram.cs` with lifecycle teardown
- [ ] Unit tests for `PortForwardSessionService` state machine

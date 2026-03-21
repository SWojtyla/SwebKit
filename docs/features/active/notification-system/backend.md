# Backend Plan — Global Notification System

## New types

### `Notification` (in `SwebKit.Core/Models/`)

```csharp
public record Notification
{
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public NotificationSeverity Severity { get; init; }
    public string Message { get; init; } = "";
    public string? Detail { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public bool IsDismissed { get; set; }
}

public enum NotificationSeverity { Success, Info, Warning, Error }
```

### `INotificationService` (in `SwebKit.Core/Services/`)

```csharp
public interface INotificationService
{
    IReadOnlyList<Notification> All { get; }
    event Action? NotificationsChanged;

    void ShowSuccess(string message, string? detail = null);
    void ShowInfo(string message, string? detail = null);
    void ShowWarning(string message, string? detail = null);
    void ShowError(string message, string? detail = null, Exception? ex = null);
    void Dismiss(string id);
    void DismissAll();
}
```

### `NotificationService` (in `SwebKit.App/Services/` or `SwebKit.Core/Services/`)

- `List<Notification>` protected by a `Lock` (or `lock` on the list)
- `ShowError` appends `ex?.Message` to `detail` if both are provided
- `NotificationsChanged` fired after every mutation using `InvokeAsync` pattern from `MainLayout`
- `All` returns an immutable snapshot (`ToList().AsReadOnly()`)
- No auto-expiry in the service — expiry is managed by the UI component

## Registration (`MauiProgram.cs`)

```csharp
builder.Services.AddSingleton<INotificationService, NotificationService>();
```

## Affected files

- `src/SwebKit.Core/Models/Notification.cs` — new
- `src/SwebKit.Core/Services/INotificationService.cs` — new
- `src/SwebKit.App/Services/NotificationService.cs` — new
- `src/SwebKit.App/MauiProgram.cs` — register singleton
- All feature pages — inject and call `INotificationService`

## Tasks

- [ ] Create `Notification` model + `NotificationSeverity` enum
- [ ] Define `INotificationService`
- [ ] Implement `NotificationService` (thread-safe list, event firing)
- [ ] Register in `MauiProgram.cs`
- [ ] Inject and integrate in Service Bus page (sent, resubmitted, scheduled cancelled)
- [ ] Inject and integrate in AKS page (restarted, deleted, port-forward started/stopped, error)
- [ ] Inject and integrate in Redis page (deleted, TTL set, value saved, DB flushed)
- [ ] Inject and integrate in Storage page (downloaded, SAS URL copied)
- [ ] Inject and integrate in Releases page (approval submitted, deployment triggered)
- [ ] Unit tests for `NotificationService`

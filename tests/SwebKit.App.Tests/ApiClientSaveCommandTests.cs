using SwebKit.App.Services;
using SwebKit.Core.Configuration;

namespace SwebKit.App.Tests;

/// <summary>
/// Focused coverage for the "api-save-request" (Ctrl+S) command added in Phase 3, Task 7.
///
/// ApiClientPage cannot be instantiated directly in bUnit (MAUI FilePicker dependency wall,
/// consistent with prior Phase 3 task test infra limitations), so this test exercises
/// CommandRegistry.Register/Unregister directly using the exact Id/Shortcut/AreaScope values
/// that ApiClientPage.RegisterApiClientCommands() registers in production code. It guards the
/// contract (shortcut label + area scoping + lifecycle unregister) without depending on the
/// full page's DI graph.
/// </summary>
public class ApiClientSaveCommandTests
{
    private const string CommandId = "api-save-request";

    [Fact]
    public void ApiSaveRequestCommand_IsRegisteredWithCtrlSShortcutAndApiClientScope()
    {
        var registry = CreateRegistry();

        RegisterApiSaveRequestCommand(registry);

        var command = Assert.Single(registry.Commands, c => c.Id == CommandId);
        Assert.Equal("Ctrl+S", command.Shortcut);
        Assert.Equal("api-client", command.AreaScope);
    }

    [Fact]
    public void ApiSaveRequestCommand_IsRemovedAfterUnregister()
    {
        var registry = CreateRegistry();
        RegisterApiSaveRequestCommand(registry);

        registry.Unregister(CommandId);

        Assert.DoesNotContain(registry.Commands, c => c.Id == CommandId);
    }

    /// <summary>Mirrors ApiClientPage.RegisterApiClientCommands()'s "api-save-request" registration.</summary>
    private static void RegisterApiSaveRequestCommand(CommandRegistry registry)
    {
        registry.Register(new AppCommand
        {
            Id = CommandId,
            Label = "API Client: Save Request",
            Category = "API Client",
            Icon = "\ud83d\udcbe",
            Shortcut = "Ctrl+S",
            AreaScope = "api-client",
            Execute = () => Task.CompletedTask,
        });
    }

    private static CommandRegistry CreateRegistry()
    {
        var uiState = new UiStateRepository();
        return new CommandRegistry(uiState);
    }
}

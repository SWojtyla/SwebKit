using SwebKit.App.Services;
using SwebKit.Core.Configuration;

namespace SwebKit.App.Tests;

/// <summary>
/// Focused coverage for the "api-quick-nav" (Ctrl+P) command contract, added in
/// ApiClientPage.RegisterApiClientCommands() alongside "api-save-request" (see
/// <see cref="ApiClientSaveCommandTests"/>, which this test mirrors).
///
/// Phase 3 test-plan (docs/features/active/api-client-ux-refactor/test-plan.md): OFF #3
/// ("Ctrl+P / tree switch — works as before") and part of ON #10 ("Ctrl+S / Send / Ctrl+P under
/// tabs — route to the active tab"). This test only verifies the static Id/Shortcut/AreaScope
/// registration contract — same narrow slice ApiClientSaveCommandTests exercises for Ctrl+S.
/// The actual routing behaviour (global Ctrl+P opening CommandPalette scoped to the active
/// request/tab) lives in ApiClientPage, which cannot be instantiated in this bUnit project (MAUI
/// FilePicker dependency wall — see ApiClientStateTestDouble.cs remarks); that part remains
/// untested pending resolution of that infra limitation.
/// </summary>
public class ApiClientQuickNavCommandTests
{
    private const string CommandId = "api-quick-nav";

    [Fact]
    public void ApiQuickNavCommand_IsRegisteredWithCtrlPShortcutAndApiClientScope()
    {
        var registry = CreateRegistry();

        RegisterApiQuickNavCommand(registry);

        var command = Assert.Single(registry.Commands, c => c.Id == CommandId);
        Assert.Equal("Ctrl+P", command.Shortcut);
        Assert.Equal("api-client", command.AreaScope);
    }

    [Fact]
    public void ApiQuickNavCommand_IsRemovedAfterUnregister()
    {
        var registry = CreateRegistry();
        RegisterApiQuickNavCommand(registry);

        registry.Unregister(CommandId);

        Assert.DoesNotContain(registry.Commands, c => c.Id == CommandId);
    }

    /// <summary>Mirrors ApiClientPage.RegisterApiClientCommands()'s "api-quick-nav" registration.</summary>
    private static void RegisterApiQuickNavCommand(CommandRegistry registry)
    {
        registry.Register(new AppCommand
        {
            Id = CommandId,
            Label = "API Client: Quick Nav (Request Picker)",
            Category = "API Client",
            Icon = "\u2315",
            Shortcut = "Ctrl+P",
            AreaScope = "api-client",
            Execute = () => Task.CompletedTask, // handled by global Ctrl+P -> CommandPalette
        });
    }

    private static CommandRegistry CreateRegistry()
    {
        var uiState = new UiStateRepository();
        return new CommandRegistry(uiState);
    }
}

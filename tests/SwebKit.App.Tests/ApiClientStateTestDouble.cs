namespace SwebKit.App.Components.ApiClient;

/// <summary>
/// Test-only stand-in for the real <c>ApiClientState</c> (see class-level remarks on
/// <see cref="SwebKit.App.Tests.ApiClientOpenTabsStripTests"/>). The real type can't be linked
/// into this net10.0 test project: it declares an <c>internal ApiClientPage.LinkedSaveConflict?</c>
/// member, and <c>ApiClientPage.razor</c> transitively renders
/// <c>CollectionExportDialog</c>/<c>RequestBuilderPanel</c>, both of which use MAUI-only
/// <c>FilePicker</c> APIs unavailable outside the MAUI-targeted app project. This stand-in exposes
/// only the two members <c>ApiClientOpenTabsStrip</c> actually reads, so the freshly-recompiled
/// component in this test assembly has an <c>ApiClientState</c> type to bind its <c>State</c>
/// parameter against. Not shared with, or referenced by, production code.
/// </summary>
public sealed class ApiClientState
{
    public List<ApiClientOpenTab> OpenTabs { get; set; } = [];
    public string? ActiveTabRequestId { get; set; }
}

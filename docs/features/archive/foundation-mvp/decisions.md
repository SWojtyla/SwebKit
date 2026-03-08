# Decisions - Foundation and MVP

- **Per-connection service clients**: `IServiceBusClient`, `IObservabilityProvider`, `IAksClient` are instantiated per namespace/environment on pages rather than registered as DI singletons. Rationale: each environment has different connection config; singleton registration would require a factory or scoped pattern that adds complexity without benefit.
- **JSON file persistence**: `ProfileRepository` stores projects, namespaces, and templates in a single JSON file at `%LocalAppData%\SwebKit\profiles.json`. Rationale: simple, human-readable, no database dependency for a desktop app.
- **Windows Credential Manager**: `WindowsCredentialStore` wraps `PasswordVault` with `SwebKit:` key prefix. Rationale: OS-level secret storage, no plaintext secrets on disk.
- **Blazor Router for navigation**: Uses `NavigationManager` instead of MAUI Shell routing. Rationale: entire UI is Razor components in `BlazorWebView`; MAUI Shell routing would require XAML pages.
- **Event bus for cross-component communication**: `IAppEventBus` with typed events (`ProjectChangedEvent`, `EnvironmentChangedEvent`, etc.) instead of cascading callbacks. Rationale: decouples components, avoids deep callback chains.

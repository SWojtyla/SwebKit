namespace SwebKit.Core.Configuration;

/// <summary>
/// Configuration options for render coalescing behavior across components.
/// </summary>
public record RenderCoalescingOptions
{
    /// <summary>
    /// Default debounce window in milliseconds for render coalescing.
    /// </summary>
    public int DefaultDebounceMs { get; init; } = 75;

    /// <summary>
    /// Per-component debounce overrides. Key is component type name.
    /// </summary>
    public Dictionary<string, int> ComponentOverrides { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Environment-specific presets for different deployment scenarios.
    /// </summary>
    public Dictionary<string, RenderCoalescingEnvironmentPreset> EnvironmentPresets { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Environment-specific render coalescing preset configuration.
/// </summary>
public record RenderCoalescingEnvironmentPreset
{
    /// <summary>
    /// Default debounce for this environment.
    /// </summary>
    public int DefaultDebounceMs { get; init; } = 75;

    /// <summary>
    /// Component-specific overrides for this environment.
    /// </summary>
    public Dictionary<string, int> ComponentOverrides { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

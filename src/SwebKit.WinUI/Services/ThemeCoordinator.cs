using Microsoft.UI.Xaml;
using Microsoft.Win32;
using SwebKit.Core.Configuration;

namespace SwebKit.WinUI.Services;

public sealed class ThemeCoordinator
{
    private const string DefaultThemeKey = "dark-studio-ledger";
    private const string SystemThemeKey = "system";

    private static readonly IReadOnlyDictionary<string, ThemeDefinition> Themes =
        new Dictionary<string, ThemeDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["dark-studio-ledger"] = new(
                "dark-studio-ledger",
                "Dark - Studio Ledger",
                "ms-appx:///Resources/Themes/DarkStudioLedger.xaml",
                ElementTheme.Dark),
            ["dark-iron-noir"] = new(
                "dark-iron-noir",
                "Dark - Iron Noir",
                "ms-appx:///Resources/Themes/DarkIronNoir.xaml",
                ElementTheme.Dark),
            ["dark-command-deck"] = new(
                "dark-command-deck",
                "Dark - Command Deck",
                "ms-appx:///Resources/Themes/DarkCommandDeck.xaml",
                ElementTheme.Dark),
            ["light-cloud-paper"] = new(
                "light-cloud-paper",
                "Light - Cloud Paper",
                "ms-appx:///Resources/Themes/LightCloudPaper.xaml",
                ElementTheme.Light),
            ["light-sand-dune"] = new(
                "light-sand-dune",
                "Light - Sand Dune",
                "ms-appx:///Resources/Themes/LightSandDune.xaml",
                ElementTheme.Light),
            ["light-portima"] = new(
                "light-portima",
                "Light - Portima",
                "ms-appx:///Resources/Themes/LightPortima.xaml",
                ElementTheme.Light),
        };

    private static readonly IReadOnlyDictionary<string, string> Aliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [""] = SystemThemeKey,
            ["system"] = SystemThemeKey,
            ["dark"] = "dark-studio-ledger",
            ["light"] = "light-cloud-paper",
            ["dark-control-room"] = "dark-studio-ledger",
            ["dark-technical-editorial"] = "dark-iron-noir",
            ["light-azure-bloom"] = "light-cloud-paper",
            ["light-coral-studio"] = "light-sand-dune",
            ["light-forest-dev"] = "light-portima",
            ["light-violet-cloud"] = "light-cloud-paper",
        };

    private readonly UserSettingsRepository _userSettings;
    private FrameworkElement? _shellRoot;

    public ThemeCoordinator(UserSettingsRepository userSettings)
    {
        _userSettings = userSettings;
        ThemeOptions =
        [
            new ThemeOption(SystemThemeKey, "System - follow Windows"),
            .. Themes.Values.Select(static definition => new ThemeOption(definition.Key, definition.Label)),
        ];
    }

    public IReadOnlyList<ThemeOption> ThemeOptions { get; }

    public string CurrentThemeKey { get; private set; } = DefaultThemeKey;

    public void AttachShellRoot(FrameworkElement shellRoot)
    {
        _shellRoot = shellRoot;
        ApplyRequestedTheme();
    }

    public string NormalizeThemeKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return SystemThemeKey;
        }

        if (string.Equals(key, SystemThemeKey, StringComparison.OrdinalIgnoreCase) || Themes.ContainsKey(key))
        {
            return key;
        }

        return Aliases.TryGetValue(key, out var normalized)
            ? normalized
            : DefaultThemeKey;
    }

    public void ApplyTheme(string? requestedThemeKey)
    {
        var normalizedThemeKey = NormalizeThemeKey(requestedThemeKey);
        var definition = ResolveThemeDefinition(normalizedThemeKey);

        ReplaceThemeDictionary(definition.DictionaryUri);
        CurrentThemeKey = normalizedThemeKey;

        ApplyRequestedTheme();
    }

    private void ApplyRequestedTheme()
    {
        if (_shellRoot is null)
        {
            return;
        }

        var definition = ResolveThemeDefinition(NormalizeThemeKey(CurrentThemeKey));
        _shellRoot.RequestedTheme = definition.RequestedTheme;
    }

    private static ThemeDefinition ResolveThemeDefinition(string requestedThemeKey)
    {
        if (!string.Equals(requestedThemeKey, SystemThemeKey, StringComparison.OrdinalIgnoreCase))
        {
            return Themes[requestedThemeKey];
        }

        return PrefersLightTheme()
            ? Themes["light-cloud-paper"]
            : Themes[DefaultThemeKey];
    }

    private static bool PrefersLightTheme()
    {
        using var personalizeKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");

        return personalizeKey?.GetValue("AppsUseLightTheme") switch
        {
            int intValue => intValue > 0,
            byte byteValue => byteValue > 0,
            _ => false,
        };
    }

    private static void ReplaceThemeDictionary(string dictionaryUri)
    {
        var mergedDictionaries = Application.Current.Resources.MergedDictionaries;

        for (var index = mergedDictionaries.Count - 1; index >= 0; index--)
        {
            var source = mergedDictionaries[index].Source?.OriginalString;
            if (source is not null && source.Contains("/Resources/Themes/", StringComparison.OrdinalIgnoreCase))
            {
                mergedDictionaries.RemoveAt(index);
            }
        }

        mergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri(dictionaryUri),
        });
    }

    private sealed record ThemeDefinition(
        string Key,
        string Label,
        string DictionaryUri,
        ElementTheme RequestedTheme);
}

public sealed record ThemeOption(string Key, string Label);
using SwebKit.Core.Configuration;

namespace SwebKit.App.Services;

public sealed class PinnedPortForwardService(UserSettingsRepository settingsRepo)
{
    private const int MaxPinsPerContext = 20;

    public IReadOnlyList<PinnedPortForwardEntry> GetPins(string kubeconfigContext)
    {
        if (string.IsNullOrEmpty(kubeconfigContext)) return [];
        return settingsRepo.Settings.PinnedPortForwards.TryGetValue(kubeconfigContext, out var pins)
            ? pins.AsReadOnly()
            : [];
    }

    public async Task AddPinAsync(string kubeconfigContext, PinnedPortForwardEntry entry)
    {
        if (string.IsNullOrEmpty(kubeconfigContext)) return;

        var pins = settingsRepo.Settings.PinnedPortForwards;
        if (!pins.TryGetValue(kubeconfigContext, out var list))
        {
            list = [];
            pins[kubeconfigContext] = list;
        }

        // Remove exact duplicates (same pod selector + ports)
        list.RemoveAll(p => p.PodLabelSelector == entry.PodLabelSelector
                         && p.RemotePort == entry.RemotePort
                         && p.LocalPort == entry.LocalPort);

        list.Add(entry);

        // Enforce cap — evict oldest
        while (list.Count > MaxPinsPerContext)
            list.RemoveAt(0);  // list is ordered oldest-first by PinnedAt

        await settingsRepo.SaveAsync();
    }

    public async Task RemovePinAsync(string kubeconfigContext, PinnedPortForwardEntry entry)
    {
        if (string.IsNullOrEmpty(kubeconfigContext)) return;

        if (settingsRepo.Settings.PinnedPortForwards.TryGetValue(kubeconfigContext, out var list))
        {
            list.RemoveAll(p => p.Label == entry.Label
                             && p.PodLabelSelector == entry.PodLabelSelector
                             && p.RemotePort == entry.RemotePort
                             && p.LocalPort == entry.LocalPort);
            await settingsRepo.SaveAsync();
        }
    }
}

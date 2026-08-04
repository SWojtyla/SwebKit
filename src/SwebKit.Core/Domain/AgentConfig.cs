namespace SwebKit.Core.Domain;

/// <summary>
/// Per-user configuration for the AI agent feature.
/// Stored in <see cref="SwebKit.Core.Configuration.UserSettings"/> (user-scoped, not per-workspace).
/// API keys are NOT stored here — profiles reference a logical <see cref="AgentProfile.CredentialKey"/>
/// resolved via <c>ICredentialStore</c> at runtime.
/// </summary>
public sealed class AgentConfig
{
    /// <summary>Show the AI Agent section in the navigation.</summary>
    public bool IsEnabled { get; set; }

    /// <summary>
    /// List of configured provider profiles. The active one is selected by
    /// <see cref="ActiveProfileId"/>. If empty, a default LM Studio profile is created.
    /// </summary>
    public List<AgentProfile> Profiles { get; set; } = [];

    /// <summary>
    /// ID of the active profile (must match an <see cref="AgentProfile.Id"/> in
    /// <see cref="Profiles"/>). Empty string means no active profile.
    /// </summary>
    public string ActiveProfileId { get; set; } = string.Empty;

    // ── Legacy fields (kept for migration, not used after migration) ──

    /// <summary>
    /// Legacy: Override the Mistral model. Migrated into a Mistral profile.
    /// </summary>
    public string ModelOverride { get; set; } = string.Empty;

    // ── Migration ──

    private bool _migrated;

    /// <summary>
    /// Migrates legacy single-provider configuration (ModelOverride + credential key
    /// "SwebKit-Agent:Mistral-ApiKey") into a Mistral profile if no profiles exist yet.
    /// Also ensures there is a valid active profile. Idempotent.
    /// </summary>
    public void Migrate()
    {
        if (_migrated) return;
        _migrated = true;

        // If profiles already exist, just ensure ActiveProfileId is valid.
        if (Profiles.Count > 0)
        {
            if (string.IsNullOrEmpty(ActiveProfileId) ||
                        !Profiles.Any(p => p.Id == ActiveProfileId))
            {
                ActiveProfileId = Profiles[0].Id;
            }
            return;
        }

        // No profiles — migrate from legacy ModelOverride or create a default LM Studio profile.
        if (!string.IsNullOrEmpty(ModelOverride))
        {
            // User had a Mistral model override → create a Mistral profile.
            var mistralProfile = AgentProfilePresets.Mistral();
            mistralProfile.Model = ModelOverride;
            Profiles.Add(mistralProfile);
            ActiveProfileId = mistralProfile.Id;
        }
        else
        {
            // Default: LM Studio local (no key required).
            var lmStudioProfile = AgentProfilePresets.LmStudio();
            Profiles.Add(lmStudioProfile);
            ActiveProfileId = lmStudioProfile.Id;
        }
    }

    /// <summary>
    /// Returns the active profile, or null if none is configured.
    /// Calls <see cref="Migrate"/> first to ensure profiles are initialized.
    /// </summary>
    public AgentProfile? GetActiveProfile()
    {
        Migrate();
        return Profiles.FirstOrDefault(p => p.Id == ActiveProfileId);
    }
}

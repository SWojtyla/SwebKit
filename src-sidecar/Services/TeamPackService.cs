using System.Text.Json;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Core.Models;
using SwebKit.Core.Serialization;
using SwebKit.Core.Services;

namespace SwebKit.Sidecar.Services;

/// <summary>
/// Export/import for "team packs" — the shareable subset of <see cref="ConfigurationBundleService"/>'s
/// full bundle. Export is sanitized by construction: credential secrets are stripped, connection ids
/// degrade to portable hints, demo-overlay entities can never land in a pack, and secret references
/// surface as a name-only <see cref="TeamPack.CredentialRefs"/> manifest. Import supports
/// <see cref="TeamPackImportOptions.DryRun"/> previews and merge/replace per section.
/// </summary>
public sealed class TeamPackService
{
    private static readonly JsonSerializerOptions Options = new(SwebKitJsonOptions.Indented)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    private readonly ProfileRepository _profiles;
    private readonly SqlQueryRepository _sqlQueries;
    private readonly IAlertRuleRepository _alertRules;
    private readonly CollectionRepository _collections;
    private readonly EnvironmentRepository _environments;
    private readonly LinkedCollectionRootRepository _linkedRoots;
    private readonly ChainRepository _chains;
    private readonly DemoModeService _demo;

    public TeamPackService(
        ProfileRepository profiles,
        SqlQueryRepository sqlQueries,
        IAlertRuleRepository alertRules,
        CollectionRepository collections,
        EnvironmentRepository environments,
        LinkedCollectionRootRepository linkedRoots,
        ChainRepository chains,
        DemoModeService demo)
    {
        _profiles = profiles;
        _sqlQueries = sqlQueries;
        _alertRules = alertRules;
        _collections = collections;
        _environments = environments;
        _linkedRoots = linkedRoots;
        _chains = chains;
        _demo = demo;
    }

    // ── Export ───────────────────────────────────────────────────────────────

    /// <param name="sections">Null or empty means "every section".</param>
    public async Task<TeamPack> ExportAsync(IReadOnlySet<string>? sections = null)
    {
        var profile = _profiles.GetProfileData();
        var pack = new TeamPack { Name = profile.Config.Name };
        var include = (string section) => sections is null || sections.Count == 0 || sections.Contains(section);

        if (include(TeamPackSections.Maps))
        {
            // EffectiveMaps folds in the legacy Topology pseudo-map so a pack exported from a
            // not-yet-migrated profile still carries the graph.
            pack.Maps = profile.Config.EffectiveMaps().Select(Clone).ToList();
        }

        if (include(TeamPackSections.SqlQueries))
        {
            var connectionsById = (profile.Config.SqlConfig?.Connections ?? [])
                .Where(c => c.Id is not (DemoModeService.DemoSqlConnectionId
                    or DemoModeService.DemoSqlConnectionId2
                    or DemoModeService.DemoSqlConnectionIdRestricted))
                .ToDictionary(c => c.Id, StringComparer.Ordinal);

            pack.SavedSqlQueries = (await _sqlQueries.GetQueriesAsync().ConfigureAwait(false))
                .Select(q => new TeamPackSqlQuery
                {
                    Name = q.Name,
                    Folder = q.Folder,
                    Sql = q.Sql,
                    ConnectionHint = q.ConnectionId is { } id && connectionsById.TryGetValue(id, out var conn)
                        ? new TeamPackConnectionHint { Server = conn.Server, Database = conn.Database }
                        : null,
                })
                .ToList();
        }

        if (include(TeamPackSections.AlertRules))
        {
            // Runtime fields (evaluation/firing timestamps, mute windows) are local-machine state,
            // not part of the shared rule definition — cleared so a teammate's engine starts clean.
            pack.AlertRules = (await _alertRules.GetAllAsync().ConfigureAwait(false))
                .Select(Clone)
                .Select(r =>
                {
                    r.LastEvaluatedAt = null;
                    r.LastFiredAt = null;
                    r.MutedUntil = null;
                    return r;
                })
                .ToList();
        }

        if (include(TeamPackSections.Collections))
        {
            var store = Clone(new CollectionsStore
            {
                SchemaVersion = 1,
                Collections = _collections.Collections
                    .Where(c => c.Id != DemoApiCollectionFactory.DemoCollectionId)
                    .ToList(),
            });
            StripCredentialSecrets(store);
            pack.CollectionsData = store;
        }

        if (include(TeamPackSections.Environments))
        {
            // UiState (active selections, last-opened request) references local ids — never exported.
            pack.EnvironmentsData = Clone(new EnvironmentsStore
            {
                SchemaVersion = 1,
                Environments = _environments.Environments.ToList(),
            });
        }

        if (include(TeamPackSections.LinkedRoots))
        {
            pack.LinkedRoots = _linkedRoots.Roots
                .Select(r => new TeamPackLinkedRoot
                {
                    Name = r.Name,
                    Path = r.Path,
                    IsEnabled = r.IsEnabled,
                    BrunoSyncFolderPath = r.BrunoSyncFolderPath,
                    BrunoSyncEnabled = r.BrunoSyncEnabled,
                })
                .ToList();
        }

        if (include(TeamPackSections.ApiChains))
        {
            // Chains are pure references + metadata — no auth config, no variables — so there's
            // nothing to strip; the demo chain is synthetic and never reaches the persisted store.
            await _chains.LoadAsync().ConfigureAwait(false);
            pack.ApiChains = Clone(_chains.Chains.ToList());
        }

        pack.CredentialRefs = CollectCredentialRefs(pack);
        pack.Warnings = CollectWarnings(pack);
        return pack;
    }

    public string Serialize(TeamPack pack) => JsonSerializer.Serialize(pack, Options);

    public TeamPack Deserialize(string json)
    {
        var pack = JsonSerializer.Deserialize<TeamPack>(json, Options)
            ?? throw new InvalidOperationException("Team pack could not be deserialized.");

        if (!string.Equals(pack.Format, TeamPack.ExpectedFormat, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Not a SwebKit team pack (format '{pack.Format ?? "(missing)"}' — expected '{TeamPack.ExpectedFormat}').");
        }

        if (pack.SchemaVersion is < 1 or > 1)
        {
            throw new InvalidOperationException($"Unsupported team pack schema version '{pack.SchemaVersion}'.");
        }

        return pack;
    }

    // ── Import ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Applies (or previews, when <see cref="TeamPackImportOptions.DryRun"/>) a pack against the
    /// current state. Dry runs touch nothing: every section computes against cloned data.
    /// </summary>
    public async Task<TeamPackImportResult> ImportAsync(TeamPack pack, TeamPackImportOptions options)
    {
        var result = new TeamPackImportResult
        {
            DryRun = options.DryRun,
            RequiredCredentialRefs = pack.CredentialRefs ?? [],
            Warnings = pack.Warnings ?? [],
        };
        var replace = options.Strategy == TeamPackMergeStrategy.Replace;

        if (pack.Maps is not null)
            await ImportMapsAsync(pack.Maps, replace, options.DryRun, result).ConfigureAwait(false);

        var collectionIdRemap = new Dictionary<string, string>(StringComparer.Ordinal);
        if (pack.CollectionsData is not null)
            await ImportCollectionsAsync(pack.CollectionsData, replace, options.DryRun, result, collectionIdRemap).ConfigureAwait(false);

        if (pack.EnvironmentsData is not null)
            await ImportEnvironmentsAsync(pack.EnvironmentsData, replace, options.DryRun, result, collectionIdRemap).ConfigureAwait(false);

        if (pack.SavedSqlQueries is not null)
            await ImportSqlQueriesAsync(pack.SavedSqlQueries, replace, options.DryRun, result).ConfigureAwait(false);

        if (pack.AlertRules is not null)
            await ImportAlertRulesAsync(pack.AlertRules, replace, options.DryRun, result).ConfigureAwait(false);

        if (pack.LinkedRoots is not null)
            await ImportLinkedRootsAsync(pack.LinkedRoots, replace, options.DryRun, result).ConfigureAwait(false);

        // Chains import last so step collectionIds rebind onto whatever the collections section
        // minted (or an existing same-named collection). Request ids travel verbatim — collection
        // import preserves them, so a chain over imported collections keeps working.
        if (pack.ApiChains is not null)
            await ImportChainsAsync(pack.ApiChains, replace, options.DryRun, result, collectionIdRemap).ConfigureAwait(false);

        return result;
    }

    /// <summary>Merge keys by map name — a teammate's "orders" map updates the local one.</summary>
    private async Task ImportMapsAsync(List<WorkspaceMap> packMaps, bool replace, bool dryRun, TeamPackImportResult result)
    {
        var data = Clone(_profiles.GetProfileData());
        var maps = data.Config.Maps;

        if (replace)
        {
            result.Skipped += maps.Count;
            maps.Clear();
        }

        foreach (var incoming in packMaps)
        {
            incoming.Nodes ??= [];
            incoming.Relationships ??= [];
            var existing = maps.FirstOrDefault(m => SameName(m.Name, incoming.Name));
            if (existing is not null)
            {
                existing.Nodes = incoming.Nodes;
                existing.Relationships = incoming.Relationships;
                result.Updated++;
            }
            else
            {
                incoming.Id = Guid.NewGuid().ToString("N")[..8];
                maps.Add(incoming);
                result.Added++;
            }
        }

        if (!dryRun)
        {
            _profiles.ReplaceProfileData(data);
            await _profiles.SaveAsync().ConfigureAwait(false);
        }
    }

    private async Task ImportCollectionsAsync(
        CollectionsStore store, bool replace, bool dryRun, TeamPackImportResult result,
        Dictionary<string, string> collectionIdRemap)
    {
        // Defensive second pass — export already stripped these, but a hand-edited pack must not
        // be able to write a CredentialSecret into collections.json either.
        StripCredentialSecrets(store);

        if (replace)
        {
            result.Skipped += _collections.Collections.Count;
            foreach (var collection in store.Collections)
            {
                var oldId = collection.Id;
                collection.Id = Guid.NewGuid().ToString("N");
                collectionIdRemap[oldId] = collection.Id;
                collection.UpdatedAt = DateTimeOffset.UtcNow;
            }
            if (!dryRun)
                await _collections.ReplaceStoreAsync(new CollectionsStore
                {
                    SchemaVersion = 1,
                    Collections = store.Collections,
                }).ConfigureAwait(false);
            result.Added += store.Collections.Count;
            return;
        }

        var existingByName = _collections.Collections
            .GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        foreach (var collection in store.Collections)
        {
            var oldId = collection.Id;
            if (existingByName.TryGetValue(collection.Name, out var clash))
            {
                // Even though the collection itself is skipped, its environments still rebind —
                // the same-named local collection is the natural scope.
                collectionIdRemap[oldId] = clash.Id;
                result.Skipped++;
                result.Conflicts.Add($"collections: '{collection.Name}' already exists — skipped (use replace to overwrite)");
                continue;
            }

            existingByName[collection.Name] = collection;
            result.Added++;

            if (!dryRun)
            {
                // AddImportedCollectionAsync mints the fresh id itself — record whatever it chose
                // so environments rebind to the id that actually persisted.
                await _collections.AddImportedCollectionAsync(collection).ConfigureAwait(false);
                collectionIdRemap[oldId] = collection.Id;
            }
            else
            {
                collectionIdRemap[oldId] = Guid.NewGuid().ToString("N");
            }
        }
    }

    private async Task ImportEnvironmentsAsync(
        EnvironmentsStore store, bool replace, bool dryRun, TeamPackImportResult result,
        Dictionary<string, string> collectionIdRemap)
    {
        // Rebind collection-scoped environments onto the ids the collections section just minted
        // (or an existing collection of the same name already had — either way the scope survives).
        foreach (var env in store.Environments)
        {
            if (env.CollectionId is { } oldId && collectionIdRemap.TryGetValue(oldId, out var newId))
            {
                env.CollectionId = newId;
            }
        }

        if (replace)
        {
            result.Skipped += _environments.Environments.Count;
            foreach (var env in store.Environments)
                env.Id = Guid.NewGuid().ToString("N");
            if (!dryRun)
            {
                await _environments.ReplaceStoreAsync(new EnvironmentsStore
                {
                    SchemaVersion = 1,
                    Environments = store.Environments,
                    UiState = _environments.UiState,
                }).ConfigureAwait(false);
            }
            result.Added += store.Environments.Count;
            return;
        }

        var existingNames = _environments.Environments.Select(e => e.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var env in store.Environments)
        {
            if (existingNames.Contains(env.Name))
            {
                result.Skipped++;
                result.Conflicts.Add($"environments: '{env.Name}' already exists — skipped (use replace to overwrite)");
                continue;
            }

            existingNames.Add(env.Name);
            result.Added++;
            if (!dryRun)
                await _environments.AddImportedEnvironmentAsync(env).ConfigureAwait(false);
        }
    }

    /// <summary>Merge keys by name+folder; <see cref="TeamPackSqlQuery.ConnectionHint"/> rebinds
    /// the query onto a local SQL connection when the same server+database exists here.</summary>
    private async Task ImportSqlQueriesAsync(List<TeamPackSqlQuery> packQueries, bool replace, bool dryRun, TeamPackImportResult result)
    {
        var connections = _profiles.Config.SqlConfig?.Connections ?? [];
        string? Rebind(TeamPackConnectionHint? hint) =>
            hint is null ? null
                : connections.FirstOrDefault(c =>
                        string.Equals(c.Server, hint.Server, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(c.Database, hint.Database, StringComparison.OrdinalIgnoreCase))
                    ?.Id;

        var existing = await _sqlQueries.GetQueriesAsync().ConfigureAwait(false);

        if (replace)
        {
            result.Skipped += existing.Count;
            if (!dryRun)
            {
                foreach (var stale in existing.ToList())
                    await _sqlQueries.DeleteQueryAsync(stale.Id).ConfigureAwait(false);
            }
            foreach (var q in packQueries)
            {
                result.Added++;
                if (!dryRun)
                {
                    await _sqlQueries.AddQueryAsync(new SavedSqlQuery
                    {
                        Name = q.Name,
                        Folder = q.Folder,
                        Sql = q.Sql,
                        ConnectionId = Rebind(q.ConnectionHint),
                    }).ConfigureAwait(false);
                }
            }
            return;
        }

        foreach (var q in packQueries)
        {
            var match = existing.FirstOrDefault(e =>
                SameName(e.Name, q.Name) && string.Equals(e.Folder ?? "", q.Folder ?? "", StringComparison.OrdinalIgnoreCase));
            var connectionId = Rebind(q.ConnectionHint);
            if (q.ConnectionHint is not null && connectionId is null)
            {
                result.Conflicts.Add(
                    $"sqlQueries: '{q.Name}' references {q.ConnectionHint.Server}/{q.ConnectionHint.Database} — no local connection matches; imported unbound");
            }

            if (match is not null)
            {
                result.Updated++;
                if (!dryRun)
                {
                    match.Sql = q.Sql;
                    match.Folder = q.Folder;
                    match.ConnectionId = connectionId;
                    match.UpdatedAt = DateTimeOffset.UtcNow;
                }
            }
            else
            {
                result.Added++;
                if (!dryRun)
                {
                    await _sqlQueries.AddQueryAsync(new SavedSqlQuery
                    {
                        Name = q.Name,
                        Folder = q.Folder,
                        Sql = q.Sql,
                        ConnectionId = connectionId,
                    }).ConfigureAwait(false);
                }
            }
        }

        if (!dryRun)
            await _sqlQueries.SaveAsync().ConfigureAwait(false);
    }

    /// <summary>Merge keys by rule name via <see cref="IAlertRuleRepository.UpsertAsync"/> —
    /// matched rules keep their local id so history/silences stay attached.</summary>
    private async Task ImportAlertRulesAsync(List<MonitoringAlertRule> packRules, bool replace, bool dryRun, TeamPackImportResult result)
    {
        var existing = (await _alertRules.GetAllAsync().ConfigureAwait(false)).ToList();

        if (replace)
        {
            result.Skipped += existing.Count;
            result.Added += packRules.Count;
            if (!dryRun)
            {
                await _alertRules.SaveAllAsync(packRules.Select(Clone).ToList()).ConfigureAwait(false);
            }
            return;
        }

        foreach (var rule in packRules)
        {
            var match = existing.FirstOrDefault(e => SameName(e.Name, rule.Name));
            if (match is not null)
            {
                rule.Id = match.Id;
                result.Updated++;
            }
            else
            {
                rule.Id = Guid.NewGuid().ToString("N");
                result.Added++;
            }

            if (!dryRun)
                await _alertRules.UpsertAsync(rule).ConfigureAwait(false);
        }
    }

    /// <summary>Merge keys by the (normalized) on-disk path — already-linked roots are skipped.</summary>
    private async Task ImportLinkedRootsAsync(List<TeamPackLinkedRoot> packRoots, bool replace, bool dryRun, TeamPackImportResult result)
    {
        if (replace)
        {
            result.Skipped += _linkedRoots.Roots.Count;
            if (!dryRun)
                await _linkedRoots.ReplaceRootsAsync([]).ConfigureAwait(false);
        }

        foreach (var root in packRoots)
        {
            string? fullPath = null;
            try
            {
                fullPath = Path.GetFullPath(root.Path);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                result.Skipped++;
                result.Conflicts.Add($"linkedRoots: '{root.Path}' is not a usable path on this machine");
                continue;
            }

            if (!Directory.Exists(fullPath))
            {
                result.Conflicts.Add($"linkedRoots: '{root.Path}' does not exist on this machine — link it manually after cloning the repo");
            }

            var existing = _linkedRoots.Roots.FirstOrDefault(r =>
                string.Equals(SafeFullPath(r.Path), fullPath, StringComparison.OrdinalIgnoreCase));
            if (existing is not null && !replace)
            {
                result.Skipped++;
                continue;
            }

            result.Added++;
            if (!dryRun)
            {
                var added = await _linkedRoots.AddRootAsync(fullPath, root.Name).ConfigureAwait(false);
                if (root.BrunoSyncFolderPath is not null || root.BrunoSyncEnabled != added.BrunoSyncEnabled)
                    await _linkedRoots.UpdateBrunoSyncSettingsAsync(added.Id, root.BrunoSyncFolderPath, root.BrunoSyncEnabled).ConfigureAwait(false);
            }
        }
    }

    /// <summary>Merge keys by chain name; each step's <see cref="ApiChainStep.CollectionId"/>
    /// rebinds through <paramref name="collectionIdRemap"/> so a chain over just-imported
    /// collections still resolves. References that can't rebind (linked-root ids, request ids
    /// from a collection the pack didn't carry) stay verbatim and surface as plan errors.</summary>
    private async Task ImportChainsAsync(
        List<ApiChain> packChains, bool replace, bool dryRun, TeamPackImportResult result,
        Dictionary<string, string> collectionIdRemap)
    {
        await _chains.LoadAsync().ConfigureAwait(false);
        foreach (var chain in packChains)
        {
            chain.Steps ??= [];
            foreach (var step in chain.Steps)
            {
                if (collectionIdRemap.TryGetValue(step.CollectionId, out var remapped))
                    step.CollectionId = remapped;
                // Step ids are run-correlation keys — mint one when the pack left it blank so a
                // malformed pack can't produce steps with no SSE identity.
                if (string.IsNullOrWhiteSpace(step.Id))
                    step.Id = Guid.NewGuid().ToString("N");
            }
        }

        if (replace)
        {
            result.Skipped += _chains.Chains.Count;
            if (!dryRun)
            {
                foreach (var chain in packChains)
                {
                    chain.Id = Guid.NewGuid().ToString("N");
                    chain.UpdatedAt = DateTimeOffset.UtcNow;
                }
                await _chains.ReplaceStoreAsync(new ChainsStore
                {
                    SchemaVersion = 1,
                    Chains = packChains,
                }).ConfigureAwait(false);
            }
            result.Added += packChains.Count;
            return;
        }

        var existingByName = _chains.Chains
            .GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        foreach (var chain in packChains)
        {
            if (existingByName.TryGetValue(chain.Name, out _))
            {
                result.Skipped++;
                result.Conflicts.Add($"apiChains: '{chain.Name}' already exists — skipped (use replace to overwrite)");
                continue;
            }

            existingByName[chain.Name] = chain;
            result.Added++;
            // AddChainAsync always mints a fresh chain id — a teammate may already own the
            // pack's id for a different chain, and the id must never silently repoint.
            if (!dryRun)
                await _chains.AddChainAsync(chain).ConfigureAwait(false);
        }
    }

    private static string? SafeFullPath(string path)
    {
        try { return Path.GetFullPath(path); }
        catch { return path; }
    }

    // ── Secret hygiene ───────────────────────────────────────────────────────

    /// <summary>
    /// The name-only manifest of secrets a teammate must re-link: auth credential keys on
    /// collections/folders/requests and secret-sourced environment variables. Values never appear.
    /// </summary>
    private static List<TeamPackCredentialRef> CollectCredentialRefs(TeamPack pack)
    {
        var refs = new List<TeamPackCredentialRef>();

        void FromAuth(AuthConfig? auth, string context)
        {
            if (auth is null) return;
            if (!string.IsNullOrWhiteSpace(auth.CredentialKey))
                refs.Add(new TeamPackCredentialRef { Key = auth.CredentialKey!, Kind = "credentialStore", Hint = context });
            if (!string.IsNullOrWhiteSpace(auth.OAuth2TokenCredentialKey))
                refs.Add(new TeamPackCredentialRef { Key = auth.OAuth2TokenCredentialKey!, Kind = "oauthToken", Hint = context });
        }

        void WalkNode(ApiCollectionNode node, string context)
        {
            FromAuth(node.DefaultAuth, context);
            FromAuth(node.Request?.Auth, $"{context}/{node.Name}");
            foreach (var child in node.Children) WalkNode(child, context);
        }

        foreach (var collection in pack.CollectionsData?.Collections ?? [])
        {
            FromAuth(collection.DefaultAuth, collection.Name);
            foreach (var node in collection.Nodes) WalkNode(node, collection.Name);
        }

        foreach (var env in pack.EnvironmentsData?.Environments ?? [])
        {
            foreach (var variable in env.Variables)
            {
                switch (variable.SecretSource)
                {
                    case EnvironmentVariableSecretSource.WindowsCredentialStore
                        when !string.IsNullOrWhiteSpace(variable.CredentialKey):
                        refs.Add(new TeamPackCredentialRef
                        {
                            Key = variable.CredentialKey!,
                            Kind = "credentialStore",
                            Hint = $"{env.Name}:{variable.Key}",
                        });
                        break;
                    case EnvironmentVariableSecretSource.AzureKeyVault
                        when !string.IsNullOrWhiteSpace(variable.CredentialKey):
                        refs.Add(new TeamPackCredentialRef
                        {
                            Key = variable.CredentialKey!,
                            Kind = "azureKeyVault",
                            Hint = $"{env.Name}:{variable.Key} (vault: {variable.KeyVaultName ?? "default"})",
                        });
                        break;
                }
            }
        }

        // De-dupe: the same credential key referenced by two requests is one secret to re-link.
        return refs
            .GroupBy(r => (r.Key, r.Kind), StringTupleComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
    }

    /// <summary>Variable names that look secret-y — only these get a "plain value might be a
    /// secret" warning, so ordinary vars like <c>baseUrl</c> don't cry wolf.</summary>
    private static readonly string[] SuspiciousVariableNameParts = ["key", "secret", "token", "password", "pwd"];

    private static List<string> CollectWarnings(TeamPack pack)
    {
        var warnings = new List<string>();
        foreach (var env in pack.EnvironmentsData?.Environments ?? [])
        {
            foreach (var variable in env.Variables)
            {
                if (variable.SecretSource == EnvironmentVariableSecretSource.Plain
                    && !string.IsNullOrEmpty(variable.Value)
                    && SuspiciousVariableNameParts.Any(part =>
                        variable.Key.Contains(part, StringComparison.OrdinalIgnoreCase)))
                {
                    warnings.Add(
                        $"environment '{env.Name}' variable '{variable.Key}' carries a plain value and its name suggests a secret — verify it before sharing");
                }
            }
        }
        return warnings;
    }

    /// <summary>Recursive <see cref="AuthConfig.CredentialSecret"/> strip — same invariant
    /// <c>ConfigEndpoints.StripCredentialSecrets</c> enforces on the save path.</summary>
    private static void StripCredentialSecrets(CollectionsStore store)
    {
        foreach (var collection in store.Collections)
        {
            Strip(collection.DefaultAuth);
            foreach (var node in collection.Nodes)
                StripNode(node);
        }

        static void StripNode(ApiCollectionNode node)
        {
            Strip(node.DefaultAuth);
            Strip(node.Request?.Auth);
            foreach (var child in node.Children)
                StripNode(child);
        }

        static void Strip(AuthConfig? auth)
        {
            if (auth is not null)
                auth.CredentialSecret = null;
        }
    }

    private static bool SameName(string? a, string? b) =>
        string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);

    private static T Clone<T>(T value)
    {
        var json = JsonSerializer.Serialize(value, Options);
        return JsonSerializer.Deserialize<T>(json, Options)!;
    }

    /// <summary>OrdinalIgnoreCase grouping for (Key, Kind) tuples.</summary>
    private sealed class StringTupleComparer : IEqualityComparer<(string Key, string Kind)>
    {
        public static readonly StringTupleComparer OrdinalIgnoreCase = new();
        public bool Equals((string Key, string Kind) x, (string Key, string Kind) y) =>
            string.Equals(x.Key, y.Key, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.Kind, y.Kind, StringComparison.OrdinalIgnoreCase);
        public int GetHashCode((string Key, string Kind) obj) =>
            HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Key),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Kind));
    }
}

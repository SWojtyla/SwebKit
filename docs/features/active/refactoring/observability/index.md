# Refactoring Feature: Observability

## 🎯 Objectif Global

**Améliorer la structure et les performances** de la feature **Observability** en décomposant `ObservabilityLogs.razor` (1,256 lignes) et `ObservabilityPage.razor` (724 lignes).

## 📊 État Actuel

### Fichiers Critiques

| Fichier | Lignes | Taille | Complexité | Priorité |
|--------|--------|--------|------------|----------|
| `ObservabilityLogs.razor` | **1,256** | 54.2 KB | ⭐⭐⭐⭐⭐ | 🟡 ÉLEVÉE |
| `ObservabilityPage.razor` | **724** | 27.6 KB | ⭐⭐⭐⭐ | 🟡 ÉLEVÉE |
| `ObservabilityFailures.razor` | **543** | 26.4 KB | ⭐⭐⭐ | 🟡 ÉLEVÉE |
| `ObservabilityPerformance.razor` | **563** | 26.6 KB | ⭐⭐⭐ | 🟡 ÉLEVÉE |
| `AzureAppInsightsProvider.cs` | **539** | 22.1 KB | ⭐⭐⭐⭐ | 🟡 ÉLEVÉE |

### Problèmes Identifiés

1. **ObservabilityLogs.razor** (1,256 lignes)
   - ❌ **Composant monolithe** : Logs + query + KQL + monitoring
   - ❌ **Logique complexe** : Gestion des query KQL + affichage logs
   - ❌ **State management** : Multiples states (time range, filters, etc.)
   - ❌ **Performance critique** : Gestion de grands volumes de logs

2. **ObservabilityPage.razor** (724 lignes)
   - ❌ **Coordinateur trop gros** : Gère plusieurs tabs
   - ❌ **Duplication de code** : Logique similaire dans chaque tab

3. **ObservabilityFailures.razor** et **ObservabilityPerformance.razor**
   - ❌ **Trop de logique d'affichage** dans chaque composant
   - ❌ **Duplication** : Patterns similaires entre les deux

4. **AzureAppInsightsProvider.cs** (539 lignes)
   - ❌ **Provider monolithe** : Toutes les opérations App Insights
   - ❌ **Complexe à tester**

## ✅ Objectifs Spécifiques

### 1. Décomposer ObservabilityLogs.razor

**Cible** : 1 log viewer + 5-6 sous-composants + 3-4 services.

```
Components/Observability/
├── ObservabilityLogs.razor          # Log Viewer principal (~150-200 lignes)
│   - Coordination générale
│   - Gestion de l'état global
│
├── Logs/
│   ├── KqlQueryEditor.razor         # Éditeur KQL
│   ├── LogResultsTable.razor        # Tableau des résultats
│   ├── LogResultRow.razor           # Ligne de résultat
│   ├── LogTimeRangeSelector.razor   # Sélecteur de plage temporelle
│   ├── LogFilterPanel.razor         # Filtres avancés
│   └── LogStreamingDisplay.razor     # Streaming des logs en temps réel
│
└── Services/
    ├── LogQueryService.cs          # Exécution des queries log
    ├── LogFilterService.cs          # Filtrage des logs
    ├── LogKqlService.cs             # Parsing et validation KQL
    └── LogStreamingService.cs        # Streaming des logs
```

### 2. Décomposer ObservabilityPage.razor

**Cible** : 1 page + 4-on 5 tabs.

```
Components/Pages/
└── ObservabilityPage.razor          # Page coordinateur (~100 lignes)

Components/Observability/
├── Overview/
│   └── ObservabilityOverview.razor # Tab Overview
├── Logs/
│   └── ObservabilityLogs.razor     # Tab Logs (déjà existant)
├── Failures/
│   └── ObservabilityFailures.razor # Tab Failures
├── Performance/
│   └── ObservabilityPerformance.razor # Tab Performance
└── Availability/
    └── ObservabilityAvailability.razor # Tab Availability
```

### 3. Décomposer ObservabilityFailures.razor & Performance.razor

**Cible** : 1 tab template + spécialisations.

```
Components/Observability/
├── ObservabilityTab.razor           # Template commun pour les tabs
├── Failures/
│   ├── ObservabilityFailures.razor # Composant principal
│   ├── FailureList.razor            # Liste des failures
│   └── FailureDetail.razor          # Détail d'une failure
│
└── Performance/
    ├── ObservabilityPerformance.razor # Composant principal
    ├── PerformanceChart.razor        # Graphiques de performance
    └── PerformanceDetail.razor       # Détail des métriques
```

### 4. Décomposer AzureAppInsightsProvider.cs

**Cible** : 4-5 services spécialisés.

```
Services/Observability/
├── IAppInsightsConnectionService.cs # Gestion de la connexion
├── IAppInsightsQueryService.cs      # Exécution des queries
├── IAppInsightsResourceService.cs    # Gestion des ressources
├── IAppInsightsMetadataService.cs    # Récupération des métadonnées
├── AppInsightsResourceDiscovery.cs  # Découverte des ressources App Insights
└── AppInsightsServiceAggregator.cs   # Agrégateur pour compatibilité
```

## 📋 Tâches Détaillées

### Phase 1: Préparation (1/2 jour)
- [ ] Analyser ObservabilityLogs.razor
- [ ] Identifier les patterns de query KQL
- [ ] Documenter l,value de données
- [ ] Analyser les performances actuelles

### Phase 2: Décomposer les Services (2-3 jours)
- [ ] Créer les interfaces pour App Insights
- [ ] Implémenter les services spécialisés
- [ ] Créer l'agrégateur
- [ ] Mettre à jour MauiProgram.cs
- [ ] Tester les services

### Phase 3: Décomposer ObservabilityLogs (2-3 jours)
- [ ] Créer KqlQueryEditor.razor
- [ ] Créer LogResultsTable.razor
- [ ] Créer LogTimeRangeSelector.razor
- [ ] Extraire la logique depuis ObservabilityLogs
- [ ] Réduire ObservabilityLogs.razor à 150-200 lignes
- [ ] Tester l'intégration

### Phase 4: Décomposer ObservabilityPage (1-2 jours)
- [ ] Créer le tab template commun
- [ ] Spécialiser chaque tab
- [ ] Réduire ObservabilityPage.razor à 100 lignes
- [ ] Tester tous les tabs

### Phase 5: Décomposer Failures & Performance (1-2 jours)
- [ ] Créer le composant FailureList
- [ ] Créer le composant PerformanceChart
- [ ] Réduire les fichiers existants
- [ ] Tester chaque spécialisation

### Phase 6: Tests et Performance (2 jours)
- [ ] Tests unitaires pour les services
- [ ] Tests de composants
- [ ] Optimiser le streaming des logs
- [ ] Tests de performance
- [ ] Tests de régression

## 🎯 Améliorations de Performances

### 1. Virtualisation des Résultats
```razor
<Virtualize Items="@_logEntries" Context="entry">
    <LogResultRow Entry="entry" />
</Virtualize>
```

### 2. Lazy Loading des Logs
```csharp
private async Task LoadMoreLogsAsync()
{
    if (HasMore && !IsLoading)
    {
        IsLoading = true;
        var moreEntries = await _logQueryService.QueryNextAsync(CurrentQuery, Skip, Take);
        _logEntries.AddRange(moreEntries);
        Skip += Take;
        HasMore = moreEntries.Count == Take;
        IsLoading = false;
    }
}
```

### 3. Cache des Requêtes
- Cache des résultats de query KQL
- Cache des métadonnées des ressources
- Invalidations intelligentes du cache

### 4. Streaming Optimisé
- Buffering intelligent
- Pause automatique quand non visible
- Reprise silencieuse au Scroll

### 5. Exécution paresseuse des queries
- Exécuter uniquement quand nécessaire
- Debouncing des requêtes
- Limitation des requêtes simultanées

## 🧪 Stratégie de Tests

- Tests unitaires pour tous les services
- Tests pour le parsing KQL
- Tests pour le streaming des logs
- Tests de composants pour chaque tab
- Tests de performance pour les grands volumes

### Couverture Cible
- Services : **> 90%**
- Composants : **> 85%**

## 📁 Fichiers à Créer/Modifier

### Nouveaux Fichiers
```
src/SwebKit.Observability/Services/
├── IAppInsightsConnectionService.cs
├── AppInsightsConnectionService.cs
├── IAppInsightsQueryService.cs
├── AppInsightsQueryService.cs
├── IAppInsightsResourceService.cs
├── AppInsightsResourceService.cs
├── IAppInsightsMetadataService.cs
├── AppInsightsMetadataService.cs
├── AppInsightsServiceAggregator.cs
└── Logs/
    ├── LogQueryService.cs
    ├── LogFilterService.cs
    ├── LogKqlService.cs
    └── LogStreamingService.cs

src/SwebKit.App/Components/Observability/
├── ObservabilityPage.razor
├── Logs/
│   ├── KqlQueryEditor.razor
│   ├── LogResultsTable.razor
│   ├── LogResultRow.razor
│   ├── LogTimeRangeSelector.razor
│   ├── LogFilterPanel.razor
│   └── LogStreamingDisplay.razor
├── ObservabilityTab.razor
├── Overview/
│   └── ObservabilityOverview.razor
├── Failures/
│   ├── FailureList.razor
│   └── FailureDetail.razor
└── Performance/
    ├── PerformanceChart.razor
    └── PerformanceDetail.razor
```

### Fichiers à Modifier
- `src/SwebKit.App/MauiProgram.cs`
- `src/SwebKit.Observability/AzureAppInsightsProvider.cs`
- Tous les fichiers dépendants

## ⚠️ Risques et Atténuation

| Risque | Impact | Atténuation |
|--------|--------|-------------|
| Problèmes avec le streaming de logs | Élevé | Tests extensifs du streaming |
| Problèmes avec les queries KQL | Élevé | Validation et parsing robustes |
| Performance dégradée | Moyen | Benchmarks comparatifs |
| Incompatibilité App Insights | Moyen | Tests avec des ressources réelles |

## 📊 Métriques de Succès

| Métrique | Avant | Après | Statut |
|----------|-------|-------|--------|
| Lignes ObservabilityLogs | 1,256 | < 200 | À faire |
| Lignes ObservabilityPage | 724 | < 100 | À faire |
| Nombre de services | 1-2 | 8-10 | À faire |
| Couverture de tests | ~35% | > 85% | À faire |
| Performance des queries | TBR | TBR | À faire |

---

## 📚 Documentation Connexe
- [Observability Functionalities](../../../architecture/functionalities/observability.md)
- [Architecture globale](../../../architecture/architecture.md)

---

*Créé le: {{date}}*
*Statut: En planification*
*Priorité: 🟡 ÉLEVÉE*

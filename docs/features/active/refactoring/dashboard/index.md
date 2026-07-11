# Refactoring Feature: Dashboard

## 🎯 Objectif Global

**Réduire drastiquement la complexité** de `DashboardPage.razor` (2,960 lignes) en le décomposant en composants modulaire et réutilisables, tout en améliorant les performances et la maintenabilité.

## 📊 État Actuel

### Fichier Critique
- **DashboardPage.razor** : **2,960 lignes**, 140 KB

### Problèmes Identifiés

1. **DashboardPage.razor** (2,960 lignes)
   - ❌ **Composant monolithe** : Contient toute la logique de dashboard
   - ❌ **Trop de responsabilités** : Tiles + layout + navigation + state management
   - ❌ **Code dupliqué** : Chaque tile a sa propre logique similaire
   - ❌ **Performance variable** : Certains tiles chargent lentement
   - ❌ **Difficile à customiser** : Ajouter un nouveau type de tile est complexe
   - ❌ **State management complexe** : État global difficile à comprendre

### Analyse des Types de Tiles

Basé sur le code existant, le dashboard contient environ **12-15 types de tiles** différents :
- Health tiles (par service)
- Readiness tiles
- Recent resources tiles
- Favorites tiles
- Quick actions tiles
- Metrics tiles (CPU, Memory, etc.)
- Alert tiles
- Status tiles
- Custom tiles
- etc.

## ✅ Objectifs Spécifiques

### 1. Décomposer en Composants Modulaires

**Cible** : 1 Dashboard + 15-20 composants de tile + 5-7 services.

**Architecture proposée** :
```
Components/Pages/
├── DashboardPage.razor            # Coordinateur principal (~100-150 lignes)
│   - Gère le layout global
│   - Coordonne les tiles
│   - Gère l'état global partagé
│
Components/Dashboard/
├── Tiles/
│   ├── DashboardTile.razor         # Base générique pour tous les tiles
│   ├── HealthTile.razor            # Tile de santé
│   ├── ReadinessTile.razor         # Tile de readiness
│   ├── ResourceTile.razor          # Tile de ressource (base)
│   │   ├── AksTile.razor           # Spécialisé pour AKS
│   │   ├── ServiceBusTile.razor   # Spécialisé pour Service Bus
│   │   ├── RedisTile.razor         # Spécialisé pour Redis
│   │   └── StorageTile.razor       # Spécialisé pour Storage
│   ├── RecentTile.razor            # Tile "Recent resources"
│   ├── FavoritesTile.razor         # Tile "Favorites"
│   ├── QuickActionTile.razor       # Tile d'actions rapides
│   ├── MetricsTile.razor           # Tile de métriques
│   ├── AlertTile.razor             # Tile d'alertes
│   └── CustomTile.razor            # Tile customisable
│
├── Layout/
│   ├── DashboardGrid.razor        # Grille des tiles (responsive)
│   ├── TileDragDrop.razor         # Drag & Drop pour réorganiser
│   └── DashboardSettings.razor      # Paramètres du dashboard
│
└── Services/
    ├── DashboardService.cs         # Gestion globale du dashboard
    ├── TileService.cs              # Services pour les tiles
    ├── LayoutService.cs            # Gestion du layout
    └── DashboardStateService.cs    # État global
```

### 2. Créer une Hiérarchie de Tiles

```
┌─────────────────────────────┐
│        DashboardTile          │
│        (Base abstraite)        │
├─────────────────────────────┤
│ + Title                       │
│ + Icon                        │
│ + Color/Status                │
│ + Size                        │
│ + OnClick                     │
│ + OnRefresh                   │
│ + RenderContent()             │  ← Méthode abstraite
└─────────────────────────────┘
              │
    ┌─────────┬─────────────┬─────────────┐
    ▼         ▼               ▼             ▼
┌────────┐ ┌──────────┐ ┌─────────┐ ┌─────────┐
│ Health  │ │ Readiness│ │Resource │ │ Metrics │
│ Tile    │ │ Tile     │ │ Tile    │ │ Tile    │
└────────┘ └──────────┘ └─────────┘ └─────────┘
```

### 3. Externaliser la Logique Métier

**Services proposés** :

| Service | Responsabilité | Utilisation |
|---------|---------------|-------------|
| `ITileDataService.cs` | Récupération des données pour les tiles | Tous les tiles qui ont besoin de données |
| `IDashboardLayoutService.cs` | Gestion de la disposition des tiles | Layout + Drag & Drop |
| `ITileRefreshService.cs` | Gestion du rafraîchissement automatique | DashboardPage + Tiles |
| `IDashboardCustomizationService.cs` | Personnalisation du dashboard (ajout/suppression de tiles) | Dashboard Settings |
| `ITileNotificationService.cs` | Gestion des notifications depuis les tiles | AlertTile + StatusTile |

### 4. Améliorer les Performances

**Problèmes actuels** :
- Certains tiles mettent trop de temps à charger
- Ré-rendu inutiles de tiles non visibles
- Pas de lazy loading efficace
- Cache peu optimisé

**Solutions proposées** :

#### Lazy Loading des Tiles
```csharp
// Chaque tile charge ses données uniquement quand il devient visible
public class HealthTile : DashboardTile
{
    private bool _isVisible;
    private bool _isLoaded;
    
    public override async Task OnVisibleAsync()
    {
        if (!_isLoaded)
        {
            await LoadDataAsync();
            _isLoaded = true;
        }
    }
    
    private async Task LoadDataAsync()
    {
        Status = TileStatus.Loading;
        StateHasChanged();
        
        try
        {
            Data = await _healthService.GetHealthAsync(ResourceType, ResourceName);
            Status = Data.IsHealthy ? TileStatus.Healthy : TileStatus.Warning;
        }
        catch (Exception ex)
        {
            Status = TileStatus.Error;
            ErrorMessage = ex.Message;
        }
        
        StateHasChanged();
    }
}
```

#### Virtualisation des Tiles (Off-screen)
```razor
<Virtualize Items="@_tiles" Context="tile">
    <TileComponent Tile="tile" 
                  OnRefresh="HandleRefresh"
                  OnClick="HandleClick" />
</Virtualize>
```

#### Cache des Données par Tile
```csharp
public class TileCacheService
{
    private readonly MemoryCache _cache = new(MemoryCacheOptions);
    
    public async Task<T> GetOrCreateAsync<T>(string tileId, 
                                            Func<Task<T>> createFunc,
                                            TimeSpan expiration)
    {
        var cacheKey = $"tile-{tileId}-{typeof(T).Name}";
        return await _cache.GetOrCreateAsync(cacheKey, 
            async e => {
                e.AbsoluteExpirationRelative = expiration;
                return await createFunc();
            });
    }
    
    public void Invalidate(string tileId)
    {
        var prefix = $"tile-{tileId}-";
        _cache.RemoveWhere(k => k.StartsWith(prefix));
    }
    
    public void InvalidateAll() => _cache.Dispose();
}
```

#### Rafraîchissement intelligent
```csharp
public class TileRefreshService
{
    private readonly Timer _refreshTimer;
    private readonly Dictionary<string, DateTime> _lastRefresh = new();
    
    public TileRefreshService()
    {
        _refreshTimer = new Timer(RunRefreshCycle, null, 
                                TimeSpan.Zero, TimeSpan.FromSeconds(30));
    }
    
    public void RegisterTile(string tileId, TimeSpan interval)
    {
        _refreshIntervals[tileId] = interval;
    }
    
    private async void RunRefreshCycle(object state)
    {
        var now = DateTime.Now;
        
        foreach (var tileId in _refreshIntervals.Keys)
        {
            if (now - _lastRefresh[tileId] > _refreshIntervals[tileId])
            {
                await RefreshTileAsync(tileId);
                _lastRefresh[tileId] = now;
            }
        }
    }
}
```

## 📋 Tâches Détaillées

### Phase 1: Préparation (1 jour)
- [ ] Analyser la structure actuelle de DashboardPage.razor
- [ ] Identifier tous les types de tiles existants
- [ ] Documenter les dépendances entre les tiles
- [ ] Créer l'inventaire des fonctionnalités actuelles
- [ ] Analyser les performances actuelles

### Phase 2: Créer la Base des Tiles (2-3 jours)

#### Base Class (0/3)
- [ ] Créer `DashboardTile.razor` (base abstraite)
- [ ] Créer `DashboardTile.razor.cs` (code-behind)
- [ ] Créer `TileStatus.cs` (enum des statuts)

#### Infrastructure (0/5)
- [ ] Créer `IDashboardService.cs` + implémentation
- [ ] Créer `ITileDataService.cs` + implémentation
- [ ] Créer `ITileRefreshService.cs` + implémentation
- [ ] Créer `TileCacheService.cs`
- [ ] Mettre à jour MauiProgram.cs pour les nouveaux services

### Phase 3: Décomposer les Tiles Existants (3-4 jours)

#### Tiles de Santé (0/4)
- [ ] Créer `HealthTile.razor`
- [ ] Extraire la logique de santé depuis DashboardPage
- [ ] Créer `HealthTileViewModel.cs`
- [ ] Tester le tile

#### Tiles de Ressources (0/8)
- [ ] Créer `ResourceTile.razor` (base)
- [ ] Créer `AksTile.razor`
- [ ] Créer `ServiceBusTile.razor`
- [ ] Créer `RedisTile.razor`
- [ ] Créer `StorageTile.razor`
- [ ] Extraire la logique correspondante
- [ ] Créer les ViewModels pour chaque type
- [ ] Tester chaque tile

#### Autres Tiles (0/8)
- [ ] Créer `ReadinessTile.razor`
- [ ] Créer `RecentTile.razor`
- [ ] Créer `FavoritesTile.razor`
- [ ] Créer `QuickActionTile.razor`
- [ ] Créer `MetricsTile.razor`
- [ ] Créer `AlertTile.razor`
- [ ] Créer `CustomTile.razor`
- [ ] Tester chaque tile

### Phase 4: Décomposer le Layout (1-2 jours)

#### Grille et Layout (0/5)
- [ ] Créer `DashboardGrid.razor` (grille responsive)
- [ ] Extraire la logique de layout depuis DashboardPage
- [ ] Implémenter le responsive design
- [ ] Gérer le mode compact/puisant
- [ ] Tester le layout

#### Drag & Drop (0/5)
- [ ] Créer `TileDragDrop.razor`
- [ ] Implémenter Drag & Drop avecJS Interop
- [ ] Sauvegarder la position des tiles
- [ ] Charger la position sauvegardée
- [ ] Tester le Drag & Drop

### Phase 5: Créer DashboardPage.razor minimal (1/2 jour)
- [ ] Créer DashboardPage.razor comme simple coordinateur
- [ ] Intégrer DashboardGrid
- [ ] Intégrer tous les tiles
- [ ] Gérer l'état global
- [ ] Tester l'intégration complète

### Phase 6: Dashboard Settings (1 jour)
- [ ] Créer `DashboardSettings.razor`
- [ ] Implémenter ajout/suppression de tiles
- [ ] Implémenter personnalisation des tiles
- [ ] Implémenter sauvegarde/chargement de la configuration
- [ ] Tester les settings

### Phase 7: Performances et Tests (2-3 jours)
- [ ] Optimiser les performances de chargement
- [ ] Implémenter le lazy loading
- [ ] Ajouter le cache
- [ ] Configurer le rafraîchissement automatique
- [ ] Tests unitaires pour les services
- [ ] Tests de composants pour les tiles
- [ ] Tests d'intégration
- [ ] Tests de régression

## 🧪 Stratégie de Tests

### Tests Unitaires (Services)
- Tests pour TileDataService
- Tests pour DashboardService
- Tests pour TileRefreshService
- Tests pour TileCacheService

### Tests de Composants (bUnit)
- Tests pour DashboardPage
- Tests pour DashboardGrid
- Tests pour chaque type de tile
- Tests pour Drag & Drop
- Tests pour Dashboard Settings

### Tests de Performance
- Temps de chargement des tiles
- Memory usage des tiles
- Cache hit rate
- Rafraîchissement automatique

### Couverture Cible
- Services : **> 90%**
- Composants : **> 85%**
- Global : **> 85%**

## 📁 Fichiers à Créer/Modifier

### Nouveaux Fichiers
```
src/SwebKit.App/Components/Pages/
└── DashboardPage.razor              # Réécrit (100-150 lignes)

src/SwebKit.App/Components/Dashboard/
├── Tiles/
│   ├── DashboardTile.razor           # Base abstraite
│   ├── DashboardTile.razor.cs        # Code-behind
│   ├── HealthTile.razor             # Tile de santé
│   ├── ReadinessTile.razor           # Tile de readiness
│   ├── ResourceTile.razor            # Base pour les resources
│   │   ├── AksTile.razor
│   │   ├── ServiceBusTile.razor
│   │   ├── RedisTile.razor
│   │   └── StorageTile.razor
│   ├── RecentTile.razor             # Recent resources
│   ├── FavoritesTile.razor           # Favorites
│   ├── QuickActionTile.razor         # Quick actions
│   ├── MetricsTile.razor             # Metrics
│   ├── AlertTile.razor               # Alerts
│   └── CustomTile.razor              # Custom
│
├── Layout/
│   ├── DashboardGrid.razor          # Grille responsive
│   ├── TileDragDrop.razor           # Drag & Drop
│   └── DashboardSettings.razor      # Paramètres
│
└── Services/
    ├── IDashboardService.cs
    ├── DashboardService.cs
    ├── ITileDataService.cs
    ├── TileDataService.cs
    ├── ITileRefreshService.cs
    ├── TileRefreshService.cs
    └── TileCacheService.cs

src/SwebKit.App/Styles/
└── dashboard.css                    # Styles spécifiques
```

### Fichiers à Modifier
- `src/SwebKit.App/Components/Layout/MainLayout.razor` - Intégration du nouveau dashboard
- `src/SwebKit.App/MauiProgram.cs` - Enregistrement des nouveaux services
- Fichiers de configuration existants pour la migration

### Fichiers à Supprimer
- DashboardPage.razor actuel (2,960 lignes)

## ⚠️ Risques et Atténuation

| Risque | Impact | Atténuation |
|--------|--------|-------------|
| Rupture de la personnalisation existante | Élevé | Migration progressive de la configuration |
| Problèmes de compatibilité UI | Moyen | Tests extensifs de l'UI |
| Régression des performances | moyen | Benchmarks comparatifs |
| Bugs dans le Drag & Drop | moyen | Tests spécifiques du Drag & Drop |

## 📊 Métriques de Succès

| Métrique | Avant | Après | Statut |
|----------|-------|-------|--------|
| Lignes DashboardPage | 2,960 | < 150 | À faire |
| Nombre de tiles | 0 | 15-20 | À faire |
| Nombre de services | 0 | 5-7 | À faire |
| Couverture de tests | ~40% | > 85% | À faire |
| Temps de chargement | TBR | TBR | À faire |
| personnalisation configurable | Non | Oui | À faire |

## 🎯 Performances Attendues

| Opération | Avant | Après | Amélioration |
|-----------|-------|-------|-------------|
| Chargement dashboard | > 2s | < 500ms | 75% |
| Rafraîchissement tile | 100-500ms | < 100ms | 80% |
| Memory usage | TBR | TBR | TBR |
| Tiles simultanés | Limité | Illimité (virtualisé) | ✅ |

---

## 📚 Documentation Connexe
- [Dashboard Functionalities](../../../architecture/functionalities/dashboard.md)
- [Architecture globale](../../../architecture/architecture.md)
- [Design des composants](../../../architecture/design.md)

---

*Créé le: {{date}}*
*Statut: En planification*
*Priorité: 🔴 CRITIQUE*

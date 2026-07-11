# Refactoring Feature: Layout

## 🎯 Objectif Global

**Améliorer la maintenabilité et les performances** des composants de **Layout** en décomposant `MainLayout.razor` (685 lignes) et `TopBar.razor` (485 lignes), ainsi que les autres composants de navigation.

## 📊 État Actuel

### Fichiers Critiques

| Fichier | Lignes | Taille | Complexité | Priorité |
|--------|--------|--------|------------|----------|
| `MainLayout.razor` | **685** | 25.8 KB | ⭐⭐⭐⭐ | 🟡 ÉLEVÉE |
| `TopBar.razor` | **485** | 21.1 KB | ⭐⭐⭐ | 🟡 ÉLEVÉE |
| `LeftNav.razor` | **~400** | ~18 KB | ⭐⭐⭐ | 🟡 ÉLEVÉE |
| `SidePanel.razor` | **~200** | ~10 KB | ⭐⭐ | 🟢 MOYENNE |
| `PageToolbar.razor` | **~150** | ~8 KB | ⭐⭐ | 🟢 MOYENNE |
| `PageHeader.razor` | **~100** | ~6 KB | ⭐ | 🟢 MOYENNE |

### Problèmes Identifiés

1. **MainLayout.razor** (685 lignes) + API
   - ❌ **Responsabilités multiples** : Shell + Navigation + Tabs + Notifications
   - ❌ **État complexe** : Gestion de multiples states_SHARED
   - ❌ **Code de bootstrap gros** : Initialisation de l'application
   - ❌ **Difficile à customiser** : css

2. **TopBar.razor** (485 lignes)
   - ❌ **Trop de fonctionnalités** : Recherche + Workspace + Actions globales
   - ❌ **Logique complexe** : Gestion des raccourcis clavier + menus
   - ❌ **Commandes dispersées** : Logique des commandes partout

3. **LeftNav.razor** (~400 lignes)
   - ❌ **Navigation complexe** : Multiples niveaux de menu
   - ❌ **State management** : Sélection active, expansion, etc.
   - ❌ **Intégration avec le routing**

## ✅ Objectifs Spécifiques

### 1. Décomposer MainLayout.razor

**Cible** : 1 coordinateur + 8-10 composants spécialisés.

**Architecture proposée** :
```
Components/Layout/
├── MainLayout.razor                 # Coordinateur principal (~100-150 lignes)
│   - Gestion du state global
│   - Coordination Shell/Navigation
│   - Gestion du lifecycle
│
├── Shell/
│   ├── AppShell.razor               # Shell de l'application
│   ├── TabManager.razor             # Gestion des tabs ouverts
│   └── NotificationHost.razor       # Hôte des notifications
│
├── Navigation/
│   ├── LeftNav.razor                # Navigation latérale (déjà existant)
│   └── NavItem.razor                # Item de navigation (base)
│
├── Toolbars/
│   ├── TopBar.razor                 # Barre du haut (à décomposer)
│   └── PageToolbar.razor             # Barre de la page
│
└── Shared/
    ├── SidePanel.razor               # Panel latéral (existante)
    ├── PageHeader.razor              # En-tête de page
    └── PageTitle.razor               # Titre de page
```

### 2. Décomposer TopBar.razor

**Cible** : 1 toolbar + 5-6 sections spécialisées.

```
Components/Layout/Toolbars/
├── TopBar.razor                      #Toolbar Principal (~50-80 lignes)
│   - Coordination des sections
│
├── Sections/
│   ├── WorkspaceSelectSection.razor  # Sélecteur d'espace de travail
│   ├── SearchSection.razor           # Barre de recherche
│   ├── CommandsSection.razor         # Commandes globales
│   ├── TabsSection.razor             # Onglets ouverts (coordination)
│   └── UserSection.razor             # Menu utilisateur
│
└── Components/
    ├── WorkspaceSelect.razor          # Composant de sélection workspace
    ├── GlobalSearch.razor             # Composant de recherche globale
    ├── CommandPalette.razor          # Palette de commandes
    └── UserMenu.razor                 # Menu utilisateur
```

### 3. Décomposer LeftNav.razor

**Cible** : Navigation modulaire + items spécialisés.

```
Components/Layout/Navigation/
├── LeftNav.razor                     # Navigation principale (~100-150 lignes)
│   - Gestion de l'état de navigation
│   - Coordination des items
│
├── NavGroups/
│   ├── NavGroup.razor               # Groupe de navigation
│   ├── ExpandableNavGroup.razor     # Groupe expandable
│   └── NavGroupHeader.razor          # En-tête de groupe
│
├── NavItems/
│   ├── NavItem.razor                # Item de base
│   ├── NavItemWithIcon.razor        # Item avec icône
│   ├── NavItemWithBadge.razor        # Item avec badge
│   └── NavItemCustom.razor           # Item customisable
│
└── NavSections/
    ├── ServiceBusSection.razor      # Section Service Bus
    ├── AksSection.razor              # Section AKS
    ├── RedisSection.razor            # Section Redis
    ├── StorageSection.razor          # Section Storage
    ├── ApiClientSection.razor        # Section API Client
    └── AdminSection.razor             # Section Admin
```

### 4. Externaliser la Logique de Navigation

**Services proposés** :

```
Services/Layout/
├── ILayoutStateService.cs           # État global du layout
├── LayoutStateService.cs
├── ITabService.cs                   # Gestion des tabs
├── TabService.cs
├── INavigationService.cs            # Services de navigation
├── NavigationService.cs
├── ICommandService.cs               # Gestion des commandes globales
└── CommandService.cs
```

### 5. Améliorer les Composants Partagés

- **SidePanel.razor** : Optimiser pour plusieurs usages
- **PageHeader.razor** : Rend-le plus flexible
- **PageToolbar.razor** : Simplifier et rendre générique

## 📋 Tâches Détaillées

### Phase 1: Préparation (1/2 jour)
- [ ] Analyser MainLayout.razor et sa structure
- [ ] Analyser TopBar.razor et ses fonctionnalités
- [ ] Analyser LeftNav.razor et la hiérarchie de navigation
- [ ] Documenter l'état actuel et les flows

### Phase 2: Créer les Services de Layout (1-2 jours)
- [ ] Créer ILayoutStateService + implémentation
- [ ] Créer ITabService + implémentation
- [ ] Créer INavigationService + implémentation
- [ ] Créer ICommandService + implémentation
- [ ] Mettre à jour MauiProgram.cs
- [ ] Tester tous les services

### Phase 3: Décomposer TopBar (1-2 jours)
- [ ] Créer les sections spécialisées
- [ ] Créer les composants enfants
- [ ] Extraire la logique depuis TopBar
- [ ] Réduire TopBar.razor à 50-80 lignes
- [ ] Intégrer avec les nouveaux services
- [ ] Tester chaque section

### Phase 4: Décomposer LeftNav (1-2 jours)
- [ ] Créer NavGroup et NavGroupHeader
- [ ] Créer les types de NavItem
- [ ] Créer les sections par feature
- [ ] Extraire la logique depuis LeftNav
- [ ] Réduire LeftNav.razor à 100-150 lignes
- [ ] Tester la navigation

### Phase 5: Décomposer MainLayout (1-2 jours)
- [ ] Créer AppShell.razor
- [ ] Extraire TabManager
- [ ] Extraire NotificationHost
- [ ] Réduire MainLayout.razor à 100-150 lignes
- [ ] Intégrer tous les sous-composants
- [ ] Tester l'intégration complète

### Phase 6: Optimiser les Composants Partagés (1/2 jour)
- [ ] Optimiser SidePanel
- [ ] Améliorer PageHeader
- [ ] Améliorer PageToolbar
- [ ] Tester tous les composants partagés

### Phase 7: Tests et Validation (1-2 jours)
- [ ] Tests unitaires pour les services
- [ ] Tests de composants pour les nouveaux composants
- [ ] Tests d'intégration
- [ ] Tests de régression
- [ ] Validation manuelle de l'UI
- [ ] Validation des raccourcis clavier

## 🎯 Améliorations de Performances

### 1. Optimisation des Tabs
- Lazy loading des tabs inactifs
- Mémorisation de l'état des tabs
- Restore rapide de l'état

### 2. Optimisation de la Navigation
- Cache des items de navigation
- Rendering efficace des menus
- Scroll performant pour les grands menus

### 3. Optimisation des Commandes
- Registry efficace des commandes
- Keybindings optimisés
- Pas de memory leak dans les subscriptions

### 4. Cleanup des Resources
```csharp
// Implémenter IDisposable correctement
public class LayoutStateService : IDisposable
{
    private bool _disposed;
    
    public void Dispose()
    {
        if (!_disposed)
        {
            // Cleanup des subscriptions
            _tabService.TabChanged -= OnTabChanged;
            _navigationService.Navigated -= OnNavigated;
            
            _disposed = true;
        }
    }
}
```

## 🧪 Stratégie de Tests

- Tests unitaires pour les services de layout
- Tests de composants pour tous les nouveaux composants
- Tests d'intégration pour la navigation
- Tests des raccourcis clavier
- Tests de régression pour toutes les fonctionnalités

### Couverture Cible
- Services : **> 85%**
- Composants : **> 80%**

## 📁 Fichiers à Créer/Modifier

### Nouveaux Fichiers
```
src/SwebKit.App/Components/Layout/
├── Shell/
│   ├── AppShell.razor
│   ├── TabManager.razor
│   └── NotificationHost.razor
├── Toolbars/
│   ├── TopBar.razor
│   ├── Sections/
│   │   ├── WorkspaceSelectSection.razor
│   │   ├── SearchSection.razor
│   │   ├── CommandsSection.razor
│   │   ├── TabsSection.razor
│   │   └── UserSection.razor
│   └── Components/
│       ├── WorkspaceSelect.razor
│       ├── GlobalSearch.razor
│       └── UserMenu.razor
└── Navigation/
    ├── LeftNav.razor
    ├── NavGroups/
    │   ├── NavGroup.razor
    │   ├── ExpandableNavGroup.razor
    │   └── NavGroupHeader.razor
    ├── NavItems/
    │   ├── NavItem.razor
    │   ├── NavItemWithIcon.razor
    │   ├── NavItemWithBadge.razor
    │   └── NavItemCustom.razor
    └── NavSections/ (6 sections spécialisées)

src/SwebKit.App/Services/Layout/
├── ILayoutStateService.cs
├── LayoutStateService.cs
├── ITabService.cs
├── TabService.cs
├── INavigationService.cs
├── NavigationService.cs
├── ICommandService.cs
└── CommandService.cs
```

### Components/Shared à améliorer
- `Components/Shared/SidePanel.razor`
- `Components/Shared/PageToolbar.razor`
- `Components/Shared/PageHeader.razor`
- `Components/Shared/PageTitle.razor`

### Fichiers à Modifier
- `src/SwebKit.App/App.xaml` (MAIL)
- `src/SwebKit.App/App.xaml.cs`
- `src/SwebKit.App/MauiProgram.cs`
- Tous les fichiers dépendant de MainLayout/TopBar/LeftNav

## ⚠️ Risques et Atténuation

| Risque | Impact | Atténuation |
|--------|--------|-------------|
| Rupture de la navigation | Élevé | Tests extensifs de navigation |
| Problèmes de tabs | Élevé | Tests des tabs\
| Rupture des raccourcis | Moyen | Tests des raccourcis |
| Problèmes d'UI | Moyen | Validation manuelle UI |

## 📊 Métriques de Succès

| Métrique | Avant | Après | Statut |
|----------|-------|-------|--------|
| Lignes MainLayout | 685 | < 150 | À faire |
| Lignes TopBar | 485 | < 80 | À faire |
| Nombre de composants layout | ~10 | ~25-30 | À faire |
| Couverture de tests | ~25% | > 80% | À faire |
| Temps d'initialisation | TBR | TBR | À faire |

---

## 📚 Documentation Connexe
- [Architecture globale](../../../architecture/architecture.md)
- [design.md - App Bootstrap Flow](../../../architecture/design.md#app-bootstrap-flow)
- [Codebase Guide - Entry Points](../../../architecture/codebase-guide.md#entry-points-by-task-type)

---

*Créé le: {{date}}*
*Statut: En planification*
*Priorité: 🟡 ÉLEVÉE*

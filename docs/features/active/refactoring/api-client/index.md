# Refactoring Feature: API Client

## 🎯 Objectif Global

**Simplifier et modulariser** la feature **API Client** en décomposant les fichiers monolithes, particulièrement `ApiClientPage.razor` (1,975 lignes) et `CollectionTree.razor` (896 lignes).

## 📊 État Actuel

### Fichiers Critiques

| Fichier | Lignes | Taille | Complexité | Priorité |
|--------|--------|--------|------------|----------|
| `ApiClientPage.razor` | **1,975** | 81.4 KB | ⭐⭐⭐⭐⭐ | 🔴 CRITIQUE |
| `CollectionTree.razor` | **896** | 35.5 KB | ⭐⭐⭐⭐ | 🔴 CRITIQUE |
| `ApiClientRequestWorkspace.razor` | **331** | 15.2 KB | ⭐⭐⭐ | 🟡 ÉLEVÉE |
| `RequestBuilderPanel.razor` | **621** | 26.3 KB | ⭐⭐⭐⭐ | 🟡 ÉLEVÉE |

### Problèmes Identifiés

1. **ApiClientPage.razor** (1,975 lignes)
   - ❌ **Composant géant** : Gère collections + requests + environments + workspace
   - ❌ **État complexe** : Multiples états à synchroniser
   - ❌ **Code dupliqué** : Logique similaire dans différents panels
   - ❌ **Difficile à maintenir** : Changements impactent toute la page

2. **CollectionTree.razor** (896 lignes)
   - ❌ **Trop de logique dans un seul fichier**
   - ❌ **Arbre complexe** : Gestion des folders, collections, requests
   - ❌ **Drag & Drop complexe** : Logique de réorganisation
   - ❌ **Gestion des sélections** : Multiples modes de sélection

3. **RequestBuilderPanel.razor** (621 lignes)
   - ❌ **Trop de champs** : Gestion de tous les types de requêtes
   - ❌ **Logique complexe** : Build des headers, body, auth, etc.
   - ❌ **Difficile à tester** : Beaucoup de logique métier dans le Razor

## ✅ Objectifs Spécifiques

### 1. Décomposer ApiClientPage.razor

**Cible** : 1 page coordinateur + 8-10 sous-composants spécialisés.

**Structure proposée** :
```
Components/ApiClient/
├── ApiClientPage.razor                 # Coordinateur (~100-150 lignes)
│   - Gère l'état global
│   - Coordonne les panels
│   - Gère la navigation entre les campagnes
│
├── Panels/
│   ├── ApiClientMainPanel.razor        # Panel principal (collections)
│   ├── ApiClientToolbar.razor          # Barre d'outils globale
│   ├── RequestWorkspacePanel.razor    # Espace de travail requête
│   ├── CollectionTreePanel.razor      # Panel de l'arbre
│   ├── EnvironmentPanel.razor          # Panel des environments
│   └── GitPanel.razor                  # Panel Git
│
├── Workspaces/
│   ├── UnifiedRequestWorkspace.razor  # Workspace principal
│   ├── RequestBuilderWorkspace.razor  # Builder de requêtes
│   ├── HistoryWorkspace.razor          # Historique
│   └── VariablesWorkspace.razor        # Variables
│
└── Components/
    ├── CollectionTree.razor            # Arbre des collections
    ├── RequestBuilder.razor            # Builder de requêtes
    └── (autres composants...)
```

### 2. Décomposer CollectionTree.razor

**Cible** : 1 arbre générique + des nœuds spécialisés.

**Structure proposée** :
```
Components/ApiClient/CollectionTree/
├── CollectionTree.razor                # Arbre principal (~150-200 lignes)
│   - Gère la structure de l'arbre
│   - Coordonne les nœuds
│   - Gère la sélection
│   - Gère le Drag & Drop
│
├── TreeNodes/
│   ├── TreeNode.razor                 # Nœud de base
│   ├── FolderNode.razor               # Nœud dossier
│   ├── CollectionNode.razor           # Nœud collection
│   └── RequestNode.razor              # Nœud requête
│
├── Services/
│   ├── CollectionTreeService.cs       # Services pour l'arbre
│   ├── TreeDragDropService.cs         # Drag & Drop
│   └── TreeSelectionService.cs        # Gestion de la sélection
│
└── Models/
    └── TreeNodeModel.cs               # Modèles pour l'arbre
```

### 3. Décomposer RequestBuilderPanel.razor

**Cible** : 1 builder générique + des sections spécialisées.

**Structure proposée** :
```
Components/ApiClient/RequestBuilder/
├── RequestBuilderPanel.razor          # Builder principal (~150-200 lignes)
│   - Coordonne les sections
│   - Gère l'état de la requête
│   - Valide la requête
│
├── RequestSections/
│   ├── RequestMethodSection.razor     # Méthode HTTP
│   ├── RequestUrlSection.razor        # URL
│   ├── RequestHeadersSection.razor   # Headers
│   ├── RequestBodySection.razor       # Body
│   ├── RequestAuthSection.razor       # Authentification
│   ├── RequestParamsSection.razor     # Paramètres
│   └── RequestTabs.razor               # Onglets du builder
│
├── Services/
│   ├── RequestBuilderService.cs       # Construction de la requête
│   ├── RequestValidationService.cs    # Validation
│   └── RequestDetecterService.cs       # Détection automatique
│
└── Models/
    ├── RequestModel.cs                # Modèle de la requête
    ├── HeaderModel.cs                 # Modèle header
    └── (autres modèles...)
```

### 4. Externaliser la Logique Métier

**Services proposés** :

| Service | Responsabilité |
|---------|---------------|
| `IApiClientWorkflowService.cs` | Gestion du workflow général |
| `ICollectionService.cs` | Gestion des collections |
| `IRequestExecutionService.cs` | Exécution des requêtes |
| `IEnvironmentService.cs` | Gestion des environments |
| `IGitService.cs` | Gestion de l'intégration Git |
| `ICredentialService.cs` | Gestion des credentials |

## 📋 Tâches Détaillées

### Phase 1: Préparation (1 jour)
- [ ] Analyser la structure actuelle d'ApiClientPage
- [ ] Identifier tous les workflows de l'API Client
- [ ] Documenter les dépendances
- [ ] Analyser les flows de données

### Phase 2: Décomposer CollectionTree (2-3 jours)
- [ ] Créer la structure de dossiers
- [ ] Créer `TreeNode.razor` (base)
- [ ] Créer les nœuds spécialisés
- [ ] Créer `TreeDragDropService.cs`
- [ ] Créer `TreeSelectionService.cs`
- [ ] Mettre à jour `CollectionTree.razor`
- [ ] Tester l'arbre

### Phase 3: Décomposer RequestBuilder (2-3 jours)
- [ ] Créer la structure RequestBuilder/
- [ ] Créer les sections spécialisées
- [ ] Créer `RequestBuilderService.cs`
- [ ] Extraire la logique de RequestBuilderPanel
- [ ] Réduire RequestBuilderPanel.razor
- [ ] Tester le builder

### Phase 4: Décomposer ApiClientPage (2-3 jours)
- [ ] Créer les panels enfants
- [ ] Extraire la logique de coordination
- [ ] Réduire ApiClientPage.razor à 100-150 lignes
- [ ] Intégrer tous les sous-composants
- [ ] Tester l'intégration

### Phase 5: Refactorer les autres composants (1-2 jours)
- [ ] Optimiser ApiClientRequestWorkspace
- [ ] Optimiser UnifiedRequestWorkspace
- [ ] Nettoyer le code

### Phase 6: Tests (2 jours)
- [ ] Tests unitaires pour les services
- [ ] Tests de composants pour les nouveaux composants
- [ ] Tests d'intégration
- [ ] Tests de régression

## 🎯 Améliorations de Performances

### 1. Virtualisation de l'Arbre
```razor
<Virtualize Items="@_treeNodes" ChildContent="RenderNode">
    @context RenderNode(context)
</Virtualize>

@if (VirtualizeHasMore)
{
    <spinner />
}
```

### 2. Cache des Requêtes
- Cache des réponses pour éviter de refaire des requêtes identiques
- Cache des résultats de détection
- Cache des validations

### 3. Lazy Loading de l'Arbre
- Charger les sous-arbores à la demande
- Charger les requêtes d'une collection à la demande

### 4. Optimization du Builder
- Éviter les `StateHasChanged()` inutiles
- Utiliser `IComponentRender` pour détecter les changements
- Gérer le dirty state efficacement

## 🧪 Stratégie de Tests

### Tests Unitaires
- Services de workflow
- Services de collection
- Services d'exécution de requêtes
- Services de validation

### Tests de Composants
- ApiClientPage
- Tous les panels
- CollectionTree
- RequestBuilder
- Tous les nodes de l'arbre

### Couverture Cible
- Services : **> 90%**
- Composants : **> 85%**

## 📁 Fichiers à Créer/Modifier

### Nouveaux Fichiers (Structure Cible)
```
src/SwebKit.App/Components/ApiClient/
├── ApiClientPage.razor                 # Réécrit (100-150 lignes)
├── ApiClientMainPanel.razor           # Panel principal
├── ApiClientToolbar.razor             # Barre d'outils
├── CollectionTree.razor               # Réécrit (150-200 lignes)
│
├── Panels/
│   ├── RequestWorkspacePanel.razor
│   ├── EnvironmentPanel.razor
│   └── GitPanel.razor
│
├── Workspaces/
│   ├── UnifiedRequestWorkspace.razor
│   ├── RequestBuilderWorkspace.razor
│   ├── HistoryWorkspace.razor
│   └── VariablesWorkspace.razor
│
├── CollectionTree/
│   ├── TreeNodes/
│   │   ├── TreeNode.razor
│   │   ├── FolderNode.razor
│   │   ├── CollectionNode.razor
│   │   └── RequestNode.razor
│   └── Services/
│       ├── CollectionTreeService.cs
│       └── TreeDragDropService.cs
│
└── RequestBuilder/
    ├── RequestBuilderPanel.razor
    └── RequestSections/
        ├── RequestMethodSection.razor
        ├── RequestUrlSection.razor
        ├── RequestHeadersSection.razor
        └── (autres sections...)
```

### Services à Créer
```
src/SwebKit.App/Services/ApiClient/
├── IApiClientWorkflowService.cs
├── ApiClientWorkflowService.cs
├── ICollectionService.cs
├── CollectionService.cs
└── (autres services...)
```

### Fichiers à Modifier
- `src/SwebKit.App/MauiProgram.cs`
- Tous les fichiers dépendant d'ApiClientPage
- Configuration des dépendances

## ⚠️ Risques et Atténuation

| Risque | Impact | Atténuation |
|--------|--------|-------------|
| Rupture du workflow existant | Élevé | Migration progressive, tests extensifs |
| Problèmes de drag & drop | Moyen | Tests spécifiques |
| Régécution dans la détection | Moyen | Tests unitaires complets |

## 📊 Métriques de Succès

| Métrique | Avant | Après | Statut |
|----------|-------|-------|--------|
| Lignes ApiClientPage | 1,975 | < 150 | À faire |
| Lignes CollectionTree | 896 | < 200 | À faire |
| Nombre de composants | ~15 | ~30-40 | À faire |
| Couverture de tests | ~45% | > 85% | À faire |

---

## 📚 Documentation Connexe
- [API Client Functionalities](../../../architecture/functionalities/api-client.md)
- [Architecture globale](../../../architecture/architecture.md)

---

*Créé le: {{date}}*
*Statut: En planification*
*Priorité: 🔴 CRITIQUE*

# Refactoring Feature: AKS (Kubernetes)

## 🎯 Objectif Global

**Réduire la complexité et améliorer la maintenabilité** de la feature AKS en décomposant les fichiers monolithes en composants et services modulaire, tout en garantissant une **amélioration des performances** et une **meilleure testabilité**.

## 📊 État Actuel

### Fichiers Critiques (à refactorer)

| Fichier | Lignes | Taille | Complexité | Priorité |
|--------|--------|--------|------------|----------|
| `KubernetesAksClient.cs` | **4,445** | 184.2 KB | ⭐⭐⭐⭐⭐ | 🔴 CRITIQUE |
| `AksPage.razor` | **2,939** | 148.2 KB | ⭐⭐⭐⭐ | 🔴 CRITIQUE |
| `AksDetailPanels.razor` | **1,087** | 53.2 KB | ⭐⭐⭐ | 🔴 CRITIQUE |
| `MultiPodLogView.razor` | **954** | 34.4 KB | ⭐⭐⭐ | 🟡 ÉLEVÉE |
| `PodLogView.razor` | **884** | 31.7 KB | ⭐⭐⭐ | 🟡 ÉLEVÉE |
| `AksYamlViewer.razor` | **472** | 18.7 KB | ⭐⭐ | 🟡 ÉLEVÉE |
| `AksConnectionBar.razor` | **434** | 17.2 KB | ⭐⭐ | 🟡 ÉLEVÉE |

### Problèmes Identifiés

1. **KubernetesAksClient.cs** (4,445 lignes)
   - ❌ **Trop de responsabilités** : Gestion des pods, deployments, services, ingress, logs, shell, YAML, port-forward, etc.
   - ❌ **Métodes trop longues** : Méthodes de 100+ lignes
   - ❌ **Duplication de code** : Logique similaire pour différents types de ressources
   - ❌ **Difficile à tester** : Trou de dépendances directes avec Kubernetes SDK
   - ❌ **Mauvaise performance** : Chargement de toutes les données avant affichage

2. **AksPage.razor** (2,939 lignes)
   - ❌ **Composant géant** : Contient toute la logique de page + sous-composants
   - ❌ **État complexe** : Gestion manuelle de StateHasChanged
   - ❌ **Mélange de couches** : Logique métier + présentation + coordination
   - ❌ **Difficile à maintenir** : Changements impactent toute la page

3. **AksDetailPanels.razor** (1,087 lignes)
   - ❌ **Trop de panels différents** : Pod, Deployment, Service, Ingress, etc. dans un seul fichier
   - ❌ **Logique de rendu complexe** : Conditions nested profondes
   - ❌ **Duplication** : Chaque panel a sa propre logique de chargement

## ✅ Objectifs Spécifiques

### 1. Décomposer KubernetesAksClient.cs

**Cible** : 5-7 services spécialisés de **300-500 lignes max** chacun

| Nouveau Service | Responsabilité | Lignes Estimées | interfaces |
|-----------------|---------------|-----------------|------------|
| `IPodService` | Gestion des Pods (list, get, logs, exec, delete) | 400-450 | ✅ |
| `IDeploymentService` | Gestion des Deployments (list, scale, restart, YAML) | 350-400 | ✅ |
| `IServiceService` | Gestion des Services Kubernetes | 250-300 | ✅ |
| `IIngressService` | Gestion des Ingress | 200-250 | ✅ |
| `IHelmService` | Gestion Helm (releases, history, rollback) | 300-350 | ✅ |
| `IResourceService` | Opérations génériques (YAML, delete, restart) | 300-350 | ✅ |
| `IKubernetesContextService` | Gestion du contexte Kubernetes (kubeconfig, namespace) | 200-250 | ✅ |

### 2. Décomposer AksPage.razor

**Cible** : 1 fichier principal + 8-10 sous-composants spécialisés

```
Components/Aks/
├── AksPage.razor                    # Coordination seulement (~150 lignes)
├── AksToolbar.razor                # Barre d'outils principale
├── ResourceGrid.razor              # Grille générique des ressources
├── NamespaceSelector.razor          # Sélecteur de namespaces
├── ContextSelector.razor           # Sélecteur de contexte K8s
├── AksFilters.razor                # Filtres avancés
├── AksStats.razor                  # Statistiques du cluster
└── AksSidePanel.razor              # Rail latéral avec panels
```

### 3. Décomposer AksDetailPanels.razor

**Cible** : 1 composant parent + panels spécialisés

```
Components/Aks/Panels/
├── AksDetailPanels.razor           # Coordination seulement (~50 lignes)
├── AksPodDetailPanel.razor         # Détails d'un Pod
├── AksDeploymentDetailPanel.razor  # Détails d'un Deployment
├── AksServiceDetailPanel.razor     # Détails d'un Service
├── AksIngressDetailPanel.razor     # Détails d'un Ingress
├── AksHelmDetailPanel.razor        # Détails Helm
├── AksJobDetailPanel.razor         # Détails d'un Job
├── AksCronJobDetailPanel.razor     # Détails d'un CronJob
├── AksConfigMapDetailPanel.razor   # Détails ConfigMap/Secret
└── AksEventDetailPanel.razor       # Détails des événements
```

### 4. Améliorer MultiPodLogView.razor et PodLogView.razor

**Cible** : Extraire la logique dans des services dédiés

| Composant | Action |
|-----------|--------|
| `MultiPodLogView.razor` | Extraire dans `PodLogAggregatorService` |
| `PodLogView.razor` | Extraire dans `PodLogStreamingService` |
| Logique commune | Créer `IPodLogService` |

## 🏗️ Architecture Cible

```
┌─────────────────────────────────────────────────────────────┐
│                    AksPage.razor (Coordinateur)                  │
└─────────────────────────────────────────────────────────────┘
                              │
              ┌───────────────────┼───────────────────┐
              │                   │                   │
              ▼                   ▼                   ▼
┌─────────────────┐   ┌─────────────────┐   ┌─────────────────┐
│   AksToolbar     │   │  ResourceGrid    │   │  AksSidePanel    │
│   (Barre outils) │   │  (Grille res.)    │   │  (Panels lat.)   │
└─────────────────┘   └─────────────────┘   └─────────────────┘
              │                   │                   │
              └───────────────────┼───────────────────┘
                              │
              ▼
┌─────────────────────────────────────────────────────────────┐
│                    KubernetesAksClient (Ancien)                   │
│                    ⬇ Décomposé en :                               │
├─────────────┬──────────────┬──────────────┬──────────────────┤
│ PodService   │ DeploymentSvc │ ServiceService │ ... (7 services)  │
└─────────────┴──────────────┴──────────────┴──────────────────┘
                              │
              ▼
┌─────────────────────────────────────────────────────────────┐
│                 IAksClient (Interface Unifiée)                     │
│                 Aggregateur des services spécialisés             │
└─────────────────────────────────────────────────────────────┘
```

## 📋 Tâches Détaillées

### Phase 1: Préparation (1 jour)

- [ ] Analyser les dépendances de `KubernetesAksClient.cs`
- [ ] Identifier les interfaces existantes à réutiliser
- [ ] Créer le diagramme d'architecture cible (Mermaid)
- [ ] Préparer les tests unitaires existants pour migration
- [ ] Documenter toutes les dépendances externes (K8s SDK, etc.)

### Phase 2: Décomposition Backend (5-7 jours)

#### Sous-Phase 2.1: Créer les interfaces (1 jour)
- [ ] Créer `IPodService.cs` avec méthodes async
- [ ] Créer `IDeploymentService.cs` 
- [ ] Créer `IServiceService.cs`
- [ ] Créer `IIngressService.cs`
- [ ] Créer `IHelmService.cs`
- [ ] Créer `IResourceService.cs`
- [ ] Créer `IKubernetesContextService.cs`
- [ ] Créer `IAksClient.cs` (interface unifiée)

#### Sous-Phase 2.2: Implémenter les services (3-4 jours)
- [ ] Implémenter `PodService.cs` avec extraction des méthodes de pods
- [ ] Implémenter `DeploymentService.cs` avec extraction des déploiements
- [ ] Implémenter `ServiceService.cs` pour les Services K8s
- [ ] Implémenter `IngressService.cs` pour les Ingress
- [ ] Implémenter `HelmService.cs` pour Helm
- [ ] Implémenter `ResourceService.cs` pour les opérations communes
- [ ] Implémenter `KubernetesContextService.cs` pour le contexte

#### Sous-Phase 2.3: Créer l'aggrégateur (1 jour)
- [ ] Créer `AksClientAggregator.cs` implémentant `IAksClient`
- [ ] Déleguer les appels aux services appropriés
- [ ] Maintenir la compatibilité avec l'ancien API

#### Sous-Phase 2.4: Migration progressive (1 jour)
- [ ] Remplacer l'injection de `KubernetesAksClient` par `IAksClient`
- [ ] Mettre à jour `MauiProgram.cs` pour registre les nouveaux services
- [ ] Tester chaque service individuellement

### Phase 3: Décomposition Frontend (3-5 jours)

#### Sous-Phase 3.1: Extraire AksToolbar (1/2 jour)
- [ ] Créer `AksToolbar.razor` avec toute la logique de barre d'outils
- [ ] Extraire la gestion des tolltips et des menus contextuels
- [ ] Maintenir les raccourcis clavier

#### Sous-Phase 3.2: Extraire ResourceGrid (1-2 jours)
- [ ] Créer `ResourceGrid.razor` générique et réutilisable
- [ ] Extraire la logique de filtrage et de tri
- [ ] Extraire la logique de sélection multiple
- [ ] Extraire les colonnes personnalisables

#### Sous-Phase 3.3: Extraire AksSidePanel (1 jour)
- [ ] Créer `AksSidePanel.razor` comme conteneur de panels
- [ ] Implémenter la logique d'ouverture/fermeture
- [ ] Gérer la persistance de l'état des panels

#### Sous-Phase 3.4: Décomposer AksPage (1/2 jour)
- [ ] Réduire `AksPage.razor` à la coordination uniquement
- [ ] Extraire toute la logique métier dans des services
- [ ] Utiliser les nouveaux composants enfants

### Phase 4: Décomposition des Panels (2-3 jours)

#### Sous-Phase 4.1: Créer la structure des panels
- [ ] Créer le dossier `Components/Aks/Panels/`
- [ ] Créer `AksDetailPanels.razor` comme coordinateur

#### Sous-Phase 4.2: Décomposer chaque panel
- [ ] `AksPodDetailPanel.razor` (extraire de AksDetailPanels)
- [ ] `AksDeploymentDetailPanel.razor`
- [ ] `AksServiceDetailPanel.razor`
- [ ] `AksIngressDetailPanel.razor`
- [ ] `AksHelmDetailPanel.razor`
- [ ] `AksJobDetailPanel.razor`
- [ ] `AksCronJobDetailPanel.razor`
- [ ] `AksConfigMapDetailPanel.razor`
- [ ] `AksEventDetailPanel.razor`

### Phase 5: Amélioration des Logs (1-2 jours)

- [ ] Créer `IPodLogService.cs` pour la logique commune des logs
- [ ] Extraire `PodLogStreamingService.cs` pour le streaming
- [ ] Extraire `PodLogAggregatorService.cs` pour l'aggrégation multi-pods
- [ ] Réduire `PodLogView.razor` et `MultiPodLogView.razor`

### Phase 6: Tests et Validation (2-3 jours)

- [ ] Créer des tests unitaires pour chaque nouveau service
- [ ] Tester la compatibilité ascendante
- [ ] Vérifier les performances (pas de régression)
- [ ] Validation manuelle de toutes les fonctionnalités
- [ ] Mise à jour de la documentation

## 🎯 Améliorations de Performances

### 1. Chargement Paresseux (Lazy Loading)
```csharp
// ✅ Utiliser Lazy<T> pour les services lourds
private readonly Lazy<IPodService> _podService;

// ✅ Charger les données à la demande
private async Task LoadPodsIfNeededAsync()
{
    if (_pods == null && !_isLoadingPods)
    {
        _isLoadingPods = true;
        StateHasChanged();
        _pods = await _podService.GetPodsAsync(_currentNamespace);
        _isLoadingPods = false;
        StateHasChanged();
    }
}
```

### 2. Minimiser StateHasChanged()
```csharp
// ❌ À éviter
private void OnSomeEvent()
{
    _someValue = newValue;
    StateHasChanged();  // Appelé trop souvent
}

// ✅ Préférer
private void OnSomeEvent()
{
    if (_someValue != newValue)
    {
        _someValue = newValue;
        StateHasChanged();
    }
}
```

### 3. Cache des requêtes fréquentes
```csharp
// ✅ Implémenter un cache simple
private readonly Dictionary<string, List<PodModel>> _podCache = new();
private readonly Dictionary<string, DateTime> _cacheTimestamps = new();

private async Task<List<PodModel>> GetPodsWithCacheAsync(string namespace)
{
    if (_podCache.TryGetValue(namespace, out var cached) &&
        (DateTime.Now - _cacheTimestamps[namespace]).TotalSeconds < 30)
    {
        return cached;
    }
    
    var pods = await _podService.GetPodsAsync(namespace);
    _podCache[namespace] = pods;
    _cacheTimestamps[namespace] = DateTime.Now;
    return pods;
}
```

### 4. Virtualisation des grilles
```razor
// ✅ Utiliser Virtualize pour les grandes listes
<Virtualize Items="@_allPods" Context="pod">
    <PodRow Pod="pod" OnSelected="HandlePodSelected" />
</Virtualize>
```

## 🧪 Stratégie de Tests

Voir [test-plan.md](./test-plan.md) pour les détails complets.

### Types de Tests
1. **Tests unitaires** : Chaque service testé individuellement avec des mocks
2. **Tests d'intégration** : Interaction entre services
3. **Tests de composants** : Composants Razor avec bUnit
4. **Tests de performance** : Vérifier les temps de réponse

### Couverture Cible
- Services : **> 90%**
- Composants : **> 80%**
- Logique métier : **100%**

## 📁 Fichiers à Créer/Modifier

### Nouveaux Fichiers
```
src/SwebKit.Kubernetes/AksClient/
├── Services/
│   ├── Interfaces/
│   │   ├── IPodService.cs
│   │   ├── IDeploymentService.cs
│   │   ├── IServiceService.cs
│   │   ├── IIngressService.cs
│   │   ├── IHelmService.cs
│   │   ├── IResourceService.cs
│   │   ├── IKubernetesContextService.cs
│   │   └── IAksClient.cs
│   ├── Implementations/
│   │   ├── PodService.cs
│   │   ├── DeploymentService.cs
│   │   ├── ServiceService.cs
│   │   ├── IngressService.cs
│   │   ├── HelmService.cs
│   │   ├── ResourceService.cs
│   │   ├── KubernetesContextService.cs
│   │   └── AksClientAggregator.cs
│   └── Models/
│       └── (DTOs spécifiques aux services)
│
src/SwebKit.App/Components/Aks/
├── Panels/
│   ├── AksDetailPanels.razor
│   ├── AksPodDetailPanel.razor
│   ├── AksDeploymentDetailPanel.razor
│   ├── AksServiceDetailPanel.razor
│   ├── AksIngressDetailPanel.razor
│   ├── AksHelmDetailPanel.razor
│   ├── AksJobDetailPanel.razor
│   ├── AksCronJobDetailPanel.razor
│   ├── AksConfigMapDetailPanel.razor
│   └── AksEventDetailPanel.razor
├── AksPage.razor
├── AksToolbar.razor
├── ResourceGrid.razor
├── NamespaceSelector.razor
├── ContextSelector.razor
├── AksFilters.razor
├── AksStats.razor
└── AksSidePanel.razor

src/SwebKit.Kubernetes/AksClient/
├── Logs/
│   ├── IPodLogService.cs
│   ├── PodLogStreamingService.cs
│   └── PodLogAggregatorService.cs
```

### Fichiers à Modifier
- `src/SwebKit.App/MauiProgram.cs` - Enregistrement des services
- `src/SwebKit.App/Components/Layout/MainLayout.razor` - Intégration du nouveau AksPage
- `src/SwebKit.Core/Abstractions/IAksClient.cs` - Mise à jour ou remplacement
- Tous les fichiers qui dépendent de `KubernetesAksClient`

### Fichiers à Supprimer (après migration)
- `src/SwebKit.Kubernetes/AksClient/KubernetesAksClient.cs` (remplacé par les services)

## 🔄 Migration Progressive

Pour minimiser les risques, la migration se fera de manière progressive :

1. **Étape 1** : Créer les nouvelles interfaces et implémentations
2. **Étape 2** : Injecter les nouveaux services à côté de l'ancien client
3. **Étape 3** : Migrer progressivement les appels vers les nouveaux services
4. **Étape 4** : Une fois tout migré, retirer l'ancien client

## ⚠️ Risques et Atténuation

| Risque | Probabilité | Impact | Atténuation |
|--------|-------------|--------|-------------|
| Rupture de compatibilité | Moyenne | Élevé | Tests extensifs, migration progressive |
| Régression de performance | Faible | Moyen | Benchmark avant/après |
| Bugs dans le nouveau code | Moyenne | Moyen | Code review, tests unitaires |
| Temps de migration trop long | Élevée | Moyen | Décomposer en petites tâches |

## 📊 Métriques de Succès

| Métrique | Avant | Après | Amélioration |
|----------|-------|-------|--------------|
| Lignes max par fichier | 4,445 | < 500 | ✅ |
| Nombre de fichiers | ~10 | ~30 | ✅ |
| Couverture de tests | ~50% | > 80% | ✅ |
| Temps de chargement | TBR | TBR | ✅ |
| Complexité cyclomatique | > 50 | < 15 | ✅ |

## 🎯 Prochaines Étapes

1. **Commencer par** : Créer les interfaces des services Kubernetes
2. **Puis** : Implémenter `PodService.cs` (le plus utilisé)
3. **Ensuite** : Migrer `AksPage.razor` pour utiliser les nouveaux services
4. **Enfin** : Décomposer les panels et les logs

## 📚 Documentation Connexe

- [Architecture globale](../../../architecture/architecture.md)
- [AKS Functionalities](../../../architecture/functionalities/aks.md)
- [Codebase Guide](../../../architecture/codebase-guide.md)
- [KubernetesAksClient actuel](file:///D:/Projects/SwebKit/src/SwebKit.Kubernetes/AksClient/KubernetesAksClient.cs)

---

*Créé le: {{date}}*
*Statut: En planification*
*Priorité: 🔴 CRITIQUE*
*Responsable: À assigner*
*Sprint: À déterminer*

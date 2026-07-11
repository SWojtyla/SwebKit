# Refactoring Feature: Core Services

## 🎯 Objectif Global

**Améliorer la qualité et les performances** des **services core** en décomposant les fichiers trop volumineux, particulièrement `LinkedCollectionFileService.cs` (1,118 lignes) et `DemoAksClient.cs` (2,300 lignes).

## 📊 État Actuel

### Fichiers Critiques

| Fichier | Lignes | Taille | Priorité |
|--------|--------|--------|----------|
| `DemoAksClient.cs` | **2,300** | 97.9 KB | 🔴 CRITIQUE |
| `LinkedCollectionFileService.cs` | **1,118** | 49.9 KB | 🟡 ÉLEVÉE |
| `BrunoFolderImporter.cs` | **715** | 26.9 KB | 🟡 ÉLEVÉE |
| `ConfigurationHealthService.cs` | **706** | 30.6 KB | 🟡 ÉLEVÉE |
| `DemoServiceBusClient.cs` | **~200** | ? KB | 🟡 ÉLEVÉE |
| `DemoRedisClient.cs` | **685** | 25.6 KB | 🟡 ÉLEVÉE |

### Problèmes Identifiés

1. **DemoAksClient.cs** (2,300 lignes)
   - ❌ **Fake data generation géant** : Génération de fake AKS resources
   - ❌ **Duplication massive** : Code similaire pour différents types
   - ❌ **Difficile à maintenir** : Ajouter un nouveau type de ressource est complexe
   - ❌ **Pas de pattern clair** : Génération ad-hoc

2. **LinkedCollectionFileService.cs** (1,118 lignes)
   - ❌ **Trop de responsabilités** : Linked roots + collections + fichier I/O
   - ❌ **Complexe à tester** : Beaucoup de logique métier mélangée
   - ❌ **Performance sous-optimale** : Lecture/écriture fréquente des fichiers

3. **Demo clients en général** (DemoAksClient, DemoServiceBusClient, DemoRedisClient)
   - ❌ **Inconsistants** : Chaque demo client a son propre style
   - ❌ **Duplication** : Logique de generation similaire entre les demo clients

## ✅ Objectifs Spécifiques

### 1. Réusiner DemoAksClient.cs

**Cible** : Système modulaire de génération de fake data.

**Architecture proposée** :
```
Services/Demo/
├── Aks/
│   ├── Factories/
│   │   ├── PodFactory.cs
│   │   ├── DeploymentFactory.cs
│   │   ├── ServiceFactory.cs
│   │   ├── IngressFactory.cs
│   │   ├── ConfigMapFactory.cs
│   │   ├── SecretFactory.cs
│   │   ├── NamespaceFactory.cs
│   │   └── EventFactory.cs
│   ├── DemoAksContextService.cs      # Gestion du contexte demo
│   └── DemoAksResourceService.cs     # Génération des ressources
│
├── ServiceBus/
│   ├── Factories/
│   │   ├── QueueFactory.cs
│   │   ├── TopicFactory.cs
│   │   ├── SubscriptionFactory.cs
│   │   └── MessageFactory.cs
│   └── DemoServiceBusResourceService.cs
│
├── Redis/
│   ├── Factories/
│   │   ├── RedisKeyFactory.cs
│   │   └── RedisValueFactory.cs
│   └── DemoRedisResourceService.cs
│
├── Shared/
│   ├── DemoClientBase.cs              # Base class pour tous les demo clients
│   ├── DemoDataGenerator.cs          # Générateur de données aléatoires
│   ├── DemoResourceStore.cs          # Stockage des ressources demo
│   └── DemoModeSwitcher.cs           # Gestion du switch demo/real
│
└── DemoServiceAggregator.cs          # Agrégateur pour compatibilité
```

**Pattern de Factory** :
```csharp
public interface IResourceFactory<TResource, TOptions> where TResource : class
{
    TResource Create(TOptions options);
    TResource Create(string name, TOptions options);
    IEnumerable<TResource> CreateMany(int count, TOptions options);
}

public class PodFactory : ResourceFactoryBase<V1Pod, PodFactoryOptions>
{
    private readonly INameGenerator _nameGenerator;
    private readonly IStateGenerator _stateGenerator;
    
    public override V1Pod Create(PodFactoryOptions options)
    {
        return new V1Pod
        {
            Metadata = CreateObjectMeta(options),
            Spec = CreatePodSpec(options),
            Status = CreatePodStatus(options)
        };
    }
    
    private V1ObjectMeta CreateObjectMeta(PodFactoryOptions options)
    {
        return new V1ObjectMeta
        {
            Name = options.Name ?? _nameGenerator.Generate("pod"),
            Namespace = options.Namespace ?? _nameGenerator.GenerateNamespace(),
            CreationTimestamp = DateTimeOffset.UtcNow.Add(optionsAge ?? RandomAge()),
            Labels = options.Labels ?? GenerateLabels("app")
        };
    }
}
```

### 2. Réusiner LinkedCollectionFileService.cs

**Cible** : Séparer en 4-5 services spécialisés.

**Architecture proposée** :
```
Services/Configuration/
├── LinkedRoots/
│   ├── ILinkedRootService.cs
│   ├── LinkedRootService.cs
│   ├── ILinkedRootDiscoveryService.cs
│   ├── LinkedRootDiscoveryService.cs
│   └── LinkedRootFileService.cs
│
├── Collections/
│   ├── ICollectionService.cs
│   ├── CollectionService.cs
│   ├── ICollectionImportService.cs
│   ├── CollectionImportService.cs
│   └── CollectionExportService.cs
│
├── Files/
│   ├── ICollectionFileService.cs
│   ├── CollectionFileService.cs
│   ├── IFileSyncService.cs
│   └── FileSyncService.cs
│
└── Git/
    ├── ICollectionGitService.cs
    └── CollectionGitService.cs
```

**Avantages** :
- Séparation claire des responsabilités
- Meilleure testabilité
- Isolation des problèmes
- Réutilisabilité des services

### 3. Réusiner BrunoFolderImporter.cs

**Cible** : Décomposer en services spécialisés pour l'import.

```
Services/Bruno/
├── Import/
│   ├── IBrunoImportService.cs
│   ├── BrunoImportService.cs
│   ├── Parsers/
│   │   ├── IBrunoRequestParser.cs
│   │   ├── BrunoRequestParser.cs
│   │   ├── IBrunoCollectionParser.cs
│   │   └── BrunoCollectionParser.cs
│   └── Validators/
│       ├── IBrunoValidator.cs
│       └── BrunoValidator.cs
│
└── Export/
    ├── IBrunoExportService.cs
    └── BrunoExportService.cs
```

### 4. Réusiner ConfigurationHealthService.cs

**Cible** : Décomposer en services de health check par type.

```
Services/Health/
├── IConfigurationHealthService.cs
├── ConfigurationHealthAggregator.cs
├── Checks/
│   ├── IHealthCheck.cs (interface de base)
│   ├── AksHealthCheck.cs
│   ├── ServiceBusHealthCheck.cs
│   ├── RedisHealthCheck.cs
│   ├── StorageHealthCheck.cs
│   ├── ConnectionHealthCheck.cs
│   └── FileHealthCheck.cs
└── Models/
    ├── HealthCheckResult.cs
    ├── HealthStatus.cs
    └── HealthCheckContext.cs
```

## 📋 Tâches Détaillées

### Phase 1: Préparation (1 jour)
- [ ] Analyser DemoAksClient.cs intégralement
- [ ] Analyser LinkedCollectionFileService.cs
- [ ] Analyser les autres services core
- [ ] Identifier les patterns communs
- [ ] Documenter les dépendances

### Phase 2: Réusiner DemoAksClient (3-4 jours)
- [ ] Créer la structure Services/Demo/
- [ ] Créer les factories par type de ressource
- [ ] Créer DemoClientBase
- [ ] Créer les services généraux
- [ ] Créer l'agrégateur
- [ ] Mettre à jour toutes les dépendances
- [ ] Tester chaque factory
- [ ] Tester le demo mode

### Phase 3: Réusiner LinkedCollectionFileService (2-3 jours)
- [ ] Créer la structure Services/Configuration/
- [ ] Décomposer par domaine (LinkedRoots, Collections, Files)
- [ ] Créer les interfaces
- [ ] Implémenter les services
- [ ] Mettre à jour MauiProgram.cs
- [ ] Tester l'intégration

### Phase 4: Réusiner les autres services (2-3 jours)
- [ ] Réusiner BrunoFolderImporter
- [ ] Réusiner ConfigurationHealthService
- [ ] Mettre à jour DemoServiceBusClient
- [ ] Optimiser DemoRedisClient
- [ ] Tester chaque service

### Phase 5: Optimisation et Cleanup (1-2 jours)
- [ ] Élimer la duplication de code
- [ ] Appliquer des patterns cohérents
- [ ] Optimiser les performances
- [ ] Nettoyer le code mort
- [ ] Domingo todas las pruebas

### Phase 6: Tests (2 jours)
- [ ] Tests unitaires pour tous les services
- [ ] Tests d'intégration
- [ ] Tests de performance
- [ ] Tests de régression

## 🎯 Améliorations de Performances

### 1. Caching des Opérations de Fichiers
- `LinkedCollectionFileService` doit minimiser les I/O fichiers
- Utiliser un cache avec expiration temporelle
- Invalidations intelligentes du cache

### 2. Lazy Loading des Collections
- Charger les collections à la demande
- Une couche de cache pour éviter de recharger

### 3. Optimisation des Demo Clients
- Génération efficiente des ressources demo
- Cache des ressources fréquemment utilisées
- Réduction de la mémoire utilisée

### 4. async/await Efficace
- Éviter les blocages synchrones
- Paralléliser quand possible
- Gestion correcte des erreurs

## 🧪 Stratégie de Tests

- Tests unitaires pour chaque factory
- Tests unitaires pour chaque service
- Tests d'intégration pour les demo mode
- Tests de performance pour les opérations I/O
- Tests de régression pour toutes les fonctionnalités demo

### Couverture Cible
- **> 90%** pour tous les services core
- **> 85%** pour les factories

## 📁 Fichiers à Créer/Modifier

### Nouveaux Fichiers (Structure compl Burns)
```
src/SwebKit.Core/Services/Demo/
├── Aks/
│   └── Factories/ (8-10 factories)
├── ServiceBus/
│   └── Factories/ (4 factories)
├── Redis/
│   └── Factories/ (2 factories)
├── Shared/ (4 services partagés)
└── DemoServiceAggregator.cs

src/SwebKit.Core/Services/Configuration/
├── LinkedRoots/ (3 services)
├── Collections/ (3 services)
├── Files/ (2 services)
└── Git/ (1 service)

src/SwebKit.Core/Services/Health/
├── ConfigurationHealthAggregator.cs
├── IConfigurationHealthService.cs
└── Checks/ (6-8 health checks)

src/SwebKit.Core/Services/Bruno/
├── Import/ (2 services + parsers + validators)
└── Export/ (1 service)
```

### Fichiers à Modifier
- `src/SwebKit.App/MauiProgram.cs`
- `src/SwebKit.Core/Demo*/`
- Tous les fichiers dépendant des services core

### Fichiers à Supprimer (après migration)
- `src/SwebKit.Core/Services/DemoAksClient.cs`
- `src/SwebKit.Core/Services/LinkedCollectionFileService.cs`
- `src/SwebKit.Core/Services/BrunoFolderImporter.cs`
- `src/SwebKit.Core/Services/ConfigurationHealthService.cs`

## ⚠️ Risques et Atténuation

| Risque | Impact | Atténuation |
|--------|--------|-------------|
| Rupture du demo mode | Élevé | Tests extensifs demo mode |
| Incompatibilité de fichier | Moyen | Validations de compatibilité |
| Problèmes de health check | Moyen | Tests des health checks |
| Performance dégradée | Faible | Optimisations ciblées |

## 📊 Métriques de Succès

| Métrique | Avant | Après | Statut |
|----------|-------|-------|--------|
| Lignes DemoAksClient | 2,300 | Distribué | À faire |
| Lignes LinkedCollection | 1,118 | Distribué | À faire |
| Nombre de services new | 0 | 20-25 | À faire |
| Couverture de tests | ~30% | > 90% | À faire |
| Performance I/O | TBR | TBR | À faire |

---

## 📚 Documentation Connexe
- [Architecture globale](../../../architecture/architecture.md)
- [Core Services dans l'architecture](../../../architecture/architecture.md#swbkitcore)

---

*Créé le: {{date}}*
*Statut: En planification*
*Priorité: 🟡 ÉLEVÉE*

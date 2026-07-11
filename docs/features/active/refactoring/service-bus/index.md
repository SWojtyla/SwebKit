# Refactoring Feature: Service Bus

## 🎯 Objectif Global

**Réduire la complexité** et améliorer la maintenabilité de la feature **Service Bus** en décomposant les fichiers monolithes, particulièrement `MessageListView.razor` (1,816 lignes) et `ServiceBusPage.razor` (927 lignes).

## 📊 État Actuel

### Fichiers Critiques

| Fichier | Lignes | Taille | Complexité | Priorité |
|--------|--------|--------|------------|----------|
| `MessageListView.razor` | **1,816** | 81.4 KB | ⭐⭐⭐⭐⭐ | 🔴 CRITIQUE |
| `ServiceBusPage.razor` | **927** | 35.7 KB | ⭐⭐⭐⭐ | 🔴 CRITIQUE |
| `ServiceBusGrid.razor` | **463** | 18.7 KB | ⭐⭐⭐ | 🟡 ÉLEVÉE |
| `MessageDetailPane.razor` | **404** | 18.2 KB | ⭐⭐⭐ | 🟡 ÉLEVÉE |
| `MessageComposer.razor` | **526** | 22.7 KB | ⭐⭐⭐ | 🟡 ÉLEVÉE |
| `AzureServiceBusClient.cs` | **652** | 26.2 KB | ⭐⭐⭐⭐ | 🟡 ÉLEVÉE |

### Problèmes Identifiés

1. **MessageListView.razor**
   - ❌ **Trop de logique mélangée** : Filtrage + affichage + gestion d'état
   - ❌ **Complexité excessive** : 1,816 lignes avec des méthodes imbriquées
   - ❌ **Testabilité réduite** : Difficile de tester l'UI en isolation
   - ❌ **Performance sous-optimale** : Recalculs inutiles

2. **ServiceBusPage.razor**
   - ❌ **Coordinateur trop gros** : Gère entities + messages + connections
   - ❌ **État global complexe** : Multiples sources de vérité
   - ❌ **Difficile à maintenir** : Logique métier entrelacée avec l'UI

3. **AzureServiceBusClient.cs**
   - ❌ **Client monolithe** : Gère queues, topics, subscriptions
   - ❌ **Callbacks complexes** : Logique de streaming compliquée
   - ❌ **Gestion d'erreurs dispersée** : Chaque méthode gère ses propres erreurs

## ✅ Objectifs Spécifiques

### 1. Décomposer MessageListView.razor

**Cible** : Extraire la logique dans des services et composants enfants.

| Nouveau Composant/Service | Responsabilité | Lignes | Priorité |
|--------------------------|---------------|--------|----------|
| `MessageList.razor` | Affichage pur des messages | 200-250 | 🔴 |
| `MessageFilterService.cs` | Gestion des filtres | 150-200 | 🔴 |
| `MessageSortService.cs` | Tri des messages | 100-150 | 🔴 |
| `MessageSelectionService.cs` | Gestion de la sélection | 100-150 | 🟡 |
| `MessageColumnService.cs` | Gestion des colonnes | 100-150 | 🟢 |

### 2. Décomposer ServiceBusPage.razor

**Cible** : 1 coordinateur + 6-8 sous-composants.

```
Components/ServiceBus/
├── ServiceBusPage.razor            # Coordinateur (~100-150 lignes)
├── ServiceBusSidebar.razor        # Navigation entities
├── EntityTree.razor               # Arbre des entités (existant)
├── MessageWorkspace.razor         # Espace de travail messages
├── ConnectionManager.razor        # Gestion des connections
└── Toolbar.razor                  # Barre d'outils
```

### 3. Décomposer AzureServiceBusClient.cs

**Cible** : 1 client par type d'entité + services de haut niveau.

```
Services/ServiceBus/
├── Queues/
│   ├── IQueueClient.cs
│   └── QueueClient.cs
├── Topics/
│   ├── ITopicClient.cs
│   └── TopicClient.cs
├── Subscriptions/
│   ├── ISubscriptionClient.cs
│   └── SubscriptionClient.cs
├── Messages/
│   ├── IMessageClient.cs
│   └── MessageClient.cs
└── ServiceBusClientAggregator.cs  # Agrégateur pour compatibilité
```

### 4. Améliorer les autres composants

| Composant | Action | Lignes Cible |
|-----------|--------|--------------|
| `ServiceBusGrid.razor` | Extraire en `EntityGrid.razor` générique | < 200 |
| `MessageDetailPane.razor` | Décomposer en 2-3 sous-composants | < 200 |
| `MessageComposer.razor` | Extraire la logique dans un service | < 300 |

## 🏗️ Architecture Cible

```
┌─────────────────────────────────────────────────────────────┐
│                    ServiceBusPage.razor                         │
│                    (Coordinateur)                                  │
└─────────────────────────────────────────────────────────────┘
                              │
              ┌───────────────────┼───────────────────┐
              │                   │                   │
              ▼                   ▼                   ▼
┌─────────────────┐   ┌─────────────────┐   ┌─────────────────┐
│  ServiceBus      │   │ Message         │   │  Connection     │
│  Sidebar         │   │ Workspace       │   │  Manager        │
│  (Navigation)    │   │ (Messages)       │   │  (Connections)   │
└─────────────────┘   └─────────────────┘   └─────────────────┘
              │                   │                   │
              └───────────────────┼───────────────────┘
                              │
    ┌───────────────────────────────▼─────────────────────────────┐
    │                    MessageListView (Ancien)                     │
    │                    ⬇ Décomposé en :                             │
    ├─────────────┬──────────────┬──────────────┬──────────────────┤
    │ MessageList  │ FilterService │ SortService   │ SelectionService  │
    │ (UI)         │ (Business)    │ (Business)    │ (Business)        │
    └─────────────┴──────────────┴──────────────┴──────────────────┘
                              │
              ┌───────────────────▼───────────────────┐
              │         AzureServiceBusClient             │
              │         (Ancien, 652 lignes)              │
              │          ⬇ Décomposé en :                │
              ├─────────────┬──────────────┬────────────┐
              │ QueueClient  │ TopicClient   │ MessageClient│
              │ SubClient    │ (par type)    │ (Commun)     │
              └─────────────┴──────────────┴────────────┘
```

## 📋 Tâches Détaillées

### Phase 1: Préparation
- [ ] Analyser les dépendances actuelles de ServiceBus
- [ ] Identifier les flows de données (entities → messages)
- [ ] Documenter lstate actuel

### Phase 2: Décomposer MessageListView (3-4 jours)
- [ ] Créer `IMessageFilterService.cs` et implémentation
- [ ] Créer `IMessageSortService.cs` et implémentation
- [ ] Créer `IMessageSelectionService.cs` et implémentation
- [ ] Créer `MessageList.razor` (display pure)
- [ ] migrer MessageListView pour utiliser les nouveaux services

### Phase 3: Décomposer ServiceBusPage (2-3 jours)
- [ ] Extraire `ServiceBusSidebar.razor`
- [ ] Extraire `MessageWorkspace.razor`
- [ ] Extraire `ConnectionManager.razor`
- [ ] Réduire ServiceBusPage.razor à la coordination
- [ ] Réintégrer EntityTree avec la nouvelle structure

### Phase 4: Décomposer AzureServiceBusClient (2-3 jours)
- [ ] Créer les interfaces par type d'entité
- [ ] Implémenter QueueClient, TopicClient, SubscriptionClient
- [ ] Implémenter MessageClient pour les opérations communes
- [ ] Créer l'agrégateur pour la compatibilité
- [ ] Mettre à jour MauiProgram.cs

### Phase 5: Améliorer les autres composants (1-2 jours)
- [ ] Réduire ServiceBusGrid.razor
- [ ] Réduire MessageDetailPane.razor
- [ ] Réduire MessageComposer.razor

### Phase 6: Tests (2 jours)
- [ ] Tests unitaires pour tous les nouveaux services
- [ ] Tests de composants pour les nouveaux composants
- [ ] Tests d'intégration
- [ ] Tests de régression

## 🎯 Améliorations de Performances

### 1. Virtualisation des listes de messages
```razor
<Virtualize Items="@_allMessages" Context="msg">
    <MessageRow Message="msg" OnSelected="HandleSelect" />
</Virtualize>
```

### 2. Cache des requêtes fréquentes
```csharp
private readonly MemoryCache _messageCache = new(TimeSpan.FromSeconds(30));

private async Task<List<ServiceBusMessage>> GetMessagesAsync(...)
{
    var cacheKey = $"{EntityPath}-{FilterHash}";
    return await _messageCache.GetOrCreateAsync(cacheKey, 
        async e => await _messageClient.GetMessagesAsync(...));
}
```

### 3. Streaming optimisé
- Utiliser du **buffring intelligent** pour le streaming des messages
- Implémenter un **débouncing** pour les rafraîchissements
- Minimiser les `StateHasChanged()`

### 4. Pagination intelligente
```csharp
// Charger automatiquement quand on approche de la fin
private async Task LoadMoreIfNeededAsync()
{
    if (IsNearBottom && !IsLoading && HasMoreItems)
    {
        await LoadNextPageAsync();
    }
}
```

## 🧪 Stratégie de Tests

Voir [test-plan.md](./test-plan.md) pour les détails.

**Focus** :
- Tests pour le filtrage des messages
- Tests pour le streaming
- Tests pour la gestion des connections
- Tests de régression pour toutes les fonctionnalités existantes

## 📁 Fichiers à Créer/Modifier

### Nouveaux Fichiers
```
src/SwebKit.App/Components/ServiceBus/
├── MessageList.razor
├── ServiceBusSidebar.razor
├── MessageWorkspace.razor
├── ConnectionManager.razor
└── Toolbar.razor

src/SwebKit.Azure/ServiceBus/
├── Queues/
│   ├── IQueueClient.cs
│   └── QueueClient.cs
├── Topics/
│   ├── ITopicClient.cs
│   └── TopicClient.cs
├── Subscriptions/
│   ├── ISubscriptionClient.cs
│   └── SubscriptionClient.cs
├── Messages/
│   ├── IMessageClient.cs
│   ├── MessageClient.cs
│   ├── MessageFilterService.cs
│   ├── MessageSortService.cs
│   └── MessageSelectionService.cs
└── ServiceBusClientAggregator.cs
```

### Fichiers à Modifier
- `src/SwebKit.App/Components/ServiceBus/MessageListView.razor`
- `src/SwebKit.App/Components/ServiceBus/ServiceBusPage.razor`
- `src/SwebKit.App/Components/ServiceBus/ServiceBusGrid.razor`
- `src/SwebKit.App/Components/ServiceBus/MessageDetailPane.razor`
- `src/SwebKit.App/Components/ServiceBus/MessageComposer.razor`
- `src/SwebKit.Azure/ServiceBus/AzureServiceBusClient.cs`
- `src/SwebKit.App/MauiProgram.cs`

## ⚠️ Risques et Atténuation

| Risque | Impact | Atténuation |
|--------|--------|-------------|
| Régression dans le streaming | Élevé | Tests extensifs du streaming |
| Incompatibilité de cache | Moyen | Invalidations claires du cache |
| Problèmes de pagination | Moyen | Tests de la pagination |

## 📊 Métriques de Succès

| Métrique | Avant | Après | Statut |
|----------|-------|-------|--------|
| Lignes max par fichier | 1,816 | < 500 | À faire |
| Nombre de fichiers | ~20 | ~30+ | À faire |
| Couverture de tests | ~50% | > 80% | À faire |
| Temps de chargement | TBR | TBR | À faire |

---

## 📚 Documentation Connexe
- [Architecture globale](../../../architecture/architecture.md)
- [Service Bus Functionalities](../../../architecture/functionalities/service-bus.md)
- [AzureServiceBusClient actuel](file:///D:/Projects/SwebKit/src/SwebKit.Azure/ServiceBus/AzureServiceBusClient.cs)

---

*Créé le: {{date}}*
*Statut: En planification*
*Priorité: 🔴 CRITIQUE*

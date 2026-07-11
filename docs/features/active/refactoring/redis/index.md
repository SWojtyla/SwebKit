# Refactoring Feature: Redis

## 🎯 Objectif Global

**Améliorer la maintenabilité et les performances** de la feature **Redis** en décomposant `RedisPage.razor` (1,451 lignes) et `RedisClient.cs` (630 lignes).

## 📊 État Actuel

### Fichiers Critiques

| Fichier | Lignes | Taille | Complexité | Priorité |
|--------|--------|--------|------------|----------|
| `RedisPage.razor` | **1,451** | 50.4 KB | ⭐⭐⭐⭐ | 🟡 ÉLEVÉE |
| `RedisClient.cs` | **630** | 24.1 KB | ⭐⭐⭐ | 🟡 ÉLEVÉE |
| `DemoRedisClient.cs` | **685** | 25.6 KB | ⭐⭐⭐ | 🟡 ÉLEVÉE |
| `RedisConnectionBar.razor` | **445** | 19.2 KB | ⭐⭐⭐ | 🟡 ÉLEVÉE |
| `RedisServerInfo.razor` | **500** | 21.8 KB | ⭐⭐⭐ | 🟡 ÉLEVÉE |
| `RedisKeyDetail.razor` | **500** | 22.3 KB | ⭐⭐⭐ | 🟡 ÉLEVÉE |

### Problèmes Identifiés

1. **RedisPage.razor** (1,451 lignes)
   - ❌ **Trop de responsabilités** : Keys + Server Info + Connections + Toolbar
   - ❌ **État complexe** : Multiples states à gérer
   - ❌ **Code dupliqué** : Logique similaire pour différents types de keys

2. **RedisClient.cs** (630 lignes)
   - ❌ **Client monolithe** : Toutes les opérations Redis dans un fichier
   - ❌ **Complexité élevée** : Gestion des différentes types de données
   - ❌ **Testabilité limitée**

3. **DemoRedisClient.cs** (685 lignes)
   - ❌ **Duplication avec RedisClient** : Logique similaire
   - ❌ **Fake data generation complexe**

4. **RedisConnectionBar.razor** (445 lignes)
   - ❌ **Trop de logique de connexion**
   - ❌ **État complexe à gérer**

## ✅ Objectifs Spécifiques

### 1. Décomposer RedisPage.razor

**Cible** : 1 page + 6-8 sous-composants + 3-4 services.

```
Components/Pages/
└── RedisPage.razor                # Coordinateur (~100-150 lignes)

Components/Redis/
├── RedisToolbar.razor             # Barre d'outils Redis
├── RedisServerInfoPanel.razor    # Panel d'info serveur
├── RedisNamespaceTree.razor      # Arbre des namespaces/keys
├── RedisKeyList.razor             # Liste des keys
├── RedisKeyDetailPanel.razor      # Détails d'une key
└── RedisConnectionManager.razor # Gestion des connections
```

### 2. Décomposer RedisClient.cs

**Cible** : 5-6 services spécialisés.

```
Services/Redis/
├── IRedisConnectionService.cs     # Gestion des connections
├── IRedisServerService.cs          # Opérations serveur
├── IRedisKeyService.cs             # Opérations sur les keys
├── IRedisStringService.cs          # Opérations Strings
├── IRedisHashService.cs            # Opérations Hashes
├── IRedisListService.cs            # Opérations Lists
├── IRedisSetService.cs             # Opérations Sets
├── IRedisSortedSetService.cs       # Opérations Sorted Sets
└── RedisServiceAggregator.cs       # Agrégateur pour compatibilité
```

### 3. Améliorer DemoRedisClient

**Cible** : Générateur modulaire de fake data.

```
Services/Redis/Demo/
├── DemoRedisConnectionService.cs
├── DemoRedisServerService.cs
├── DemoRedisKeyService.cs
├── Factories/
│   ├── DemoStringFactory.cs
│   ├── DemoHashFactory.cs
│   ├── DemoListFactory.cs
│   ├── DemoSetFactory.cs
│   └── DemoSortedSetFactory.cs
└── DemoRedisServiceAggregator.cs
```

### 4. Décomposer les autres composants

- **RedisConnectionBar.razor** → Extraire dans `RedisConnectionManager.razor`
- **RedisServerInfo.razor** → Décomposer en 2-3 sous-composants
- **RedisKeyDetail.razor** → Spécialiser par type de key

## 🏗️ Architecture Cible

```
┌─────────────────────────────────────────────────────────────┐
│                    RedisPage.razor                              │
│                    (Coordinateur)                                │
└─────────────────────────────────────────────────────────────┘
                              │
      ┌───────────────────────┬───────────────────────┐
      │                       │                       │
      ▼                       ▼                       ▼
┌─────────────────┐   ┌─────────────────┐   ┌─────────────────┐
│ RedisToolbar     │   │ Connection       │   │ RedisNamespace   │
│ (Actions)        │   │ Manager         │   │ Tree             │
└─────────────────┘   └─────────────────┘   └─────────────────┘
              │                   │                       │
              └───────────────────┼───────────────────────┘
                              │
              ▼
┌─────────────────────────────────────────────────────────────┐
│                    RedisClient (Ancien)                           │
│                    ⬇ Décomposé en :                             │
├─────────────┬──────────────┬──────────────┬──────────────────┤
│ Connection   │ Server        │ Key           │ Data Type         │
│ Service     │ Service       │ Service       │ Services          │
└─────────────┴──────────────┴──────────────┴──────────────────┘
                              │
         ┌────────────────────────┼────────────────────────┐
         │                        │                        │
         ▼                        ▼                        ▼
┌─────────────────┐       ┌─────────────────┐       ┌─────────────────┐
│ DemoRedisClient  │       │ RedisPage UI     │       │ Redis Key       │
│ ⬇ Décomposé en:  │       │ Components      │       │ Detail           │
│ Demo Services    │       │ (6-8 composants) │       │ Components      │
└─────────────────┘       └─────────────────┘       └─────────────────┘
```

## 📋 Tâches Détaillées

### Phase 1: Préparation (1/2 jour)
- [ ] Analyser RedisPage.razor et RedisClient.cs
- [ ] Identifier les types de données Redis gérés
- [ ] Documenter le flow de connexion
- [ ] Analyser les performances actuelles

### Phase 2: Décomposer RedisClient (2-3 jours)
- [ ] Créer les interfaces par type d'opération
- [ ] Implémenter les services spécialisés
- [ ] Créer l'agrégateur pour compatibilité
- [ ] Mettre à jour MauiProgram.cs
- [ ] Tester chaque service

### Phase 3: Décomposer DemoRedisClient (1 jour)
- [ ] Créer les services demo par type
- [ ] Créer les factories de données
- [ ] Créer l'agrégateur demo
- [ ] Tester le demo mode

### Phase 4: Décomposer RedisPage (1-2 jours)
- [ ] Créer les sous-composants
- [ ] Extraire la logique de RedisPage
- [ ] Réduire RedisPage.razor à 100-150 lignes
- [ ] Intégrer tous les composants
- [ ] Tester l'intégration

### Phase 5: Améliorer les autres composants (1 jour)
- [ ] Optimiser RedisConnectionBar
- [ ] Décomposer RedisServerInfo
- [ ] Spécialiser RedisKeyDetail
- [ ] Tester tous les composants

### Phase 6: Tests (1-2 jours)
- [ ] Tests unitaires pour les services
- [ ] Tests de composants
- [ ] Tests d'intégration
- [ ] Tests de régression

## 🧪 Stratégie de Tests

- Tests pour chaque type de service Redis
- Tests pour la gestion des connections
- Tests pour le demo mode
- Tests de composants bUnit
- Tests de régression pour toutes les fonctionnalités

### Couverture Cible
- Services : **> 90%**
- Composants : **> 85%**

## 📁 Fichiers à Créer/Modifier

### Nouveaux Fichiers
```
src/SwebKit.Redis/
├── Services/
│   ├── Connections/
│   │   ├── IRedisConnectionService.cs
│   │   └── RedisConnectionService.cs
│   ├── Server/
│   │   ├── IRedisServerService.cs
│   │   └── RedisServerService.cs
│   ├── Keys/
│   │   ├── IRedisKeyService.cs
│   │   └── RedisKeyService.cs
│   ├── DataTypes/
│   │   ├── IRedisStringService.cs
│   │   ├── IRedisHashService.cs
│   │   ├── IRedisListService.cs
│   │   ├── IRedisSetService.cs
│   │   └── IRedisSortedSetService.cs
│   └── RedisServiceAggregator.cs
└── Demo/
    ├── DemoRedisConnectionService.cs
    ├── DemoRedisServerService.cs
    ├── DemoRedisKeyService.cs
    ├── Factories/
    │   ├── DemoStringFactory.cs
    │   ├── DemoHashFactory.cs
    │   ├── DemoListFactory.cs
    │   ├── DemoSetFactory.cs
    │   └── DemoSortedSetFactory.cs
    └── DemoRedisServiceAggregator.cs

src/SwebKit.App/Components/Redis/
├── RedisPage.razor
├── RedisToolbar.razor
├── RedisConnectionManager.razor
├── RedisServerInfoPanel.razor
├── RedisNamespaceTree.razor
├── RedisKeyList.razor
├── RedisKeyDetailPanel.razor
└── KeyDetails/
    ├── StringKeyDetail.razor
    ├── HashKeyDetail.razor
    ├── ListKeyDetail.razor
    ├── SetKeyDetail.razor
    └── SortedSetKeyDetail.razor
```

### Fichiers à Modifier
- `src/SwebKit.App/MauiProgram.cs`
- `src/SwebKit.App/Components/Layout/MainLayout.razor`
- Tous les fichiers dépendants

### Fichiers à Supprimer (après migration)
- `src/SwebKit.Redis/RedisClient.cs` (remplacé par les services)
- `src/SwebKit.App/Components/Pages/RedisPage.razor` (remplacé)

## ⚠️ Risques et Atténuation

| Risque | Impact | Atténuation |
|--------|--------|-------------|
| Problèmes de connexion | Élevé | Tests extensifs de connexion |
| Incompatibilité de données | Moyen | Validation des données |
| Rupture du demo mode | Moyen | Tests spécifique demo mode |

## 📊 Métriques de Succès

| Métrique | Avant | Après | Statut |
|----------|-------|-------|--------|
| Lignes RedisPage | 1,451 | < 150 | À faire |
| Lignes RedisClient | 630 | Distribué | À faire |
| Nombre de services | 1 | 8-10 | À faire |
| Couverture de tests | ~40% | > 85% | À faire |

---

## 📚 Documentation Connexe
- [Redis Functionalities](../../../architecture/functionalities/redis.md)
- [Architecture globale](../../../architecture/architecture.md)

---

*Créé le: {{date}}*
*Statut: En planification*
*Priorité: 🟡 ÉLEVÉE*

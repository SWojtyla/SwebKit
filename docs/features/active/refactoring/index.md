# Refactoring Global - SwebKit

## 📋 Contexte Global

Ce dossier contient les plans de refactoring organisés par **feature** pour améliorer :
- **Lisibilité** du code
- **Maintenabilité** à long terme  
- **Testabilité** avec des composants isolables
- **Performances** là où c'est pertinent

## 🎯 Objectifs Principaux

### Problèmes Identifiés

L'analyse du codebase a révélé les problèmes suivants :

| Métrique | État Actuel | Cible |
|----------|-------------|-------|
| Fichiers > 1000 lignes | **8 fichiers** | 0 |
| Fichiers 500-1000 lignes | **25+ fichiers** | < 10 |
| Complexité cyclomatique | Non mesurée | < 15 par méthode |
| Couverture de tests | variable | > 80% |

### Top 10 Fichiers Critiques (par taille)

1. **KubernetesAksClient.cs** - 4,445 lignes ⭐ **PRIORITÉ MAXIMALE**
2. **DashboardPage.razor** - 2,960 lignes ⭐ **PRIORITÉ MAXIMALE**
3. **AksPage.razor** - 2,939 lignes ⭐ **PRIORITÉ MAXIMALE**
4. **DemoAksClient.cs** - 2,300 lignes ⭐ **PRIORITÉ MAXIMALE**
5. **ApiClientPage.razor** - 1,975 lignes ⭐ **PRIORITÉ MAXIMALE**
6. **MessageListView.razor** - 1,816 lignes ⭐ **PRIORITÉ MAXIMALE**
7. **RedisPage.razor** - 1,451 lignes
8. **ObservabilityLogs.razor** - 1,256 lignes
9. **LinkedCollectionFileService.cs** - 1,118 lignes
10. **AksDetailPanels.razor** - 1,087 lignes

## 🗂️ Structure des Plans (Mise à jour)

```
docs/features/active/refactoring/
├── index.md                    # Vue d'ensemble GLOBALE (ce fichier)
├── test-plan.md               # Stratégie de TEST GLOBALE
├── aks/                       # Feature AKS/Kubernetes ✅ COMPLET
│   ├── index.md               # Plan détaillé (427 lignes)
│   ├── status.md              # Suivi de progression
│   ├── test-plan.md           # Stratégie de tests
│   └── decisions.md           # Décisions techniques ⭐ NOUVEAU
├── service-bus/               # Feature Service Bus ✅ COMPLET
│   ├── index.md               # Plan détaillé (~300 lignes)
│   ├── status.md              # Suivi de progression
│   └── test-plan.md           # Stratégie de tests
├── dashboard/                 # Feature Dashboard ✅ COMPLET
│   ├── index.md               # Plan détaillé (~390 lignes) 
│   ├── status.md              # Suivi de progression
│   └── test-plan.md           # Stratégie de tests
├── api-client/                # Feature API Client ✅ COMPLET
│   ├── index.md               # Plan détaillé (~270 lignes)
│   ├── status.md              # Suivi de progression
│   └── test-plan.md           # Stratégie de tests
├── redis/                     # Feature Redis ✅ COMPLET
│   ├── index.md               # Plan détaillé (~290 lignes)
│   ├── status.md              # Suivi de progression
│   └── test-plan.md           # Stratégie de tests
├── observability/             # Feature Observability ✅ COMPLET
│   ├── index.md               # Plan détaillé (~240 lignes)
│   ├── status.md              # Suivi de progression
│   └── test-plan.md           # Stratégie de tests
├── core-services/             # Core Services ✅ COMPLET
│   ├── index.md               # Plan détaillé (~280 lignes)
│   ├── status.md              # Suivi de progression
│   └── test-plan.md           # Stratégie de tests
└── layout/                    # Feature Layout ✅ COMPLET
    ├── index.md               # Plan détaillé (~290 lignes)
    ├── status.md              # Suivi de progression
    └── test-plan.md           # Stratégie de tests
```

### 📁 Fichiers par Feature

| Feature | index.md | status.md | test-plan.md | ecrans | total |
|---------|----------|-----------|--------------|--------|-------|
| AKS | ✅ | ✅ | ✅ | ✅ | **4** |
| Service Bus | ✅ | ✅ | ✅ | ❌ | **3** |
| Dashboard | ✅ | ✅ | ✅ | ❌ | **3** |
| API Client | ✅ | ✅ | ✅ | ❌ | **3** |
| Redis | ✅ | ✅ | ✅ | ❌ | **3** |
| Observability | ✅ | ✅ | ✅ | ❌ | **3** |
| Core Services | ✅ | ✅ | ✅ | ❌ | **3** |
| Layout | ✅ | ✅ | ✅ | ❌ | **3** |

## 📊 Métriques et Progression par Feature

| Feature | Fichiers Critiques | Lignes Totales | Priorité | Statut | Durée Estimée | Responsable |
|---------|-------------------|----------------|----------|--------|---------------|-------------|
| [AKS](./aks/index.md) | 5+ (KubernetesAksClient, AksPage, AksDetailPanels, etc.) | **~8,471** | 🔴 CRITIQUE | ⬜ Planification | 3-4 semaines | À assigner |
| [Service Bus](./service-bus/index.md) | 6+ (MessageListView, ServiceBusPage, etc.) | **~2,742** | 🔴 CRITIQUE | ⬜ Planification | 2-3 semaines | À assigner |
| [Dashboard](./dashboard/index.md) | 1 (DashboardPage) | **2,960** | 🔴 CRITIQUE | ⬜ Planification | 3-4 semaines | À assigner |
| [API Client](./api-client/index.md) | 2+ (ApiClientPage, CollectionTree) | **~2,305** | 🔴 CRITIQUE | ⬜ Planification | 2-3 semaines | À assigner |
| [Redis](./redis/index.md) | 5+ (RedisPage, DemoRedisClient, etc.) | **~2,081** | 🟡 ÉLEVÉE | ⬜ Planification | 2-3 semaines | À assigner |
| [Observability](./observability/index.md) | 4+ (ObservabilityLogs, etc.) | **~1,800** | 🟡 ÉLEVÉE | ⬜ Planification | 2-3 semaines | À assigner |
| [Core Services](./core-services/index.md) | 3+ (DemoAksClient, LinkedCollectionFileService, etc.) | **~1,812+** | 🟡 ÉLEVÉE | ⬜ Planification | 3-4 semaines | À assigner |
| [Layout](./layout/index.md) | 3+ (MainLayout, TopBar, LeftNav) | **~1,170** | 🟡 ÉLEVÉE | ⬜ Planification | 2-3 semaines | À assigner |

### 💰 ROI par Feature
- **AKS** : Réduction de ~8,500 lignes → **Gain maximal** 🏆
- **Dashboard** : 2,960 → 150 lignes = **95% de réduction** 📉
- **API Client** : 3 fichiers de 600-2,000 lignes
- **Service Bus** : 2 fichiers de 1,800+ lignes
- **Redis** : 5 fichiers améliorables

### 📈 Totaux Global
- **Fichiers > 1000 lignes** : 8 → **0** (cible)
- **Fichiers 500-1000 lignes** : 25+ → **< 10** (cible)  
- **Lignes totales à refactorer** : **~24,300+ lignes**
- **Nouveaux services à créer** : **~40-50 services**
- **Nouveaux composants à créer** : **~80-100 composants**
- **Durée totale estimée** : **16-22 semaines** (avec 2-3 semaines pour les tests
- **Couverture de code cible** : **> 85% Global, > 90% Services**

## 🎯 Principes de Refactoring

### 1. **Règle des 500 lignes**
- Aucun fichier source ne doit dépasser **500 lignes**
- Exception : fichiers générés automatiquement (`.g.cs`)
- Pour les fichiers Razor : extraire la logique dans des code-behind `.cs`

### 2. **Séparation des Responsabilités**
- **Un fichier = Une responsabilité** (Single Responsibility Principle)
- Extraction excessive des méthodes en services dédiés
- Utilisation de patterns **Service + Controller + View**

### 3. **Amélioration des Performances**
- Minimiser les `StateHasChanged()` inutiles
- Implémenter `IDisposable` pour le cleanup des ressources
- Utiliser `Lazy<T>` pour l'initialisation différée
- Éviter les recomputations coûteuses dans RenderFragment

### 4. **Testabilité**
- Chaque service doit être testable unitairement
- Utilisation de **interfaces** pour les dépendances externes
- Injection de dépendances (DI) systématique
- Mock facilement les dépendances pour les tests

### 5. **Lisibilité**
- Noms de méthodes/variables **clairs et descriptifs**
- Commentaires XML pour l'API publique
- Éviter les méthodes de plus de **50 lignes**
- Limiter la complexité cyclomatique à **< 15**

## 📈 Roadmap Recommandée

### Phase 1: Urgent (1-2 semaines)
- [ ] **KubernetesAksClient.cs** → Split en 5-6 services spécialisés
- [ ] **DashboardPage.razor** → Extraire des composants enfants
- [ ] **AksPage.razor** → Décomposer en sous-composants
- [ ] **ApiClientPage.razor** → Modulariser avec des WebComponents

### Phase 2: Élevé (2-3 semaines)  
- [ ] **MessageListView.razor** → Extraire la logique de filtrage
- [ ] **DemoAksClient.cs** → Simplifier avec des builders
- [ ] **LinkedCollectionFileService.cs** → Split par domaine

### Phase 3: Moyen (3-4 semaines)
- [ ] Tous les autres fichiers > 500 lignes
- [ ] Amélioration de la couverture de tests
- [ ] Documentation mise à jour

## 💡 Bonnes Pratiques Spécifiques

### Pour les fichiers .razor
```razor
@* ❌ À EVITER - Trop de logique dans le Razor *@
<button @onclick="() => {
    // 50+ lignes de logique ici
    if (condition) { ... }
    else { ... }
    await SomeService.DoSomething();
    StateHasChanged();
}">
    Action
</button>

@* ✅ PRÉFÉRÉ - Logique dans code-behind *@
<button @onclick="HandleActionAsync">Action</button>

@code {
    private async Task HandleActionAsync() => await _handler.HandleActionAsync();
    // Moins de 5 lignes de coordination
}
```

### Pour les gros services C#
```csharp
// ❌ À EVITER - Classe monolithe
public class GiantService
{
    public void DoEverything() { /* 4000+ lignes */ }
}

// ✅ PRÉFÉRÉ - Split par responsabilité
public interface IPodService { /* 200-300 lignes max */ }
public interface IDeploymentService { /* 200-300 lignes max */ }
public interface IYamlService { /* 100-200 lignes max */ }
```

## 🔗 Liens Utiles

- [Architecture globale](../../architecture/architecture.md)
- [Design des composants](../../architecture/design.md)
- [Guide du codebase](../../architecture/codebase-guide.md)
- [Documentation swebiplan](https://hermes-agent.nousresearch.com/docs/skills/swebiplan)

## 🎯 Décisions Clés et Patterns Communs

### Patterns à Appliquer Partout

1. **Subdivision par Domaine**
   - Frontend → Composants modulaires
   - Backend → Services spécialisés
   - Shared → Extraction de la logique commune

2. **Injection de Dépendances**
   - Toujours utiliser des interfaces
   - code behind pour \@inject
   - Facilite le mocking

3. **Gestion des Erreurs Centralisée**
   - Exception custom par domaine
   - Error handler central
   - Messages utilisateur clairs

4. **Lazy Loading et Virtualisation**
   - Virtualize pour les grands listes
   - Lazy<T> pour les services lourds
   - Cache intelligent avec invalidation

5. **Séparation UI/Business Logic**
   - Services pour la logique métier
   - ViewModels pour les données
   - Composants pour l'affichage

## 📊 Résumé Complet

### 🟢 Assets Créés

| Type | Nombre | Localisation |
|------|---------|-------------|
| Dossiers de feature | 8 | `docs/features/active/refactoring/` |
| Fichiers index.md | 8 | 1 par feature + 1 global |
| Fichiers status.md | 8 | 1 par feature |
| Fichiers test-plan.md | 9 | 1 par feature + 1 global |
| Fichiers decisions.md | 1 | AKS uniquement (optionnel pour les autres) |
| **TOTAL** | **34 fichiers** | **~200 KB de documentation** |

### 📈 Impact Attendu

| Métrique | Avant | Cible | Amélioration |
|----------|-------|-------|-------------|
| Fichiers > 1000 lignes | 8+ | 0 | ⭐⭐⭐⭐⭐ |
| Fichiers 500-1000 lignes | 25+ | < 10 | ⭐⭐⭐⭐⭐ |
| Couverture de code | ~30-50% | > 85% | ⭐⭐⭐⭐ |
| Complexité moyenne | Non mesurée | < 15/méthode | ⭐⭐⭐⭐ |
| Maintenabilité | Faible | Elevée | ⭐⭐⭐⭐⭐ |
----

*Créé le: 2026-07-11*
*Dernière mise à jour: 2026-07-11*
*Statut global: **✅ COMPLET - Tous les plans créés**
*Responsable: À assigner par feature
*Priorité globale: 🔴 CRITIQUE

## ✅ Prochaines Étapes

1. **👥 Assigner les responsables** à chaque feature
2. **📅 Prioriser les features** pour l'implémentation
3. **📝 Détailler les sous-tâches** spécifiques pour AKS (déjà bien avancé)
4. **✅ Valider les plans** avec l'équipe
5. **🎯 Commencer par AKS** (ROI maximal)
6. **📊 Mettre en place le tracking** GitHub Projects ou équivalent

## 🔥 Recommandation

**Commencer par AKS** car:
- Réduction maximale de lignes (8,400+ → ~500)
- ROI le plus élevé
- Plan le plus détaillé avec décisions techniques complètes
- Architectures bien maturas avec données claires

---

_[Documentation générée via swebiplan + Hermès Agent]_

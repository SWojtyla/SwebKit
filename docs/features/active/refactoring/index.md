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

## 🗂️ Structure des Plans

```
docs/features/active/refactoring/
├── index.md                    # Ce fichier - Vue d'ensemble globale
├── aks/                       # Feature AKS/Kubernetes
│   ├── index.md               # Plan principal
│   ├── status.md              # Statut et progression
│   ├── test-plan.md           # Stratégie de tests
│   ├── backend.md             # Refactoring backend (KubernetesAksClient)
│   └── frontend.md            # Refactoring frontend (AksPage, AksDetailPanels)
├── service-bus/               # Feature Service Bus
│   ├── index.md
│   ├── status.md
│   ├── test-plan.md
│   └── frontend.md
├── dashboard/                 # Feature Dashboard
│   ├── index.md
│   ├── status.md
│   └── test-plan.md
├── api-client/                # Feature API Client
│   ├── index.md
│   ├── status.md
│   └── test-plan.md
├── redis/                     # Feature Redis
│   ├── index.md
│   ├── status.md
│   └── test-plan.md
├── observability/             # Feature Observability
│   ├── index.md
│   ├── status.md
│   └── test-plan.md
├── core-services/             # Feature Services Core
│   ├── index.md
│   ├── status.md
│   └── test-plan.md
└── layout/                    # Feature Layout & Navigation
    ├── index.md
    ├── status.md
    └── test-plan.md
```

## 📊 Métriques et Fortes de Progression

| Feature | Fichiers Critiques | Lignes Totales | Priorité | Statut |
|---------|-------------------|----------------|----------|--------|
| [AKS](./aks/index.md) | 3+ | 8,471 | 🔴 CRITIQUE | À faire |
| [Service Bus](./service-bus/index.md) | 2+ | 2,742 | 🔴 CRITIQUE | À faire |
| [Dashboard](./dashboard/index.md) | 1 | 2,960 | 🔴 CRITIQUE | À faire |
| [API Client](./api-client/index.md) | 2+ | 2,305 | 🔴 CRITIQUE | À faire |
| [Redis](./redis/index.md) | 2+ | 2,081 | 🟡 ÉLEVÉE | À faire |
| [Observability](./observability/index.md) | 2+ | 1,800 | 🟡 ÉLEVÉE | À faire |
| [Core Services](./core-services/index.md) | 2+ | 1,812 | 🟡 ÉLEVÉE | À faire |
| [Layout](./layout/index.md) | 2+ | 1,170 | 🟡 ÉLEVÉE | À faire |

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
- [Functionalities AKS](../../architecture/functionalities/aks.md)
- [Functionalities Service Bus](../../architecture/functionalities/service-bus.md)

## 📝 Notes

- Tous les plans doivent suivre le template [swebiplan](https://hermes-agent.nousresearch.com/docs/skills/swebiplan)
- Chaque feature doit avoir son propre dossier avec `index.md`, `status.md`, `test-plan.md`
- Les changements doivent être validés par : `dotnet build` → `dotnet test` → vérification manuelle
- Ne Jamais committer sans tests qui passent

---

*Créé le: {{date}}*
*Statut global: En planification*
*Priorité: 🔴 CRITIQUE*

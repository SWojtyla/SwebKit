# Test Plan - Refactoring Feature: AKS

## 🎯 Objectif des Tests

Garantir que le refactoring de la feature **AKS** ne introduit **aucune regression fonctionnelle** tout en **améliorant la couverture de tests** de 50% à > 80%.

## 📋 Stratégie Globale

### Types de Tests

| Type | Cible | Framework | % Cible |
|------|-------|-----------|----------|
| **Unit Tests** | Logique métier, Services | xUnit | > 90% |
| **Component Tests** | Composants Razor | bUnit | > 80% |
| **Integration Tests** | Interaction entre services | xUnit | > 70% |
| **Performance Tests** | Vérification temps de réponse | BenchmarkDotNet | ✅ |

### Méthodologie
1. **Tests existants** : Maintenir et adapter tous les tests existants
2. **Nouveaux tests** : Ajouter des tests pour chaque nouveau service et composant
3. **Validation manuelle** : Tester chaque fonctionnalité critique manuellement
4. **Regression tests** : Créer des tests de non-régression pour les bugs connus

---

## 🏗️ Environnement de Test

### Pré-requis
```bash
# Dépendances nécessaires
dotnet add package xunit
dotnet add package Moq
dotnet add package FluentAssertions
dotnet add package bunit
dotnet add package Microsoft.NET.Test.Sdk
```

### Configuration
```json
// Dans tests/SwebKit.Kubernetes.Tests/SwebKit.Kubernetes.Tests.csproj
<ItemGroup>
    <PackageReference Include="xunit" Version="2.4.*" />
    <PackageReference Include="Moq" Version="4.20.*" />
    <PackageReference Include="FluentAssertions" Version="6.15.*" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.*" />
</ItemGroup>
```

---

## 📝 Tests Unitaires - Backend Services

### 1. PodService Tests

**Fichier** : `tests/SwebKit.Kubernetes.Tests/Services/PodServiceTests.cs`

#### Scénarios à tester

```csharp
// Exemple de structure de test
public class PodServiceTests
{
    private readonly Mock<IKubernetesClient> _k8sMock = new();
    private readonly Mock<ILogger<PodService>> _loggerMock = new();
    private readonly PodService _service;
    
    public PodServiceTests()
    {
        _service = new PodService(_k8sMock.Object, _loggerMock.Object);
    }
    
    [Fact]
    public async Task GetPodsAsync_ShouldReturnFilteredPods()
    {
        // Arrange
        var namespace = "default";
        var mockPods = new List<V1Pod> { /* ... */ };
        _k8sMock.Setup(x => x.ListNamespacedPodAsync(namespace, null, null, null, null))
               .ReturnsAsync(new V1PodList(mockPods));
        
        // Act
        var result = await _service.GetPodsAsync(namespace);
        
        // Assert
        result.Should().HaveCount(2);
        result.Should().AllSatisfy(p => p.Namespace == namespace);
    }
    
    [Fact]
    public async Task GetPodLogsAsync_ShouldStreamCorrectly()
    {
        // Arrange
        var podName = "my-pod";
        var namespace = "default";
        var expectedLogs = new[] { "log line 1", "log line 2" };
        
        // Act
        var result = await _service.GetPodLogsAsync(namespace, podName, "container1");
        
        // Assert
        result.Should().Contain(expectedLogs);
    }
    
    [Fact]
    public async Task ExecutePodCommandAsync_ShouldHandleErrorGracefully()
    {
        // Arrange
        _k8sMock.Setup(x => x.ReadNamespacedPodExecAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()))
               .ThrowsAsync(new KubernetesException("Error"));
        
        // Act & Assert
        var exception = await Assert.ThrowsAsync<KubernetesException>(
            () => _service.ExecutePodCommandAsync("default", "my-pod", "container1", "sh"));
        
        exception.Message.Should().Contain("Error");
    }
}
```

#### Cas de test spécifiques

| ID | Description | Priorité | Statut |
|----|-------------|----------|--------|
| POD-001 | Obtenir la liste des pods dans un namespace | 🔴 | À faire |
| POD-002 | Obtenir les logs d'un pod | 🔴 | À faire |
| POD-003 | Executer une commande dans un pod | 🔴 | À faire |
| POD-004 | Supprimer un pod | 🟡 | À faire |
| POD-005 | Filtrer les pods par statut | 🟡 | À faire |
| POD-006 | Gérer les pods avec plusieurs containers | 🟡 | À faire |
| POD-007 | Gérer les erreurs Kubernetes | 🟡 | À faire |
| POD-008 | Timeout sur les opérations longues | 🟢 | À faire |

### 2. DeploymentService Tests

**Fichier** : `tests/SwebKit.Kubernetes.Tests/Services/DeploymentServiceTests.cs`

| ID | Description | Priorité | Statut |
|----|-------------|----------|--------|
| DEP-001 | Obtenir la liste des deployments | 🔴 | À faire |
| DEP-002 | Obtenir les détails d'un deployment | 🔴 | À faire |
| DEP-003 | Redémarrer un deployment | 🔴 | À faire |
| DEP-004 | Scaler un deployment | 🔴 | À faire |
| DEP-005 | Obtenir le YAML d'un deployment | 🔴 | À faire |
| DEP-006 | Appliquer un YAML.modifié | 🟡 | À faire |

### 3. ServiceService Tests

**Fichier** : `tests/SwebKit.Kubernetes.Tests/Services/ServiceServiceTests.cs`

| ID | Description | Priorité | Statut |
|----|-------------|----------|--------|
| SVC-001 | Obtenir la liste des services | 🔴 | À faire |
| SVC-002 | Obtenir les détails d'un service | 🔴 | À faire |
| SVC-003 | Exporter les endpoints d'un service | 🟡 | À faire |

### 4. IngressService Tests

**Fichier** : `tests/SwebKit.Kubernetes.Tests/Services/IngressServiceTests.cs`

| ID | Description | Priorité | Statut |
|----|-------------|----------|--------|
| ING-001 | Obtenir la liste des ingress | 🔴 | À faire |
| ING-002 | Obtenir les règles d'une ingress | 🔴 | À faire |

### 5. HelmService Tests

**Fichier** : `tests/SwebKit.Kubernetes.Tests/Services/HelmServiceTests.cs`

| ID | Description | Priorité | Statut |
|----|-------------|----------|--------|
| HLM-001 | Obtenir la liste des releases Helm | 🔴 | À faire |
| HLM-002 | Obtenir l'historique d'un release | 🔴 | À faire |
| HLM-003 | Exécuter un rollback | 🟡 | À faire |
### 6. ResourceService Tests

**Fichier** : `tests/SwebKit.Kubernetes.Tests/Services/ResourceServiceTests.cs`

| ID | Description | Priorité | Statut |
|----|-------------|----------|--------|
| RES-001 | Obtenir les événements Kubernetes | 🔴 | À faire |
| RES-002 | Formater le YAML Kubernetes | 🔴 | À faire |
| RES-003 | Appliquer les modifications YAML | 🟡 | À faire |

### 7. KubernetesContextService Tests

**Fichier** : `tests/SwebKit.Kubernetes.Tests/Services/KubernetesContextServiceTests.cs`

| ID | Description | Priorité | Statut |
|----|-------------|----------|--------|
| CTX-001 | Charger le kubeconfig | 🔴 | À faire |
| CTX-002 | Changer de contexte | 🔴 | À faire |
| CTX-003 | Changer de namespace | 🔴 | À faire |
| CTX-004 | Obtenir le contexte actuel | 🔴 | À faire |
| CTX-005 | Valider le kubeconfig | 🟡 | À faire |

### 8. AksClientAggregator Tests

**Fichier** : `tests/SwebKit.Kubernetes.Tests/AksClientAggregatorTests.cs`

| ID | Description | Priorité | Statut |
|----|-------------|----------|--------|
| AGG-001 | Délégation vers PodService | 🔴 | À faire |
| AGG-002 | Délégation vers DeploymentService | 🔴 | À faire |
| AGG-003 | Gestion des erreurs | 🔴 | À faire |
| AGG-004 | compatibilité API | 🔴 | À faire |

---

## 🎨 Tests de Composants - Frontend

### 1. AksPage Tests

**Fichier** : `tests/SwebKit.App.Tests/Components/AksPageTests.cs`

```csharp
public class AksPageTests : TestContext
{
    [Fact]
    public void AksPage_ShouldRenderWithoutError()
    {
        // Arrange
        var aksClientMock = new Mock<IAksClient>();
        var appStateMock = new Mock<AppStateService>();
        
        // Act
        var cut = RenderComponent<AksPage>(
            parameters => parameters
                .Add(p => p.AksClient, aksClientMock.Object)
                .Add(p => p.AppState, appStateMock.Object));
        
        // Assert
        cut.Should().NotBeNull();
        cut.Find(".aks-page").Should().NotBeNull();
    }
    
    [Fact]
    public async Task AksPage_ShouldLoadResourcesOnInitialized()
    {
        // Arrange
        var aksClientMock = new Mock<IAksClient>();
        aksClientMock.Setup(x => x.GetDeploymentsAsync("default"))
                     .ReturnsAsync(new List<DeploymentModel> { new() { Name = "test" } });
        
        // Act
        var cut = RenderComponent<AksPage>(...);
        
        // Assert
        await Task.Delay(100); // Allow async init
        aksClientMock.Verify(x => x.GetDeploymentsAsync("default"), Times.Once);
    }
}
```

| ID | Description | Priorité | Statut |
|----|-------------|----------|--------|
| AKS-001 | Rendu sans erreur | 🔴 | À faire |
| AKS-002 | Chargement des ressources à l'initialisation | 🔴 | À faire |
| AKS-003 | Gestion du changement de namespace | 🔴 | À faire |
| AKS-004 | Gestion du changement de contexte | 🟡 | À faire |
| AKS-005 | Filtrage des ressources | 🟡 | À faire |
| AKS-006 | Sélection des ressources | 🟡 | À faire |

### 2. ResourceGrid Tests

**Fichier** : `tests/SwebKit.App.Tests/Components/Aks/ResourceGridTests.cs`

| ID | Description | Priorité | Statut |
|----|-------------|----------|--------|
| GRD-001 | Rendu avec des données | 🔴 | À faire |
| GRD-002 | Tri des colonnes | 🔴 | À faire |
| GRD-003 | Filtrage des lignes | 🟡 | À faire |
| GRD-004 | Sélection multiple | 🟡 | À faire |
| GRD-005 | Navigation clavier | 🟢 | À faire |

### 3. AksToolbar Tests

**Fichier** : `tests/SwebKit.App.Tests/Components/Aks/AksToolbarTests.cs`

| ID | Description | Priorité | Statut |
|----|-------------|----------|--------|
| TLB-001 | Rendu des boutons d'action | 🔴 | À faire |
| TLB-002 | Gestion des raccourcis clavier | 🔴 | À faire |
| TLB-003 | Tooltips contextualisés | 🟡 | À faire |

### 4. AksSidePanel Tests

**Fichier** : `tests/SwebKit.App.Tests/Components/Aks/AksSidePanelTests.cs`

| ID | Description | Priorité | Statut |
|----|-------------|----------|--------|
| PAN-001 | Ouverture/fermeture | 🔴 | À faire |
| PAN-002 | Basculement entre panels | 🔴 | À faire |
| PAN-003 | Persistance de l'état | 🟡 | À faire |

### 5. Panels de Détails Tests

**Fichier** : `tests/SwebKit.App.Tests/Components/Aks/Panels/`

| Panel | Tests | Priorité | Statut |
|-------|-------|----------|--------|
| PodDetailPanel | 5 | 🔴 | À faire |
| DeploymentDetailPanel | 5 | 🔴 | À faire |
| ServiceDetailPanel | 3 | 🟡 | À faire |
| IngressDetailPanel | 3 | 🟡 | À faire |
| HelmDetailPanel | 3 | 🟡 | À faire |

---

## 🔄 Tests d'Intégration

### 1. Intégration Backend-Frontend

**Fichier** : `tests/SwebKit.Kubernetes.Tests/Integration/AksIntegrationTests.cs`

| ID | Description | Priorité | Statut |
|----|-------------|----------|--------|
| INT-001 | Flux complet : Chargement des pods → Display | 🔴 | À faire |
| INT-002 | Flux complet : Sélection deployment → Affichage détails | 🔴 | À faire |
| INT-003 | Gestion des erreurs K8s → UI | 🔴 | À faire |

### 2. Intégration entre Services

**Fichier** : `tests/SwebKit.Kubernetes.Tests/Integration/ServicesIntegrationTests.cs`

| ID | Description | Priorité | Statut |
|----|-------------|----------|--------|
| INT-004 | PodService + DeploymentService coordination | 🟡 | À faire |
| INT-005 | ContextService + tous les autres services | 🟡 | À faire |

---

## ⚡ Tests de Performance

### 1. Benchmarks des Services

**Fichier** : `tests/SwebKit.Kubernetes.Tests/Performance/AksPerformanceTests.cs`

```csharp
public class AksPerformanceTests
{
    [Benchmark]
    public async Task PodService_GetPods_Performance()
    {
        var service = new PodService(/* ... */);
        await service.GetPodsAsync("default");
    }
    
    [Benchmark]
    public async Task DeploymentService_GetDeployment_Performance()
    {
        var service = new DeploymentService(/* ... */);
        await service.GetDeploymentAsync("default", "my-app");
    }
}
```

| ID | Description | Cible | Statut |
|----|-------------|-------|--------|
| PERF-001 | PodService.GetPodsAsync < 100ms | < 100ms | À faire |
| PERF-002 | DeploymentService.GetDeploymentAsync < 50ms | < 50ms | À faire |
| PERF-003 | Chargement complet de la page AKS < 500ms | < 500ms | À faire |
| PERF-004 | Streaming des logs < 10ms par ligne | < 10ms | À faire |

### 2. Memory Tests

| ID | Description | Cible | Statut |
|----|-------------|-------|--------|
| MEM-001 | Pas de memory leak avec watching | 0 leak | À faire |
| MEM-002 | Cache ne grossit pas indéfiniment | < 100MB | À faire |
| MEM-003 | Cleanup des ressources | 100% | À faire |

---

## 🔍 Tests de Régression

### 1. Functional Regression Tests

Basé sur les fonctionnalités documentées dans [aks.md](../../../architecture/functionalities/aks.md)

| ID | Fonctionnalité | Statut |
|----|---------------|--------|
| REG-001 | Connexion Kubernetes avec kubeconfig | À faire |
| REG-002 | Changement de contexte | À faire |
| REG-003 | Sélection de namespace | À faire |
| REG-004 | Browse deployments | À faire |
| REG-005 | Browse pods | À faire |
| REG-006 | View pod logs | À faire |
| REG-007 | View pod events | À faire |
| REG-008 | View deployment YAML | À faire |
| REG-009 | Edit deployment YAML | À faire |
| REG-010 | Restart deployment | À faire |
| REG-011 | Scale deployment | À faire |
| REG-012 | Browse Services | À faire |
| REG-013 | Browse Ingresses | À faire |
| REG-014 | Browse Helm releases | À faire |
| REG-015 | Helm rollback | À faire |
| REG-016 | Browse Jobs | À faire |
| REG-017 | Browse CronJobs | À faire |
| REG-018 | View ConfigMaps | À faire |
| REG-019 | View Secrets | À faire |
| REG-020 | Browse Gateway API resources | À faire |
| REG-021 | Pod shell launch | À faire |
| REG-022 | Deployment restart | À faire |
| REG-023 | StatefulSet visibility | À faire |
| REG-024 | HPA inline status | À faire |
| REG-025 | port-forward sessions | À faire |

### 2. UI Regression Tests

| ID | Fonctionnalité UI | Statut |
|----|----------------|--------|
| UI-001 | Toolbar toujours visible | À faire |
| UI-002 | Filtres fonctionnent correctement | À faire |
| UI-003 | Grille responsive | À faire |
| UI-004 | Panels latéraux glissables | À faire |
| UI-005 | Raccourcis clavier fonctionnels | À faire |
| UI-006 | Tooltips contextualisés | À faire |
| UI-007 | Notifications d'erreur | À faire |
| UI-008 | Confirmation des actions destructives | À faire |

---

## 🎯 Matrice de Test

```
┌─────────────────────────────────────────────────────────────────┐
│                   MATRICE DE TEST - REFACTORING AKS                  │
├─────────────────┬──────────┬──────────┬──────────┬───────────────┤
│ Type              │ Unitaire  │ Composant│ Intégr.   │ Régression    │
├─────────────────┼──────────┼──────────┼──────────┼───────────────┤
│ Backend Services │ 50+       │ -        │ 5+        │ -             │
│ Frontend Comp    │ -        │ 30+      │ 3+        │ -             │
│ Performance      │ -        │ -        │ -        │ 4+            │
│ Fonctionnel      │ -        │ -        │ -        │ 25+           │
│ UI               │ -        │ -        │ -        │ 8+            │
├─────────────────┼──────────┼──────────┼──────────┼───────────────┤
│ Total            │ 50+       │ 30+      │ 11+       │ 37+           │
└─────────────────┴──────────┴──────────┴──────────┴───────────────┘
```

| Catégorie | Cible | Actuel | % Complétion |
|-----------|-------|--------|--------------|
| Unit Tests | > 90% | 0% | 0% |
| Component Tests | > 80% | 0% | 0% |
| Integration Tests | > 70% | 0% | 0% |
| Regression Tests | 100% | 0% | 0% |
| **Global** | **> 80%** | **0%** | **0%** |

---

## 📅 Plan d'Exécution des Tests

### Phase 1: Préparation (1 jour)
- [ ] Configurer l'infrastructure de test
- [ ] Créer les mocks de base pour Kubernetes SDK
- [ ] Préparer les fixtures de test

### Phase 2: Tests Backend (3-4 jours)
- [ ] Tester PodService (1 jour)
- [ ] Tester DeploymentService (1/2 jour)
- [ ] Tester ServiceService, IngressService (1/2 jour)
- [ ] Tester HelmService, ResourceService (1/2 jour)
- [ ] Tester KubernetesContextService (1/2 jour)
- [ ] Tester AksClientAggregator (1/2 jour)

### Phase 3: Tests Frontend (2-3 jours)
- [ ] Tester AksPage, ResourceGrid (1 jour)
- [ ] Tester Toolbar, SidePanel (1/2 jour)
- [ ] Tester tous les panels (1-2 jours)

### Phase 4: Tests d'Intégration (1 jour)
- [ ] Tester intégration backend-frontend
- [ ] Tester intégration entre services

### Phase 5: Tests de Régression (2 jours)
- [ ] Créer et exécuter tous les tests de régression
- [ ] Valider manuallement chaque fonctionnalité

---

## 📊 Critères d'Acceptation

### Pour valider le refactoring :

- [ ] Tous les tests unitaires passent (> 90% couverture)
- [ ] Tous les tests de composants passent (> 80% couverture)
- [ ] Tous les tests d'intégration passent (> 70% couverture)
- [ ] Aucun test de régression ne échoue
- [ ] Aucune régression de performance mesurée
- [ ] Aucune régression fonctionnelle identifiée
- [ ] `dotnet build` passe sans erreur
- [ ] `dotnet test` passe sur tous les projets

---

## 🔗 Ressources

- [xUnit Documentation](https://xunit.net/)
- [Moq Documentation](https://github.com/moq/moq4)
- [FluentAssertions Documentation](https://fluentassertions.com/)
- [bUnit Documentation](https://bunit.dev/)
- [Kubernetes Mocking Guide](https://github.com/dotnet-kubernetes-client/KubernetesClient/blob/master/src/KubernetesClient/Util/Mocking.md)

---

*Créé le: {{date}}*
*Dernière mise à jour: {{date}}*
*Statut: En planification*

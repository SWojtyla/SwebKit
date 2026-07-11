# Test Plan - Global Refactoring Strategy

## 📋 Introduction

Ce document décrit la **stratégie de test globale** pour tous les refactorings des features SwebKit. Chaque feature a son propre `test-plan.md` spécifique, mais ce document définit les standards, outils et méthodologies **communes** à tous les refactorings.

## 🎯 Objectifs de Test

### 1. **Assurer la Non-Régression** 🎯
    - Aucune fonctionnalité existante ne doit être cassée
    - Comportement identique avant/après refactoring
    - Tests de régression automatisés

### 2. **Améliorer la Couverture** 📈
    - **Avant** : ~30-50% de couverture moyenne
    - **Après** : **> 85%** pour toutes les features
    - **Services** : > 90% de couverture
    - **Composants** : > 85% de couverture

### 3. **Faciliter la Maintenabilité** 🔧
    - Tests faciles à écrire et à maintenir
    - Pas de tests fragiles
    - Tests isolés et indépendants

---

## 🛠️ Outils et Frameworks

### 1. Tests Unitaires (Services)

| Outil | Version | Usage | Couverture Cible |
|-------|---------|-------|------------------|
| **xUnit** | 2.x | Framework de test | Tous les types |
| **Moq** | 4.x | Mocking | > 90% |
| **AutoFixture** | 4.x | Génération de données | Facilité des tests |
| **FluentAssertions** | 6.x | Assertions lisibles | Tous les tests |

**Exemple de test unitaire** :
```csharp
public class PodServiceTests
{
    private readonly Mock<IKubernetesClient> _mockClient = new();
    private readonly PodService _service;
    
    public PodServiceTests()
    {
        _service = new PodService(_mockClient.Object);
    }
    
    [Fact]
    public async Task GetPodsAsync_WithValidNamespace_ReturnsPods()
    {
        // Arrange
        var ns = "default";
        var expectedPods = new Fixture().CreateMany<V1Pod>().ToList();
        
        _mockClient.Setup(c => c.ListNamespacedPodAsync(ns))
                   .ReturnsAsync(new V1PodList { Items = expectedPods });
        
        // Act
        var result = await _service.GetPodsAsync(ns);
        
        // Assert
        result.Should().HaveCount(expectedPods.Count);
        _mockClient.Verify(c => c.ListNamespacedPodAsync(ns), Times.Once);
    }
    
    [Fact]
    public async Task GetPodsAsync_WithEmptyNamespace_Throws()
    {
        // Arrange
        var emptyNs = string.Empty;
        
        // Act
        Func<Task> action = () => _service.GetPodsAsync(emptyNs);
        
        // Assert
        await action.Should().ThrowAsync<ArgumentNullException>();
    }
}
```

### 2. Tests de Composants (bUnit)

| Outil | Version | Usage |
|-------|---------|-------|
| **bUnit** | 1.x | Framework de test pour Blazor |
| **Microsoft.AspNetCore.Components.Testing** | 8.x | Services de test |
| **AngleSharp** | - | DOM testing |

**Exemple de test de composant** :
```csharp
public class DashboardPageTests : TestContext
{
    [Fact]
    public void DashboardPage_RendersAllTiles()
    {
        // Arrange
        Services.AddMockDashboardService();
        Services.AddMockTileService();
        
        // Act
        var cut = RenderComponent<DashboardPage>();
        
        // Assert - Vérifie que tous les tiles sont rendus
        cut.FindAll(".tile").Should().HaveCountGreaterThan(10);
    }
    
    [Fact]
    public void DashboardPage_NavigateToTile_AddsTileToGrid()
    {
        // Arrange
        Services.AddMockDashboardService();
        var cut = RenderComponent<DashboardPage>();
        var addButton = cut.Find("button.add-tile");
        
        // Act
        addButton.Click();
        var modal = cut.FindComponent<AddTileModal>();
        modal.Find("input[name='tile-type']").Change("aks");
        modal.Find("button.save").Click();
        
        // Assert
        cut.FindAll(".tile.aks").Should().HaveCount(1);
    }
}
```

### 3. Tests d'Intégration

| Outil | Version | Usage |
|-------|---------|-------|
| **Microsoft.AspNetCore.Mvc.Testing** | 8.x | Tests HTTP |
| **Respawn** | - | Réinitialisation de la base de données |
| ** Polly** | - | Gestion des retries et timeouts |

**Exemple de test d'intégration** :
```csharp
public class AksEndpointIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    
    public AksEndpointIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }
    
    [Fact]
    public async Task Get_AksClusters_ReturnsSuccess()
    {
        // Arrange
        var client = _factory.CreateClient();
        
        // Act
        var response = await client.GetAsync("/api/aks/clusters");
        
        // Assert
        response.EnsureSuccessStatusCode();
    }
}
```

### 4. Tests E2E (End-to-End)

| Outil | Version | Usage | Platform |
|-------|---------|-------|----------|
| **Playwright** | 1.x | Automatisation du navigateur | Web + Desktop |
| **xUnit** | 2.x | Runner de tests | Tous |

**Exemple de test E2E** :
```csharp
public class DashboardE2ETests : IAsyncLifetime
{
    private IPage _page;
    private IBrowser _browser;
    
    public async Task InitializeAsync()
    {
        var playwright = await Playwright.CreateAsync();
        _browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });
        
        var context = await _browser.NewContextAsync();
        _page = await context.NewPageAsync();
        
        await _page.GotoAsync("http://localhost:5000/tabs/dashboard");
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }
    
    [Fact]
    public async Task Dashboard_LoadsAndDisplaysTiles()
    {
        // Act
        await _page.WaitForSelectorAsync(".tile");
        
        // Assert
        var tiles = await _page.QuerySelectorAllAsync(".tile");
        tiles.Should().HaveCountGreaterThan(5);
    }
    
    public async Task DisposeAsync()
    {
        await _browser.CloseAsync();
    }
}
```

---

## 📊 Méthodologie de Test

### 1. **Pyramide de Test**

```
                    ┌────────────┐
                    │  E2E Tests  │ ← 5-10% des tests
                    │  (Playwright)│    (Intégration complète)
                    └──────┬──────┘
                           │
              ┌─────────────────┼─────────────────┐
              │ Integration Tests│ ← 15-20% des tests
              │ (API/Service)    │   (Interaction)
              └────────┬────────┘
                       │
        ┌──────────────────────┼──────────────────────┐
        │    Component Tests     │ ← 25-30% des tests
        │    (bUnit)             │   (UI Composants)
        └──────────────┬─────────┘
                        │
          ┌─────────────────────┼─────────────────────┐
          │     Unit Tests       │ ← 50-60% des tests
          │     (xUnit + Moq)    │   (Logique pure)
          └──────────────────────┴─────────────────────┘
```

### 2. **Stratégie de Test par Type de Fichier**

| Type de fichier | Tests applicables | Outils | Exemples |
|----------------|------------------|-------|----------|
| **Services** | Unitaires | xUnit+Moq | Business Logic |
| **Composants Razor** | Composants | bUnit | Rendering, Events |
| **API Controllers** | Intégration, Unitaires | xUnit | Routes, Validation |
| **Pages** | Intégration, E2E | bUnit, Playwright | Navigation, à jour |
| **Utilitaires** | Unitaires | Xunit | Helper methods |

### 3. **Approche AAA+** (Arrange-Act-Assert + Teardown)

Tous les tests doivent suivre ce pattern :

```csharp
[Fact]
public async Task MethodName_StateUnderTest_ExpectedBehavior()
{
    // Arrange - Setup
    var service = new ServiceToTest(...);
    var input = new InputParameters(...);
    
    // Act - Execute
    var result = await service.MethodToTest(input);
    
    // Assert - Verify
    result.Should().BeExpected(...);
    
    // Teardown - Cleanup (si nécessaire)
    await cleanupAction();
}
```

---

## 🎯 Standards de Qualité

### 1. **Noms de Tests**

Utiliser la convention :
```
[Fact]
public void MethodName_State_ExpectedBehavior()
```

**Exemple** :
```csharp
[Fact]
public async Task GetPodsAsync_WithValidNamespace_ReturnsPodList()

[Fact]  
public async Task GetPodsAsync_WithEmptyNamespace_ThrowsArgumentNullException()

[Fact]
public void RenderDashboard_WithTiles_ShowsAllTiles()
```

### 2. **Structure des Tests**

```
┌─ Tests/
│
├─ Unit/
│   ├── Services/
│   │   ├── PodServiceTests.cs
│   │   └── ...
│   └── Utilities/
│
├─ Integration/
│   ├── Controllers/
│   └── Services/
│
├─ Components/
│   ├── Pages/
│   │   └── DashboardPageTests.cs
│   └── Shared/
│       └── SidePanelTests.cs
│
└─ E2E/
    ├── Flows/
    │   ├── DashboardFlowTests.cs
    │   └── ...
    └── Pages/
```

### 3. **Bonnes Pratiques**

- ✅ **TESTS ISOLÉS** : Pas de dépendance entre tests
- ✅ **TESTS RAPIDES** : < 100ms par test unitaire
- ✅ **NOMBRE DE TESTS** : Pas de magic numbers dans les asserts
- ✅ **ARRANGEMENT** : Réduire au minimum
- ✅ **ONE ASSERT** : Un assert principal par test (étant donné que possible)
- ❌ **SLEEP** : Pas de `Thread.Sleep()` ou `Task.Delay()` dans les tests
- ❌ **TEST LOGIC** : Pas de logique complexe dans les tests
- ❌ **ASYNC VOID** : Toujours utiliser `async Task`, jamais `async void`

### 4. **Tests de Régression**

Pour chaque feature refactorée :

1. **Identifier les fonctionnalités critique**
2. **Lister tous les cas d'utilisation**
3. **Écrire des tests pour chaque cas**
4. **Exécuter avant et après le refactoring**
5. **Valider que tout passe**

**Template de checklist de régression** :

```markdown
### Checklist de Régression - Feature: [Nom]

#### Fonctionnalités Critiques
- [ ].xlsx [Description de la fonctionnalité]
- [ ] [Autre fonctionnalité...]
- [ ] [Encore une fonctionnalité...]

#### Cas d'Utilisation
| ID | Description | Étape de test | Résultat attendu | Statut |
|----|-------------|---------------|-----------------|--------|
| 1 | [Cas 1] | [Étapes] | [Résultat] | ⬜ |
| 2 | [Cas 2] | [Étapes] | [Résultat] | ⬜ |

#### Pré-requis
- [ ] Backup du code existant
- [ ] Branch de feature créée
- [ ] Build passe
- [ ] Tests existants passent

#### Post-déploiement
- [ ] Tous les tests-passent
- [ ] Manuel smoke test
- [ ] Validation utilisateur
```

---

## 🔄 CI/CD Intégration

### 1. **Pipeline GitHub Actions**

```yaml
name: CI - Build and Test

on:
  push:
    branches: [ main, develop ]
  pull_request:
    branches: [ main, develop ]

jobs:
  build:
    runs-on: ubuntu-latest
    strategy:
      matrix:
        configuration: [Release]
    
    steps:
    - uses: actions/checkout@v3
    
    - name: Setup .NET
      uses: actions/setup-dotnet@v3
      with:
        dotnet-version: 8.0.x
    
    - name: Restore
      run: dotnet restore
    
    - name: Build
      run: dotnet build --configuration Release --no-restore
    
    - name: Test Unit
      run: dotnet test --filter "TestCategory=Unit" --logger "trx;LogFileName=unit-tests.trx"
    
    - name: Test Integration
      run: dotnet test --filter "TestCategory=Integration" --logger "trx;LogFileName=integration-tests.trx"
    
    - name: Test Components
      run: dotnet test --filter "TestCategory=Component" --logger "trx;LogFileName=component-tests.trx"
    
    - name: Upload Results
      uses: actions/upload-artifact@v3
      if: always()
      with:
        name: Test-Results
        path: **/*.trx
```

### 2. **Coverage Reporting**

```yaml
- name: Install Coverlet
  run: dotnet tool install --global dotnet-coverage

- name: Calculate Coverage
  run: |
    dotnet coverage collect --output coverage --output-format cobertura \
      "dotnet test /p:CollectCoverage=true /p:CoverletOutputFormat=cobertura"
    
- name: Upload Coverage
  uses: actions/upload-artifact@v3
  with:
    name: Coverage-Report
    path: coverage.cobertura.xml
```

### 3. **Quality Gate**

| Métrique | Seuil | Action |
|----------|-------|--------|
| Build | Doit réussir | ❌ Bloquer le merge |
| Tests Unitaires | 100% pass | ❌ Bloquer le merge |
| Tests Intégration | 100% pass | ❌ Bloquer le merge |
| Couverture globale | > 85% | ⚠️ Avertissement |
| Couverture services | > 90% | ⚠️ Avertissement |

---

## 📊 Turning et Reports

### 1. **Exécuter tous les tests facteur**

```bash
# Desde la raíz del proyecto
dotnet test --configuration Release
```

### 2. **Exécuter tests par catégorie**

```bash
# Tests unitaires seulement
dotnet test --filter "TestCategory=Unit"

# Tests de composants seulement
dotnet test --filter "TestCategory=Component"

# Tests d'intégration seulement
dotnet test --filter "TestCategory=Integration"
```

### 3. **Generar reporte de covertura**

```bash
# Avec Coverlet
dotnet test /p:CollectCoverage=true /p:CoverletOutputFormat=cobertura /p:CoverletOutput=coverage.xml

# Avec ReportGenerator (pour HTML)
dotnet tool install -g dotnet-reportgenerator-dotnet
reportgenerator -reports:coverage.xml -targetdir:coverage-report -reporttypes:Html
```

---

## 🚨 Gestion des Erreurs de Test

### 1. **Test Fragile**

Un test qui passe localement mais échoue dans CI/CD :

**Causes courantes** :
- Dependance de l'environnement
- Timing issues (race conditions)
- Ordre d'exécution des tests
- Données partagées entre tests

### 2. **Solutions**

✅ **Utiliser TestCleanup** ou IDisposable :
```csharp
public class MyTests : IDisposable
{
    private TempFile _tempFile;
    
    public MyTests()
    {
        _tempFile = new TempFile();
    }
    
    public void Dispose()
    {
        _tempFile?.Dispose();
    }
}
```

✅ **Réinitialiser l'état dans chaque test** :
```csharp
[Fact]
public void Test1()
{
    var service = CreateFreshService(); // Toujours rencontrer
}
```

✅ **Éviter les dépendances externes** :
```csharp
// ❌ Mauvais - dépend de DateTime.Now
[Fact]
public void Test_TimeSensitive()
{
    var now = DateTime.Now;
    // ...
}

// ✅ Bon - utiliser des valeurs contrôlées
[Fact]
public void Test_WithMockedTime()
{
    var mockedTime = new DateTime(2024, 1, 1);
    // Utiliser un service de time mockable
}
```

---

## 📚 Ressources et Références

### Documentation
- [xUnit Documentation](https://xunit.net/docs)
- [Moq Quickstart](https://github.com/moq/moq4/wiki/Quickstart)
- [bUnit Documentation](https://bunit.dev/docs)
- [FluentAssertions](https://fluentassertions.com/)
- [Playwright for .NET](https://playwright.dev/dotnet/)

### Exemples dans SwebKit
- `tests/SwebKit.Tests/Unit/...`
- `tests/SwebKit.Tests/Components/...`

---

## ✅ Résumé

| Aspect | Standard |
|--------|----------|
| **Framework Unitaires** | xUnit + Moq |
| **Framework Composants** | bUnit |
| **Framework Intégration** | xUnit + WebApplicationFactory |
| **Framework E2E** | Playwright |
| **Couverture min. services** | > 90% |
| **Couverture min. composants** | > 85% |
| **Couverture globale** | > 85% |
| **Pyramide de test** | 50-60% Unit, 25-30% Components, 15-20% Integration, 5-10% E2E |

---

*Créé le: {{date}}*
*Dernière mise à jour: {{date}}*
*Version: 1.0*

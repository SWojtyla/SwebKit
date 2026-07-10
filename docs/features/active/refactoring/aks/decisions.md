# Décisions Techniques - Refactoring Feature: AKS

## 📋 Décisions Architecturales

### 🔹 D1: Décomposition de KubernetesAksClient en Services Spécialisés

**Contexte** : Le fichier `KubernetesAksClient.cs` fait **4,445 lignes** et gère trop de responsabilités différentes.

**Décision** : Décomposer en **7 services spécialisés** + 1 agrégateur.

**Raisonnement** :
- Principe **Single Responsibility** : Chaque service gère un type de ressource Kubernetes spécifique
- **Testabilité** : Des services plus petits sont plus faciles à tester unitairement
- **Maintenabilité** : Les changements sur une ressource impacte seulement un service
- **Parallélisation** : Permet de travailler sur différentes ressources indépendamment
- **Pattern établi** : SwebKit utilise déjà cette approche pour d'autres features

**Alternatives considérées** :
1. ❌ **Conserver le fichier monolithe** : Rejeté - trop difficile à maintenir
2. ❌ **Décomposer par opération** (Get, List, Delete, etc.) : Rejeté - moins cohérent sémantiquement
3. ✅ **Décomposer par type de ressource** : **Choisie** - aligné avec le domaine Kubernetes
4. ❌ **Décomposer par namespace** : Rejeté - pas pertinent pour la logique métier

**Services proposés** :
```
┌─────────────────────────┬─────────────────────────────┬──────────────┐
│ Service                  │ Responsabilité                │ Lignes Estim. │
├─────────────────────────┼─────────────────────────────┼──────────────┤
│ IPodService               │ Gestion complète des Pods     │ 400-450       │
│ IDeploymentService        │ Gestion des Deployments      │ 350-400       │
│ IServiceService           │ Gestion des Services K8s     │ 250-300       │
│ IIngressService           │ Gestion des Ingress          │ 200-250       │
│ IHelmService              │ Gestion Helm                  │ 300-350       │
│ IResourceService          │ Opérations transverses        │ 300-350       │
│ IKubernetesContextService │ Gestion du contexte           │ 200-250       │
└─────────────────────────┴─────────────────────────────┴──────────────┘
```

**Impact** :
- ✅ Réduction de 85-90% de la taille du fichier principal
- ✅ Meilleure isolation des responsabilités
- ✅ Tests unitaires plus faciles
- ⚠️ Migration progressive nécessaire

---

### 🔹 D2: Injection de Dépendances via Interfaces

**Contexte** : Les services Kubernetes sont actuellement souvent créés avec des dépendances concrètes.

**Décision** : **Toujours injecter via des interfaces** pour faciliter les tests et le mocking.

**Raisonnement** :
- ✅ **Testabilité** : Permet de mock facilement les dépendances
- ✅ **Flexibilité** : Permet de changer d'implémentation facilement
- ✅ **Découplage** : Réduit les dépendances directes entre composants
- ✅ **Pattern établi** : SwebKit utilise déjà ce pattern

**Implémentation** :
```csharp
// ✅ Bon
public class MyComponent
{
    [Inject]
    private IPodService PodService { get; set; }
}

// ❌ À éviter
public class MyComponent
{
    private readonly PodService _podService = new PodService(); // Dépendance concrète
}
```

**Exceptions autorisées** :
- Les services qui sont des **singletons** simples peuvent être créés directement dans `MauiProgram.cs`
- Les **DTOs** et objets de **configuration** ne nécessitent pas d'interface
- Les **extensions methods** peuvent accéder aux implémentations directement

---

### 🔹 D3: Pattern Agrégateur pour la Compatibilité Ascendante

**Contexte** : Beaucoup de code existant dépend de `KubernetesAksClient` tel quel.

**Décision** : Créer un **agrégateur** (`AksClientAggregator`) qui implémente l'ancienne interface et délègue aux nouveaux services.

**Raisonnement** :
- ✅ **Compatibilité** : Pas de breaking changes pour le code existant
- ✅ **Migration progressive** : Peut migrer petit à petit
- ✅ **Centralisation** : Un seul point de délégation
- ⚠️ **Complexité supplémentaire** : une couche d'indirection

**Implémentation** :
```csharp
public class AksClientAggregator : IAksClient
{
    private readonly IPodService _podService;
    private readonly IDeploymentService _deploymentService;
    // ... autres services
    
    public AksClientAggregator(
        IPodService podService,
        IDeploymentService deploymentService,
        /* ... */)
    {
        _podService = podService;
        _deploymentService = deploymentService;
    }
    
    // Délégation des méthodes
    public async Task<List<PodModel>> GetPodsAsync(string ns)
        => await _podService.GetPodsAsync(ns);
    
    public async Task<DeploymentModel> GetDeploymentAsync(string ns, string name)
        => await _deploymentService.GetDeploymentAsync(ns, name);
    
    // ... etc pour toutes les méthodes
}
```

**Stratégie de migration** :
1. **Phase 1** : Créer les nouvelles interfaces et services
2. **Phase 2** : Créer l'agrégateur qui implémente l'ancienne interface
3. **Phase 3** : Changer `MauiProgram.cs` pour enregistrer l'agrégateur
4. **Phase 4** : Migrer progressivement vers les nouveaux services
5. **Phase 5** : Quand tout est migré, supprimer l'agrégateur

---

### 🔹 D4: Décomposition de AksPage.razor

**Contexte** : `AksPage.razor` fait **2,939 lignes** et contient beaucoup de logique métiers.

**Décision** : Décomposer en **1 composant coordinateur + 6 sous-composants** spécialisés.

**Raisonnement** :
- ✅ **Réduction de la complexité** : Chaque composant a une responsabilité claire
- ✅ **Réutilisabilité** : Certains composants peuvent être réutilisés ailleurs
- ✅ **Testabilité** : Composants plus petits = tests plus faciles
- ✅ **Maintenabilité** : Changements isolés à un seul composant

**Structure proposée** :
```
Components/Aks/
├── AksPage.razor                    # Coordinateur (~100-150 lignes)
│                                   # - Gère l'état global
│                                   # - Coordonne les sous-composants
│                                   # - Délègue la logique métier aux services
│
├── AksToolbar.razor                # Barre d'outils principale
│   - Boutons d'action (Refresh, Delete, etc.)
│   - Raccourcis clavier
│   - Tooltips
│
├── NamespaceSelector.razor          # Sélecteur de namespaces
│   - Liste déroulante des namespaces
│   - Filtrage
│   - Sélection multiple
│
├── ContextSelector.razor           # Sélecteur de contexte K8s
│   - Liste des contextes disponibles
│   - Changement de contexte
│   - Indicateur de contexte actuel
│
├── AksFilters.razor                # Filtres avancés
│   - Filtres par statut, labels, etc.
│   - Activation/désactivation
│   - Persistance des préférences
│
├── AksStats.razor                  # Statistiques du cluster
│   - Nombre de pods, deployments, etc.
│   - État général du cluster
│   - Alertes et warnings
│
├── ResourceGrid.razor              # Grille des ressources
│   - Affichage tabulaire
│   - Tri, filtrage, pagination
│   - Sélection multiple
│   - Virtualisation pour les grandes listes
│
└── AksSidePanel.razor              # Rail latéral
    - Conteneur pour les panels de détails
    - Gestion de l'ouverture/fermeture
    - Sélection du panel actif
```

**Communication entre composants** :
- **Événements** : Utiliser des `EventCallback` pour la communication parent-enfant
- **Services** : Utiliser des services partagés pour l'état global
- **Cascading Parameters** : Pour passer des dépendances en profondeur

---

### 🔹 D5: Décomposition de AksDetailPanels.razor

**Contexte** : `AksDetailPanels.razor` fait **1,087 lignes** et gère plusieurs types de détails.

**Décision** : Décomposer en **1 coordinateur + 9 panels spécialisés**.

**Raisonnement** :
- ✅ **Un panel = Un type de ressource** : Principe de Single Responsibility
- ✅ **Meilleure séparation des préoccupations** : Chaque panel gère son propre affichage
- ✅ **Maintenabilité** : Modification d'un type de ressource n'affecte pas les autres
- ✅ **Consistance** : Chaque panel peut suivre le même pattern

**Structure proposée** :
```
Components/Aks/Panels/
├── AksDetailPanels.razor           # Coordinateur (~30-50 lignes)
│   - Gère quel panel est affiché
│   - Passe les données au panel actif
│   - Délègue les actions
│
├── AksPodDetailPanel.razor         # Détails Pod
│   - Informations de base
│   - Containers
│   - Conditions
│   - Événements
│   - Actions (delete, exec, logs)
│
├── AksDeploymentDetailPanel.razor  # Détails Deployment
│   - Configuration
│   - Réplicas
│   - Conditions
│   - History
│   - Actions (scale, restart, edit YAML)
│
├── AksServiceDetailPanel.razor     # Détails Service
│   - Type de service
│   - Ports
│   - Selectors
│   - Endpoints
│
├── AksIngressDetailPanel.razor     # Détails Ingress
│   - Règles
│   - TLS
│   - Backends
│   - Annotations
│
├── AksHelmDetailPanel.razor        # Détails Helm
│   - Informations release
│   - Values
│   - History
│   - Actions (rollback)
│
├── AksJobDetailPanel.razor         # Détails Job
│   - Statut
│   - Pods associés
│   - History
│   - Actions (rerun)
│
├── AksCronJobDetailPanel.razor     # Détails CronJob
│   - Schedule
│   - Dernier run
│   - Prochain run
│   - Actions (run now)
│
├── AksConfigMapDetailPanel.razor   # Détails ConfigMap/Secret
│   - Clés/valeurs
│   - YAML view
│   - Edit mode (pour ConfigMaps)
│
└── AksEventDetailPanel.razor       # Détails Événements
    - Liste des événements
    - Filtres
    - Tri par date
```

**Pattern commun pour tous les panels** :
```razor
@* AksPodDetailPanel.razor *@
@inject IPodService PodService
@inject ILogger<PodDetailPanel> Logger

<SidePanel Title="@Title" OnClose="OnClose">
    <Header>
        @* En-tête spécifique au pod *@
        <h3>@Pod.Name</h3>
        <StatusBadge Status="@Pod.Status" />
    </Header>
    
    <Content>
        @* Contenu spécifique au pod *@
        <PodInfoComponent Pod="Pod" />
    </Content>
    
    <Footer>
        @* Actions spécifiques au pod *@
        <AppButton OnClick="ViewLogs">Logs</AppButton>
        <AppButton OnClick="ExecShell">Shell</AppButton>
    </Footer>
</SidePanel>

@code {
    [Parameter] public PodModel Pod { get; set; }
    [Parameter] public EventCallback OnClose { get; set; }
    
    private string Title => Pod?.Name ?? "Pod Details";
    
    private void OnClose() => OnClose.InvokeAsync(null);
}
```

---

### 🔹 D6: Gestion des Logs - Services Dédiés

**Contexte** : `MultiPodLogView.razor` (954 lignes) et `PodLogView.razor` (884 lignes) ont beaucoup de logique dupliquée.

**Décision** : Extraire la logique dans **3 services dédiés**.

**Raisonnement** :
- ✅ **Réduction de la duplication** : Logique commune factorisée
- ✅ **Meilleure testabilité** : Logique de streaming testable unitairement
- ✅ **Flexibilité** : Peut être utilisé pour d'autres commandes de logs
- ✅ **Performance** : Optimisation centralisée

**Services proposés** :
```
Services/Logs/
├── IPodLogService.cs               # Interface principale
│   - GetPodLogsAsync()
│   - Options de streaming (follow, previous, etc.)
│
├── PodLogStreamingService.cs       # Streaming en temps réel
│   - Gestion du WebSocket/Stream
│   - Buffering
│   - Timeout
│   - Annulation
│
├── PodLogAggregatorService.cs      # Agrégation multi-pods
   - Merge des streams
   - Colorisation par pod
   - Synchronisation temporelle
   - Filtres globaux
│
└── Models/
    ├── LogEntry.cs
    ├── PodLogOptions.cs
    └── MultiPodLogOptions.cs
```

**Nouvelle architecture** :
```
┌──────────────────┐    ┌──────────────────┐    ┌──────────────────┐
│  PodLogView       │    │ MultiPodLogView   │    │   Other Viewers   │
└─────────┬────────┘    └─────────┬────────┘    └─────────┬────────┘
          │                      │                      │
          └──────────────────────┼──────────────────────┘
                                  │
          ┌───────────────────────▼───────────────────────┐
          │                IPodLogService                      │
          │   (Interface unifiée)                            │
          └───────────────┬───────────────────┬────────────┘
                          │                   │
    ┌─────────────────────▼─────┐ ┌────────▼─────────────────┐
    │ PodLogStreamingService      │ │ PodLogAggregatorService     │
    │ - Real-time streaming       │ │ - Multi-pod aggregation    │
    │ - Buffering                 │ │ - Color coding             │
    │ - Timeout handling          │ │ - Timestamp sync          │
    │ - Cancellation              │ │ - Global filtering         │
    └─────────────────────────────┘ └─────────────────────────────┘
                     │                     │
                     └─────────┬─────────┘
                               │
                ┌──────────────▼──────────────┐
                │         Kubernetes SDK         │
                │        (Log endpoints)        │
                └──────────────────────────────┘
```

---

### 🔹 D7: Chargement Paresseux (Lazy Loading) des Ressources

**Contexte** : Actuellement, certaines données sont chargées même quand elles ne sont pas utilisées.

**Décision** : Implémenter un **système de chargement paresseux (Lazy Loading)** pour les données qui ne sont pas immédiatement nécessaires.

**Raisonnement** :
- ✅ **Performance** : Réduction du temps de chargement initial
- ✅ **Efficience** : Évite les appels API inutiles
- ✅ **User Experience** : L'UI est responsive plus rapidement
- ⚠️ **Complexité** : Gestion plus complexe de l'état de chargement

**Implémentation** :

Option 1: Utiliser `Lazy<T>` pour les services
```csharp
// ✅ Pour les services lourds
private readonly Lazy<IPodService> _podService;

public AksPage(IAksClient aksClient)
{
    _podService = new Lazy<IPodService>(() => aksClient.PodService);
}

// Utilisation
private async Task LoadPodsAsync()
{
    var pods = await _podService.Value.GetPodsAsync(CurrentNamespace);
}
```

Option 2: Chargement à la demande pour les données
```csharp
// ✅ Pour les données
private List<DeploymentModel> _deployments;
private bool _deploymentsLoaded;
private bool _isLoadingDeployments;

private async Task LoadDeploymentsIfNeededAsync()
{
    if (!_deploymentsLoaded && !_isLoadingDeployments)
    {
        _isLoadingDeployments = true;
        StateHasChanged();
        
        try
        {
            _deployments = await _deploymentService.GetDeploymentsAsync(CurrentNamespace);
            _deploymentsLoaded = true;
        }
        finally
        {
            _isLoadingDeployments = false;
            StateHasChanged();
        }
    }
}
```

Option 3: Pattern Repository avec Cache
```csharp
public class CachedResourceService<T> where T : class
{
    private readonly IFetchService<T> _fetchService;
    private readonly MemoryCache _cache = new MemoryCache(
        new MemoryCacheOptions { SizeLimit = 1024 });
    
    public async Task<T> GetAsync(string key, Func<Task<T>> fetchFunc)
    {
        if (!_cache.TryGetValue(key, out T value))
        {
            value = await fetchFunc();
            _cache.Set(key, value, TimeSpan.FromSeconds(30));
        }
        return value;
    }
}
```

**Bonnes pratiques** :
- Marquer clairement les données non chargée (état `loading`)
- Afficher un indicateur de chargement à l'utilisateur
- Gérer correctement les erreurs pendant le chargement
- Réessayer automatiquement avec un aurait très bien pu être

---

### 🔹 D8: Pattern Service + ViewModel pour la Séparation des Couches

**Contexte** : Beaucoup de logique métier dans les composants Razor.

**Décision** : Adopter le pattern **Service + ViewModel** pour séparer clairement la couche de présentation de la couche métier.

**Raisonnement** :
- ✅ **Séparation des responsabilités** : UI vs Business Logic
- ✅ **Testabilité** : ViewModels testables sans UI
- ✅ **Maintenabilité** : Changements UI n'affectent pas la logique métier
- ✅ **Consistance** : Pattern standard dans Blazor

**Exemple d'implémentation** :
```csharp
// Service (Logique métier pure)
public interface IPodService
{
    Task<List<PodViewModel>> GetPodsAsync(string ns);
    Task<PodViewModel> GetPodAsync(string ns, string name);
    Task DeletePodAsync(string ns, string name);
}

public class PodService : IPodService
{
    private readonly IKubernetesClient _k8sClient;
    
    public async Task<List<PodViewModel>> GetPodsAsync(string ns)
    {
        var k8sPods = await _k8sClient.ListNamespacedPodAsync(ns);
        return k8sPods.Items.Select(ToViewModel).ToList();
    }
    
    private PodViewModel ToViewModel(V1Pod pod) => new()
    {
        Name = pod.Metadata.Name,
        Namespace = pod.Metadata.Namespace,
        Status = pod.Status.Phase,
        CreationTimestamp = pod.Metadata.CreationTimestamp,
        NodeName = pod.Spec.NodeName
    };
}

// ViewModel (Données prêtes pour l'UI)
public class PodViewModel
{
    public string Name { get; set; }
    public string Namespace { get; set; }
    public string Status { get; set; }
    public DateTime CreationTimestamp { get; set; }
    public string NodeName { get; set; }
    
    // Propriétés calculées pour l'UI
    public string StatusIcon => Status.ToStatusIcon();
    public string StatusColor => Status.ToStatusColor();
    public string DisplayName => $"{Name} ({Namespace})";
}

// Component (Affichage seulement)
public partial class PodGrid
{
    [Inject] private IPodService PodService { get; set; }
    
    private List<PodViewModel> _pods;
    private bool _isLoading;
    
    protected override async Task OnInitializedAsync()
    {
        _isLoading = true;
        _pods = await PodService.GetPodsAsync(CurrentNamespace);
        _isLoading = false;
    }
}
```

**Avantages** :
- Le service ne connaît pas Blazor/MAUI
- Le ViewModel ne connaît pas l'UI
- Le composant ne fait que de l'affichage et de la coordination
- Facile à tester : chaque couche peut être testée indépendamment

---

### 🔹 D9: Pattern CQRS Light pour les Opérations

**Contexte** : Difficulté à séparer clairement les opérations de lecture vs écriture.

**Décision** : Adopter une version légère de **CQRS** (Command Query Responsibility Segregation) pour séparer clairement les opérations.

**Raisonnement** :
- ✅ **Clarté** : Méthodes clairement identifiées comme Query ou Command
- ✅ **Sécurité** : Les Commands peuvent avoir des validations spécifiques
- ✅ **Audit** : Plus facile de logger/tracer les opérations d'écriture
- ✅ **Scalabilité** : Peut évoluer vers un vrai CQRS si besoin

**Implémentation light** :
```csharp
// ✅ Utilisation de conventions de nommage
public interface IPodService
{
    // Queries (lecture seule, pas de side effect)
    Task<List<PodModel>> GetPodsAsync(string ns);
    Task<PodModel> GetPodAsync(string ns, string name);
    Task<List<PodEvent>> GetPodEventsAsync(string ns, string name);
    Task<string> GetPodLogsAsync(string ns, string name, string container, PodLogOptions options);
    
    // Commands (modification, side effects)
    Task DeletePodAsync(string ns, string name, string reason = null);
    Task RestartPodAsync(string ns, string name);
    Task<string> ExecuteCommandAsync(string ns, string pod, string container, string command);
}

// ✅ Pattern Command pour les opérations complexes
public abstract record PodCommand(string Namespace, string PodName) : ICommand;

public record DeletePodCommand(string Namespace, string PodName, string Reason = null) : PodCommand(Namespace, PodName);
public record ExecPodCommand(string Namespace, string PodName, string Container, string Command) : PodCommand(Namespace, PodName);

public interface ICommandHandler<TCommand> where TCommand : ICommand
{
    Task HandleAsync(TCommand command, CancellationToken ct = default);
}

public class PodCommandHandler : 
    ICommandHandler<DeletePodCommand>,
    ICommandHandler<ExecPodCommand>
{
    private readonly IKubernetesClient _k8sClient;
    private readonly ILogger _logger;
    
    public async Task HandleAsync(DeletePodCommand command, CancellationToken ct)
    {
        // Validation
        if (string.IsNullOrEmpty(command.Namespace))
            throw new ValidationException("Namespace is required");
        
        // Log audit
        _logger.LogInformation("Deleting pod {Pod} in namespace {Ns}. Reason: {Reason}",
                               command.PodName, command.Namespace, command.Reason);
        
        // Exécution
        await _k8sClient.DeleteNamespacedPodAsync(command.PodName, command.Namespace);
        
        // Notification
        _logger.LogInformation("Successfully deleted pod {Pod}", command.PodName);
    }
}
```

**Améliorations possibles** (si le pattern se généralise) :
- Utiliser MediatR pour le pattern Command/Query
- Séparer complètement les modèles de Query et Command
- Implémenter Event Sourcing pour l'audit traçable

---

### 🔹 D10: Gestion des Erreurs Centralisée

**Contexte** : Chaque service gère les erreurs Kubernetes différemment.

**Décision** : Créer un **système de gestion des erreurs centralisé** avec des handlers uniformes.

**Raisonnement** :
- ✅ **Consistance** : Toutes les erreurs gérées de la même manière
- ✅ **User Friendly** : Messages d'erreur clairs et compréhensibles
- ✅ **Logging** : Erreurs loguées systématiquement
- ✅ **Recovery** : Possibilité de recovery automatique

**Implémentation** :
```csharp
// Custom exceptions domain
public class AksException : Exception
{
    public AksErrorType ErrorType { get; }
    public bool IsRecoverable { get; }
    public string UserMessage { get; }
    public string TechnicalDetails { get; }
    public Dictionary<string, object> Metadata { get; } = new();
    
    public AksException(AksErrorType type, string message, Exception inner = null,
                       bool isRecoverable = false, string userMessage = null)
        : base(message, inner)
    {
        ErrorType = type;
        IsRecoverable = isRecoverable;
        UserMessage = userMessage ?? GetDefaultUserMessage(type);
        TechnicalDetails = GetTechnicalDetails();
    }
    
    private static string GetDefaultUserMessage(AksErrorType type) => type switch
    {
        AksErrorType.ConnectionFailed => "Failed to connect to Kubernetes cluster",
        AksErrorType.AuthenticationFailed => "Authentication failed",
        AksErrorType.ResourceNotFound => "Resource not found",
        AksErrorType.Forbidden => "Access denied",
        AksErrorType.Conflict => "Resource conflict",
        AksErrorType.Timeout => "Operation timed out",
        _ => "An error occurred"
    };
}

public enum AksErrorType
{
    ConnectionFailed,
    AuthenticationFailed,
    ResourceNotFound,
    Forbidden,
    Conflict,
    Timeout,
    RateLimited,
    InternalServerError,
    Unknown
}

// Error handler central
public class AksErrorHandler
{
    private readonly ILogger<AksErrorHandler> _logger;
    private readonly INotificationService _notifications;
    private readonly IAppEventBus _eventBus;
    
    public void Handle(AksException exception)
    {
        var logLevel = exception.IsRecoverable ? LogLevel.Warning : LogLevel.Error;
        _logger.Log(logLevel, exception, "AKS Error [{ErrorType}]: {Message}",
                   exception.ErrorType, exception.Message);
        
        if (exception.IsRecoverable)
        {
            _notifications.ShowWarning(exception.UserMessage, "Operation may be retried");
        }
        else
        {
            _notifications.ShowError(exception.UserMessage, "Operation failed");
        }
        
        _eventBus.Publish(new AksErrorEvent(exception));
    }
    
    public void Handle(Exception exception, string context = null)
    {
        AksException aksError;
        
        if (exception is KubernetesException k8sEx)
        {
            aksError = MapKubernetesException(k8sEx);
        }
        else if (exception is AksException)
        {
            aksError = (AksException)exception;
        }
        else
        {
            aksError = new AksException(AksErrorType.Unknown, exception.Message, exception);
        }
        
        if (context != null)
            aksError.Metadata["Context"] = context;
        
        Handle(aksError);
    }
    
    private AksException MapKubernetesException(KubernetesException k8sEx)
    {
        return k8sEx.Status.Code switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden 
                => new AksException(AksErrorType.Forbidden, k8sEx.Message, k8sEx),
            HttpStatusCode.NotFound 
                => new AksException(AksErrorType.ResourceNotFound, k8sEx.Message, k8sEx),
            HttpStatusCode.Conflict 
                => new AksException(AksErrorType.Conflict, k8sEx.Message, k8sEx),
            HttpStatusCode.RequestTimeout or HttpStatusCode.GatewayTimeout
                => new AksException(AksErrorType.Timeout, k8sEx.Message, k8sEx),
            HttpStatusCode.TooManyRequests
                => new AksException(AksErrorType.RateLimited, k8sEx.Message, k8sEx),
            HttpStatusCode.InternalServerError
                => new AksException(AksErrorType.InternalServerError, k8sEx.Message, k8sEx),
            _ => new AksException(AksErrorType.Unknown, k8sEx.Message, k8sEx)
        };
    }
}

// Service wrapper avec gestion d'erreur intégrée
public class ErrorHandlingDecorator<T> : T where T : class
{
    private readonly T _actualService;
    private readonly AksErrorHandler _errorHandler;
    
    public ErrorHandlingDecorator(T actualService, AksErrorHandler errorHandler)
    {
        _actualService = actualService;
        _errorHandler = errorHandler;
    }
    
    // Les appels sont interceptés et les erreurs sont gérées automatiquement
    // (Implémentation via code généré ou reflection)
}
```

---

## 📊 Résumé des Décisions

| ID | Décision | Type | Impact | Statut |
|----|----------|------|--------|--------|
| D1 | Décomposition de KubernetesAksClient en services spécialisés | Architecture | ⭐⭐⭐⭐⭐ | ✅ Approuvée |
| D2 | Injection de dépendances via interfaces | Pattern | ⭐⭐⭐⭐ | ✅ Approuvée |
| D3 | Pattern Agrégateur pour la compatibilité | Architecture | ⭐⭐⭐⭐ | ✅ Approuvée |
| D4 | Décomposition de AksPage.razor | UI | ⭐⭐⭐⭐ | ✅ Approuvée |
| D5 | Décomposition de AksDetailPanels.razor | UI | ⭐⭐⭐ | ✅ Approuvée |
| D6 | Services dédiés pour les logs | Architecture | ⭐⭐⭐ | ✅ Approuvée |
| D7 | Lazy Loading | Performance | ⭐⭐⭐ | ✅ Approuvée |
| D8 | Pattern Service + ViewModel | Architecture | ⭐⭐⭐⭐ | ✅ Approuvée |
| D9 | Pattern CQRS Light | Architecture | ⭐⭐⭐ | ✅ Approuvée |
| D10 | Gestion des erreurs centralisée | Infrastructure | ⭐⭐⭐⭐ | ✅ Approuvée |

---

## 🎯 Prochaines Décisions à Prendre

1. **Choix du pattern de cache**
   - Option A: `MemoryCache` (simple, en mémoire)
   - Option B: `DistributedCache` (partagé entre instances)
   - Option C: Cache personnalisé avec invalidation intelligente
   - **Recommandation** : Commencer avec MemoryCache, évoluer si besoin

2. **Stratégie de pagination pour les grands datasets**
   - Option A: Client-side (tout charger, paginer localement)
   - Option B: Server-side (Kubernetes native pagination)
   - Option C: Lazy loading avec scroll infini
   - **Recommandation** : Server-side pour > 100 items, client-side pour < 100

3. **Virtualisation des listes**
   - Option A: Utiliser `<Virtualize>` de Microsoft
   - Option B: Implémenter custom avec Intersection Observer
   - **Recommandation** : Utiliser `<Virtualize>` pour commencer

4. **Streaming des logs multi-pods**
   - Option A: WebSocket natif (si supporté par l'API)
   - Option B: SignalR pour la communication temps réel
   - Option C: Polling intelligent avec buffer
   - **Recommandation** : Polling + WebSocket native si disponible

---

## 📝 Historique des Décisions

| Date | Décision | Auteur | Justification |
|------|----------|--------|---------------|
| 2026-07-11 | Décomposition en services spécialisés | IA Assistant | Analyse du fichier monolithe, 4,445 lignes |
| 2026-07-11 | Injection via interfaces | IA Assistant | Standard de l'industrie, testabilité |
| 2026-07-11 | Pattern Agrégateur | IA Assistant | Compatibilité ascendante nécessaire |
| 2026-07-11 | Décomposition AksPage | IA Assistant | 2,939 lignes, trop complexe |
| 2026-07-11 | Tous les autres | IA Assistant | Analyse complète du codebase |

---

*Créé le: {{date}}*
*Dernière mise à jour: {{date}}*
*Statut: En planification*
*Responsable: À assigner*

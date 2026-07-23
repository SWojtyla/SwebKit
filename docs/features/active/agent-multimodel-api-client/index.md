# Agent multi-modèle et pilotage sécurisé de l’API Client

Ce plan découple l’agent de Mistral, ajoute LM Studio et les endpoints OpenAI-compatible, puis lui permet de planifier, prévisualiser et exécuter sous confirmation des opérations sur les requêtes REST de l’API Client.

## Décisions validées

- Architecture multi-fournisseur.
- Profils V1 officiellement supportés : LM Studio, endpoint OpenAI-compatible générique et Mistral.
- Si le modèle ne réussit pas le test de tool calling : chat simple disponible, outils désactivés avec diagnostic explicite.
- Intelligence prioritaire : contexte actif enrichi, plan d’action explicite, validation des résultats et reprise après erreur.
- Périmètre API Client V1 : requêtes REST et organisation (collections/dossiers/requêtes locales et liées), sans gestion agentique des environnements, variables, auth, GraphQL ou WebSocket.
- Toute mutation est une proposition avec aperçu/diff et confirmation explicite avant application.
- Toute exécution HTTP exige une prévisualisation résolue et une confirmation, y compris GET/HEAD.

## Constat actuel

- `SwebKit.Agents` est couplé nominalement et structurellement à Mistral (`IMistralClient`, `MistralConfig`, `MistralHttpClient`, credential key et textes UI).
- La boucle `/chat/completions` et le format `tool_calls` sont proches du protocole OpenAI-compatible ; le registre d’outils est déjà réutilisable.
- La configuration `ModelOverride` est persistée mais n’est pas appliquée par `AgentChatService` au client courant.
- Les messages sont des `object` anonymes, ce qui rend l’adaptation multi-fournisseur, les tests et la reprise d’une action confirmée fragiles.
- Les mutations API Client sont actuellement enfermées dans la page Blazor et son `ApiClientState` page-scoped. L’agent ne doit ni dépendre du composant ni reproduire cette logique.
- Les collections locales ont un repository atomique ; les collections liées ont des opérations spécialisées et des content stamps/conflits qu’il faut préserver.
- Il n’existe pas d’événement dédié pour synchroniser une page API Client déjà ouverte après une mutation externe.

## Architecture cible

```text
AgentChatPanel
  -> IAgentChatService
     -> IAgentModelClient
        -> OpenAiCompatibleAgentClient
           -> LM Studio / endpoint générique / Mistral
     -> IAgentToolRegistry
        -> outils de lecture immédiats
        -> outils mutatifs produisant une PendingAgentAction

Agent/API Client tools
  -> IApiClientAgentService
     -> CollectionRepository (local)
     -> LinkedCollectionFileService + LinkedCollectionRootRepository (lié)
     -> IHttpRequestExecutor (exécution après confirmation)
  -> IAgentActionCoordinator
     -> preview/diff -> confirmation UI -> contrôle de fraîcheur -> apply -> événement refresh
```

### Principes structurants

- Remplacer les types `Mistral*` exposés à l’orchestrateur par des contrats neutres ; conserver les détails fournisseur dans l’adaptateur HTTP.
- Utiliser des DTO typés pour messages, tool calls, résultats, usage, erreurs et capacités au lieu de `List<object>`.
- Configurer un profil actif contenant `ProviderKind`, endpoint, modèle, paramètres et référence de credential ; ne jamais persister une clé en clair.
- Ne pas basculer automatiquement de fournisseur : le profil actif reste explicite et observable.
- Séparer strictement `propose` et `apply`. Aucun argument généré par le modèle ne déclenche directement une écriture ou un appel HTTP.
- Une proposition est un snapshot borné avec identifiants stables, résumé, diff avant/après, niveau de risque, empreinte/version attendue et expiration.
- Au moment de confirmer, recharger la cible et vérifier l’empreinte ; en cas de divergence, refuser l’application et demander une nouvelle proposition.
- Masquer les secrets dans le contexte, les previews, les logs, les résultats d’outils et l’historique du modèle.

## Phase 0 — Documentation de feature et décisions

1. Créer une feature active dédiée avec `index.md`, `status.md`, `decisions.md` et `test-plan.md`.
2. Enregistrer les décisions suivantes : protocole compatible OpenAI comme frontière V1, profils explicites sans fallback automatique, détection stricte du tool calling, protocole proposal/confirmation, ownership des mutations dans un service Core, et limites REST V1.
3. Mettre à jour en fin de livraison les cartes d’architecture, le deep dive Agent, le deep dive API Client et le guide du codebase.

## Phase 1 — Modèle de configuration multi-fournisseur

### Modèles et persistance

1. Faire évoluer `AgentConfig` vers un profil actif et une liste de profils :
   - identifiant et nom d’affichage ;
   - type `LmStudio`, `OpenAiCompatible`, `Mistral` ;
   - base URL normalisée ;
   - modèle ;
   - clé logique du credential, optionnelle pour LM Studio ;
   - température, limite de sortie, timeout ;
   - état/capacités issus du dernier test.
2. Ajouter une normalisation/migration depuis la configuration actuelle :
   - transformer `ModelOverride` et l’ancienne credential Mistral en profil Mistral ;
   - ne supprimer l’ancienne credential qu’après migration validée ;
   - conserver des valeurs par défaut sûres si des champs JSON manquent.
3. Presets :
   - LM Studio : `http://localhost:1234/v1`, clé facultative ;
   - Mistral : endpoint actuel et credential existante ;
   - générique : endpoint/modèle/credential saisis par l’utilisateur.
4. Ne pas persister de résultat contenant la clé ou les headers d’autorisation.

### UI de réglages

1. Remplacer le formulaire exclusivement Mistral par : fournisseur, endpoint, modèle, clé éventuelle, paramètres, test de connexion, profil actif.
2. Pour LM Studio, proposer la découverte via `GET /v1/models`, avec saisie manuelle en secours.
3. Afficher séparément : serveur joignable, modèle disponible, chat valide, tool calling valide.
4. Permettre le chat simple quand le serveur répond mais que le tool calling échoue ; afficher clairement que les actions sont indisponibles.
5. L’activation de l’agent ne doit plus dépendre de la présence d’une clé si le profil actif n’en exige pas.

## Phase 2 — Contrat LLM neutre et client compatible

1. Introduire `IAgentModelClient` et des DTO typés : requête, message, réponse, tool call, finish reason, erreur et capacités.
2. Renommer/refactorer `MistralHttpClient` en adaptateur OpenAI-compatible :
   - construction d’URL robuste sans double `/v1` ;
   - auth Bearer seulement si une clé existe ;
   - parsing tolérant des contenus null et `tool_calls` ;
   - propagation de l’annulation ;
   - timeout explicite ;
   - erreurs fournisseur nettoyées, sans fuite de payload sensible ;
   - limite de tours configurable et protection contre répétition du même appel.
3. Garder des stratégies/presets par fournisseur uniquement pour les divergences réelles, sans dupliquer la boucle agentique.
4. Ajouter un service de test de capacités :
   - `GET /models` si disponible ;
   - mini appel chat ;
   - mini outil sans effet avec schéma strict ;
   - classification `ChatOnly` ou `ToolCalling`.
5. Appliquer réellement le modèle et les paramètres du profil actif à chaque conversation.
6. Lors d’un changement de profil/modèle, invalider ou réinitialiser l’historique pour éviter de mélanger des formats/capacités incompatibles.

## Phase 3 — Orchestrateur plus fiable

1. Remplacer l’historique anonyme par une session typée qui conserve correctement les séquences assistant/tool.
2. Construire un prompt en sections stables : rôle, contexte actif, politique d’outils, politique de confirmation, limites, format de réponse.
3. Enrichir `AgentContextBuilder` avec un snapshot API Client sans valeurs secrètes : page active, cible locale/liée, collection/dossier/requête sélectionnés, méthode/URL non résolue, état dirty et environnement actif par nom uniquement.
4. Ajouter des métadonnées aux outils : lecture ou mutation, capacité requise, risque, disponibilité selon fournisseur.
5. Rendre la boucle auto-corrective mais bornée :
   - validation JSON des arguments ;
   - résultat d’erreur structuré permettant au modèle de corriger une fois ;
   - limite de tours et détection des doublons ;
   - synthèse finale mentionnant ce qui a été lu, proposé ou appliqué.
6. Exposer dans `AgentChatReply` les étapes/outils et les propositions en attente, sans révéler les noms internes dans le texte généré.
7. Prévoir des statuts UI : réflexion, lecture du contexte, préparation d’un changement, attente de confirmation, application, terminé/échoué.

## Phase 4 — Service applicatif API Client réutilisable

1. Créer un contrat Core `IApiClientAgentService` indépendant de Blazor et de `ApiClientState`.
2. Déplacer/extraire vers ce service les opérations métier actuellement dupliquées dans les partials de page :
   - lister collections, dossiers et requêtes avec IDs stables et origine locale/liée ;
   - lire une requête REST complète avec secrets référencés mais jamais résolus ;
   - créer une requête à la racine ou dans un dossier ;
   - modifier nom, méthode, URL, headers, query params et body ;
   - dupliquer ;
   - déplacer/réordonner dans une même collection ;
   - renommer un dossier ;
   - supprimer une requête ou un dossier ;
   - créer une collection si cette opération est nécessaire à la demande explicite.
3. Faire consommer le même service par la page et par les outils de l’agent afin d’éviter deux implémentations divergentes.
4. Préserver les deux chemins de persistance :
   - local : mutation du modèle + `CollectionRepository.UpdateCollectionAsync` ;
   - lié : opérations `LinkedCollectionFileService`, content stamps, sidecars, path scoping et détection de conflit.
5. Ajouter un événement `ApiClientDataChanged` avec cible et IDs affectés. La page ouverte recharge de façon ciblée, conserve la sélection si possible et ne perd jamais un brouillon dirty ; si conflit avec un brouillon, afficher une résolution au lieu d’écraser.
6. Sérialiser les mutations concurrentes par cible et propager l’annulation.

## Phase 5 — Propositions, diff et confirmations

1. Introduire `IAgentActionCoordinator` avec stockage mémoire borné des actions en attente : ID opaque, expiration, profil/session, type, cible, snapshot attendu, preview et payload validé.
2. Les outils mutatifs appellent uniquement `Prepare*Async` et retournent une proposition structurée.
3. Construire des previews déterministes :
   - création : emplacement et représentation complète ;
   - modification : diff champ par champ, headers/query/body inclus avec masquage ;
   - déplacement/renommage : ancien et nouvel emplacement ;
   - suppression : nom, chemin, descendants et nombre d’éléments supprimés ;
   - cible liée : badge explicite avec chemin relatif dans `.swebkit-api`, jamais un chemin hors racine.
4. Afficher dans le panneau agent une carte de confirmation native, non un simple texte généré : `Appliquer`, `Refuser`, expiration et niveau de risque.
5. À `Appliquer`, revalider l’identité, l’empreinte et le scope, exécuter une seule fois, publier l’événement de refresh et ajouter le résultat à la conversation.
6. À `Refuser`, invalider l’action et informer le modèle sans exécuter.
7. Exiger une confirmation distincte par action destructive ; ne pas accepter un « oui » ambigu du modèle comme autorisation.
8. Journaliser uniquement les métadonnées non sensibles : type d’action, cible logique, résultat, durée et motif d’échec.

## Phase 6 — Outils API Client V1

Préférer quelques outils cohérents à une explosion d’outils fins :

1. `search_api_requests` : recherche/liste avec IDs, chemins, méthodes et origine.
2. `get_api_request` : lecture complète non résolue et masquée.
3. `propose_api_request_change` : create/update/duplicate/move/rename avec opération discriminée et schéma strict.
4. `propose_api_request_delete` : outil séparé pour rendre la destruction explicite.
5. `prepare_api_request_execution` : prépare l’exécution, résout les variables côté application, masque auth/secrets et crée une action confirmable.
6. Ne jamais exposer aux outils V1 : valeur des credentials, contenu secret résolu, opérations Git, environnements/variables/auth en écriture, GraphQL ou WebSocket.

## Phase 7 — Exécution HTTP confirmée

1. Réutiliser `IHttpRequestExecutor` et les mécanismes existants d’auth, substitution, SSL et capture, sans créer un second client HTTP.
2. La preview affiche méthode, URL résolue, noms de headers, valeurs non sensibles, body masqué, environnement et avertissement d’effet externe.
3. La confirmation applique un snapshot de la définition ; si la requête ou l’environnement a changé depuis la preview, annuler et reconstruire la preview.
4. Retourner au modèle une réponse bornée : statut, durée, taille, content type, headers filtrés et body tronqué/masqué.
5. Ne pas exécuter automatiquement les règles de capture sans les signaler dans la preview ; si elles modifient des variables, les inclure dans les effets attendus et la confirmation.
6. Respecter un timeout, une limite de taille et l’annulation ; ne pas injecter la réponse complète dans l’historique si elle dépasse le budget.

## Phase 8 — Tests et validation

### Tests unitaires Agent

- Migration de l’ancienne configuration Mistral vers un profil.
- URL/preset/auth pour LM Studio, générique et Mistral.
- Parsing chat simple, tool call simple/multiple, contenu null, erreur HTTP et annulation.
- Détection `ChatOnly` vs `ToolCalling` avec handlers HTTP simulés.
- Application effective du modèle actif.
- Historique typé et séquences assistant/tool valides.
- limites de tours, appel répété, arguments invalides et correction bornée.
- masquage des secrets dans prompts, erreurs, previews et résultats.

### Tests unitaires API Client

- CRUD local sur arbres imbriqués ; IDs, timestamps, nom du node et nom de request synchronisés.
- CRUD lié utilisant uniquement les primitives de `LinkedCollectionFileService`.
- conflit de content stamp entre preview et confirmation.
- déplacement/réordonnancement, duplication et suppression récursive.
- refus de traversée de chemin ou de cible hors linked root.
- action expirée, refusée, rejouée ou confirmée deux fois.
- exécution impossible sans confirmation.
- réponse HTTP tronquée et nettoyée ; capture rule annoncée.

### Tests UI/bUnit

- formulaire de profils et migration visible.
- test de connexion/capacités et mode chat-only.
- carte diff/confirmation/refus/expiration.
- changement externe reçu par une page API Client ouverte.
- protection d’un brouillon dirty contre un refresh agentique.
- statuts d’outils et erreurs actionnables.
- mise à jour des DI hosts lorsque de nouveaux services sont injectés.

### Validation manuelle

1. LM Studio arrêté : diagnostic clair, application stable.
2. LM Studio lancé sans modèle : état dédié.
3. Modèle sans tools : chat fonctionne, actions désactivées.
4. Modèle avec tools : lecture puis proposition de création locale, confirmation, refresh immédiat.
5. Modification et suppression locale refusées puis acceptées.
6. Même parcours dans un linked root, puis conflit provoqué par une modification externe.
7. Prévisualisation et confirmation d’un GET puis d’un POST ; aucune requête réseau avant confirmation.
8. Redémarrage et migration d’un utilisateur ayant l’ancienne configuration Mistral.

### Commandes de qualité

- Tests ciblés `SwebKit.Agents.Tests`.
- Tests ciblés API Client de `SwebKit.Core.Tests` et `SwebKit.App.Tests`.
- Build de la solution/app Windows.
- Scan des logs et snapshots de tests pour vérifier l’absence de secrets.

## Critères d’acceptation

- L’agent fonctionne sans clé cloud avec LM Studio lorsqu’un modèle compatible est chargé.
- L’utilisateur peut choisir LM Studio, Mistral ou un endpoint OpenAI-compatible et tester connexion/modèle/tool calling.
- Un modèle sans tool calling ne peut déclencher aucune action ; le chat simple reste utilisable.
- Aucun composant d’orchestration ne dépend d’un type nommé Mistral.
- L’agent comprend la collection/requête active sans recevoir de secret résolu.
- Il peut rechercher, lire et proposer le CRUD/organisation REST sur collections locales et liées.
- Aucune écriture, suppression ou exécution HTTP n’a lieu avant confirmation UI explicite.
- Le diff correspond exactement au changement appliqué ; toute divergence depuis la preview bloque l’action.
- Une page API Client ouverte reflète le changement sans perdre un brouillon non sauvegardé.
- Les conflits linked-root existants restent respectés ; aucun fichier hors `.swebkit-api` n’est touché.
- Les clés, tokens, auth headers et valeurs secrètes ne figurent ni dans le modèle, ni dans les logs, ni dans les previews.
- Les tests ciblés et le build passent, et la documentation d’architecture reflète le nouveau flux.

## Hors périmètre V1

- Fallback automatique cloud/local.
- Fallback prompt-JSON pour modèles sans tool calling natif.
- Mémoire durable inter-session et RAG.
- Streaming de tokens, vision ou embeddings.
- Écriture d’environnements, variables, credentials ou auth.
- GraphQL/WebSocket agentiques.
- Opérations Git (stage/commit/push/revert).
- Exécution sans confirmation, même pour GET/HEAD.

## Risques et mitigations

- Compatibilité « OpenAI-compatible » variable : tests de capacités réels, DTO tolérants et outils désactivés en cas d’échec.
- Petits modèles locaux moins fiables : schemas courts, outils peu nombreux, contexte borné, validation stricte et aucune mutation directe.
- État API Client détenu par la page : extraire d’abord le service métier et utiliser événements/snapshots plutôt qu’injecter le composant.
- Concurrence avec fichiers liés/Git : content stamps au prepare/apply et opérations existantes scoppées.
- Fuite de secrets vers un modèle cloud : contexte non résolu, redaction centralisée, tests de non-divulgation.
- Réponses HTTP volumineuses : limites de taille, troncature et résumé avant ajout à l’historique.

## Ordre de livraison recommandé

1. Configuration/profils + client neutre + migration + tests.
2. Détection de capacités et UI LM Studio.
3. Historique typé, contexte actif et fiabilité de boucle.
4. Extraction du service API Client et synchronisation UI.
5. Infrastructure proposal/diff/confirmation.
6. Outils CRUD REST locaux, puis linked roots.
7. Exécution HTTP confirmée.
8. Durcissement sécurité, tests complets et documentation.

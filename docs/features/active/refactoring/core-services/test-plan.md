# Test Plan - Feature: Core Services

## 🎯 Objectifs de Test

### Pour la feature Core Services
- ✅ **Couverture de code > 85%** (target: 90%+ pour les services)
- ✅ **Tous les tests de régression passent**
- ✅ **Tests exécutés en < 30 secondes** (par feature)
- ✅ **0 régression critique**

## 🧪 Stratégie de Test

### 1. Tests Unitaires des Services
**Cible**: Tous les nouveaux services créés
**Outils**: xUnit + Moq + FluentAssertions

| Service | Complexité | Tests à écrire | Priorité |
|---------|------------|---------------|----------|
| [À compléter par feature] | ⭐⭐⭐⭐ | ~10-15 tests | 🟡 |

**Exemple** pour la feature Core Services:
- Test de création des ressources
- Test de récupération
- Test de mise à jour
- Test de suppression
- Test des erreurs
- Test des edge cases

### 2. Tests de Composants bUnit
**Cible**: Tous les nouveaux composants Razor
**Outils**: bUnit

| Composant | Type | Tests à écrire | Priorité |
|-----------|------|---------------|----------|
| [À compléter] | Page | 5-8 tests | 🟡 |
| [À compléter] | Tile | 3-5 tests | 🟡 |

### 3. Tests d'Intégration
**Cible**: Intégration entre services et composants
**Outils**: xUnit + WebApplicationFactory

- Tests de flux complet
- Tests d'interaction services ↔ composants
- Tests de navigation

### 4. Tests de Régression
**Checklist**: Toutes les fonctionnalités existantes doivent être testées

## 📋 Checklist des Tests de Régression

### Fonctionnalités Critiques
- [ ] [Fonctionnalité 1]
- [ ] [Fonctionnalité 2]
- [ ] [Fonctionnalité 3]

### Cas d'Utilisation Principaux
- [ ] [Cas d'utilisation 1]
- [ ] [Cas d'utilisation 2]
- [ ] [Cas d'utilisation 3]

## 📊 Couverture de Code Cible

| Catégorie | Avant | Après | Statut |
|-----------|-------|-------|--------|
| Services | TBR% | > 90% | ⬜ |
| Composants | TBR% | > 85% | ⬜ |
| Global | TBR% | > 85% | ⬜ |

## 🏷️ Catégories de Tests

- `Unit` - Tests unitaires des services
- `Component` - Tests de composants bUnit  
- `Integration` - Tests d'intégration
- `Regression` - Tests de régression

## 💡 Conseils Spécifiques à Core Services

[Conseils spécifiques à ajouter par feature]

---

*Créé le: {date}*
*Lié à: [index.md](./index.md)*

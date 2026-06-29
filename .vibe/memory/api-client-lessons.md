# 🧠 API Client UI Implementation - Lessons Learned

**Project**: SwebKit - API Client UI Improvements  
**Agent**: Mistral Vibe  
**Date**: 2025-06-27  
**Status**: Active Learning Document

---

## 🎯 **CRITICAL LESSON: CSS Isolation in Blazor**

### ❌ What I Did Wrong (Phase 1)
- Created new CSS files with `@import url()` statements inside scoped CSS files
- Assumed CSS tokens would be globally available to isolated components
- Used `@import` in `CollectionTree.razor.css` which is **NOT ALLOWED** in Blazor scoped CSS

### ✅ What I Learned
- **Blazor scoped CSS** (`.razor.css` files) **cannot use `@import`** - this causes build errors
- Scoped CSS has higher specificity and **takes precedence** over global CSS
- Tokens defined in global CSS **are available** to scoped CSS, but **cannot be imported** within scoped CSS
- To use global tokens in scoped CSS: **just reference them directly** (they're available in `:root`)

### 📝 Correct Approach
```css
/* CollectionTree.razor.css - SCOPED CSS */
/* ✅ CORRECT: Just use global tokens directly */
.collection-tree__row--selected {
    background: var(--api-accent-subtle);  /* Uses global token */
    color: var(--color-text);
}

/* ❌ WRONG: Cannot use @import in scoped CSS */
/* @import url('Styles/00-api-tokens.css'); */
```

### 📁 File Structure Solution
```
wwwroot/
├── app.css                    # Global CSS with @import for tokens
│   @import url("css/Styles/00-api-tokens.css");
│   @import url("css/Styles/01-api-base.css");
│   @import url("css/Styles/02-api-components.css");
├── css/
│   └── Styles/               # API Client design tokens and styles
│       ├── 00-api-tokens.css  # Global tokens (no isolation)
│       ├── 01-api-base.css    # Global base styles (no isolation)
│       └── 02-api-components.css # Global component styles (no isolation)
└── Components/ApiClient/
    └── CollectionTree.razor.css # Scoped CSS - uses global tokens
```

**IMPORTANT**: In .NET MAUI Blazor, CSS files in the Components folder are NOT automatically served as static assets. They must be placed in wwwroot or a subfolder of wwwroot to be accessible via @import.

---

## 🎨 **CSS Architecture Best Practices**

### Token System
- **Global tokens**: Define in `.css` files in `wwwroot` or `Components/ApiClient/Styles/`
- **Scoped components**: Reference tokens directly using `var(--token-name)`
- **Token naming**: Use consistent prefix like `--api-*` for API Client tokens

### Specificity Hierarchy
1. **Scoped CSS** (`.razor.css`) - Highest specificity for its component
2. **Global CSS** - Applies everywhere but can be overridden by scoped
3. **Inline styles** - Highest specificity overall

### Performance Considerations
- Use CSS variables for colors to enable theme switching
- Use `color-mix()` for subtle background colors
- Use `calc()` for dynamic sizing (e.g., indentation based on depth)
- Enable GPU acceleration with `transform: translateZ(0)` for scrolling containers

---

## 🔧 **Fluent UI Icon Caching**

### ❌ What I Did Wrong
```csharp
// ❌ WRONG: Using object type
private static readonly object _iconFolder = new Icons.Regular.Size16.Folder();
```

### ✅ What I Learned
- Fluent UI icons must be typed as `Microsoft.FluentUI.AspNetCore.Components.Icon`
- The `FluentIcon` component takes a `Value` parameter of type `Icon`
- Caching icons as `static readonly` prevents unnecessary re-renders

### 📝 Correct Approach
```csharp
// ✅ CORRECT: Use proper Icon type
private static readonly Microsoft.FluentUI.AspNetCore.Components.Icon _iconFolder = 
    new Icons.Regular.Size16.Folder();

// In Razor:
<FluentIcon Value="@_iconFolder" Width="14px" />
```

---

## 🚀 **Blazor Performance Tips**

### Component Rendering
1. **Use `@key`** on repeated elements to prevent unnecessary re-renders
2. **Cache static objects** (icons, strings, etc.) as `static readonly`
3. **Use `ShouldRender()`** to prevent unnecessary renders
4. **Avoid inline functions** in render tree (they cause re-renders on every parent render)

### Virtualization
- Use `Virtualize` component for large lists
- Set appropriate `ItemSize` and `OverscanCount`
- Use flat data structures for O(1) access

### CSS Performance
- Use `transform` and `opacity` for animations (GPU accelerated)
- Avoid `margin`/`padding` animations (cause layout recalculations)
- Use `will-change: transform` for elements that will be animated

---

## 📋 **Implementation Workflow**

### Before Making Changes
- [ ] **Read** the existing code
- [ ] **Understand** the component hierarchy
- [ ] **Check** for existing patterns (how icons are used elsewhere?)
- [ ] **Verify** CSS isolation scope

### After Making Changes
- [ ] **Build** to catch compilation errors
- [ ] **Test** the specific feature manually
- [ ] **Check** for regressions in other features
- [ ] **Validate** accessibility (keyboard nav, screen readers)

### Before Committing
- [ ] **Run build** - must compile without errors
- [ ] **Run tests** - all existing tests must pass
- [ ] **Manual smoke test** - verify in UI
- [ ] **Check performance** - no regressions

---

## ❌ **Mistakes to Avoid**

| Mistake | Impact | Solution |
|--------|--------|----------|
| Using `@import` in scoped CSS | Build error | Use global CSS for imports |
| Using wrong type for icons | Compilation error | Use `Microsoft.FluentUI.AspNetCore.Components.Icon` |
| Not testing scoped CSS tokens | Styles don't apply | Reference tokens directly in scoped CSS |
| Forgetting CSS isolation | Styles leak to other components | Use proper scoping |
| Hardcoding colors | No theming support | Use CSS variables |
| Not caching icons | Performance issues | Use `static readonly` for icons |
| Wrong CSS specificity | Styles don't override | Use proper specificity hierarchy |

---

## ✅ **Success Patterns**

### Pattern 1: Token-Based Styling
```css
/* Global tokens */
:root {
    --api-method-get: #3b82f6;
    --api-method-post: #22c55e;
}

/* Scoped CSS using tokens */
.collection-tree__method-badge--get {
    background: color-mix(in srgb, var(--api-method-get) 15%, transparent);
    color: var(--api-method-get);
}
```

### Pattern 2: Icon Caching
```csharp
// Cache icons once
private static readonly Icon _iconFolder = new Icons.Regular.Size16.Folder();
private static readonly Icon _iconChevronDown = new Icons.Regular.Size16.ChevronDown();
```

### Pattern 3: Depth-Based Indentation
```css
.collection-tree__row {
    --depth: @node.Depth;
    padding-left: calc(var(--api-tree-indent) + var(--api-tree-indent) * var(--depth, 0));
}

.collection-tree__row::before {
    content: '';
    position: absolute;
    left: 0;
    top: 0;
    bottom: 0;
    width: calc(var(--api-tree-indent) * var(--depth, 0));
    border-left: 1px dashed var(--api-border-color-subtle);
    pointer-events: none;
}
```

### Pattern 4: Global CSS Imports
```css
/* app.css - Main entry point */
@import url("Components/ApiClient/Styles/00-api-tokens.css");
@import url("Components/ApiClient/Styles/01-api-base.css");
@import url("Components/ApiClient/Styles/02-api-components.css");
```

---

## 📚 **Reference: File Locations**

### API Client Components
- **Main**: `D:\Projects\SwebKit\src\SwebKit.App\Components\ApiClient\`
- **Tree**: `CollectionTree.razor` and `CollectionTree.razor.css`
- **Page**: `ApiClientPage.razor`

### Styles
- **Global**: `D:\Projects\SwebKit\src\SwebKit.App\wwwroot\app.css`
- **Tokens**: `D:\Projects\SwebKit\src\SwebKit.App\wwwroot\css\Styles\00-api-tokens.css`
- **Base**: `D:\Projects\SwebKit\src\SwebKit.App\wwwroot\css\Styles\01-api-base.css`
- **Components**: `D:\Projects\SwebKit\src\SwebKit.App\wwwroot\css\Styles\02-api-components.css`

### Documentation
- **Roadmap**: `D:\Projects\SwebKit\docs\api-client-ui-improvements.md`
- **This file**: `D:\Projects\SwebKit\.vibe\memory\api-client-lessons.md`

---

## 🎓 **Key Takeaways**

1. **Blazor CSS isolation is REAL** - Scoped CSS takes precedence and cannot use `@import`
2. **Global tokens work in scoped CSS** - Just reference them with `var(--token-name)`
3. **Type matters for Fluent UI** - Icons must be `Microsoft.FluentUI.AspNetCore.Components.Icon`
4. **Always test the build** - Catch compilation errors early
5. **Document lessons learned** - Avoid repeating the same mistakes
6. **Incremental changes** - Fix one thing at a time, validate, then move to next

---

**Last Updated**: 2025-06-27  
**Next Review**: After Phase 2 completion
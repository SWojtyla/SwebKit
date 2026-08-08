---
name: Testing SwebKit API Client UI
description: How to end-to-end test the SwebKit API Client UI features (Environment Manager, variables, collection import, request actions, JSONPath picker) in the Vite + .NET sidecar dev environment.
---

# Testing SwebKit API Client UI

## Devin secrets needed

None.

## One-time environment

- Use .NET 10 at `~/.dotnet`:
  ```bash
  export PATH="$HOME/.dotnet:$PATH"
  export DOTNET_CLI_TELEMETRY_OPTOUT=1
  ```
- Use Node v22.12.0 via nvm:
  ```bash
  source /home/ubuntu/.nvm/nvm.sh && nvm use 22
  ```

## Starting the app

1. Sidecar:
   ```bash
   cd /home/ubuntu/repos/SwebKit/src-sidecar
   dotnet run --project SwebKit.Sidecar.csproj
   ```
   Defaults to `http://127.0.0.1:5199`.

2. Web dev server:
   ```bash
   cd /home/ubuntu/repos/SwebKit/web
   npm run dev
   ```
   Vite serves on `http://localhost:1420`.

3. Run Playwright tests:
   ```bash
   cd /home/ubuntu/repos/SwebKit/web
   npx playwright test e2e/api-client.spec.ts --project chromium
   ```
   Tests use `PLAYWRIGHT_SIDECAR_PORT` (default 5198) and `PLAYWRIGHT_VITE_PORT` (default 1419) and start their own servers.

## Useful API Client data-testids

- Global: `nav-api-client`, `env-selector`, `env-manager-button`
- Environment Manager: `env-manager`, `env-manager-resize-handle`, `env-manager-close`, `env-save-all`, `env-add-button`, `env-add-variable`
- Variable editor: `env-var-key-{i}`, `env-var-source-{i}`, `env-var-value-{i}`, `env-var-vault-{i}`, `env-var-0-generator-kind`
- Collection tree: `add-collection-button`, `add-request-button`, `collection-import-button`, `collection-tree`
- Collection import: `collection-import-dialog`, `collection-import-file-btn`, `collection-import-bruno-btn`, `collection-import-close`
- Request editor: `request-url-input`, `request-send-button`, `request-save-button`, `request-tab-capture`, `request-tab-actions`
- Request actions: `add-postRequestActions`, `postRequestActions-name-{i}`, `postRequestActions-kind-{i}`, `postRequestActions-source-{i}`
- Capture rules: `add-capture-rule`, `capture-rule-target-{i}`, `capture-rule-picker-{i}`, `capture-rule-path-{i}`
- JSONPath picker: `jsonpath-picker-dialog`, `jsonpath-picker-body`, `jsonpath-picker-input`, `jsonpath-picker-evaluate`, `jsonpath-picker-preview`, `jsonpath-picker-select`
- Response: `response-tab-history`, `response-status`

## Known gotchas

- Many small toolbar buttons and native `<select>` dropdowns do not register native `computer` mouse clicks reliably. Use the browser console with a native `value` setter and dispatch `input`/`change` events for React-controlled fields. Example helper:

  ```js
  function setNativeValue(el, value) {
    const tag = el.tagName;
    let proto;
    if (tag === 'SELECT') proto = window.HTMLSelectElement.prototype;
    else if (tag === 'TEXTAREA') proto = window.HTMLTextAreaElement.prototype;
    else proto = window.HTMLInputElement.prototype;
    Object.getOwnPropertyDescriptor(proto, 'value').set.call(el, value);
    el.dispatchEvent(new Event(tag === 'SELECT' ? 'change' : 'input', { bubbles: true }));
  }
  ```

- The `EnvironmentManager` resize handle (`env-manager-resize-handle`) uses pointer events. If mouse dragging is unreliable, dispatch `PointerEvent('pointerdown')`, `pointermove`, and `pointerup` from the browser console.

- Collection import uses a hidden `<input type="file">` via `pickFileWithContent`. The OS file chooser is not automatable in this harness. For manual verification, either:
  - Let Playwright cover import, or
  - POST the base64 payload to `/api/config/collections/import` and reload `/api-client` to confirm the collection appears in the tree.

- `CopyToClipboard` post-request actions trigger the browser's clipboard permission prompt on first use. Allow it, then verify with `navigator.clipboard.readText()`.

- Azure Key Vault settings inputs update on every keystroke through a TanStack mutation. Setting values too fast may race with the query invalidation; wait briefly and verify with `outerHTML`/`value`.

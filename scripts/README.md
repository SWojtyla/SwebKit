# Scripts

All repo scripts live here, split by which app they belong to. SwebKit is mid-migration:
the **MAUI** app (`src/SwebKit.App`, MSIX) is the legacy shipping app; the **Tauri**
app (`src-tauri` + `web` + `src-sidecar`, MSI/NSIS) is the rewrite. The two have
completely separate build and packaging chains — nothing is shared between them.

```
scripts/
├── maui/                  legacy .NET MAUI app (MSIX, self-signed sideload)
│   ├── install.ps1        build + sign + trust + install + launch  (the MAUI installer)
│   └── style-inventory.ps1  Razor/CSS style audit for the MAUI Blazor UI
├── tauri/                 Tauri + React + .NET sidecar app (MSI/NSIS)
│   ├── build-msi.ps1      fresh clean build -> installer
│   ├── test-frontend.ps1  fresh production frontend + published sidecar, in the browser
│   ├── run-dev.ps1        3-tier dev launcher with hot reload
│   ├── run-dev.cmd        same, double-clickable from Explorer
│   └── _common.ps1        shared helpers (dot-sourced; not run directly)
└── logs/                  script output, gitignored
```

Run everything from a full clone; the scripts locate the repo root themselves, so the
working directory does not matter.

## MAUI (legacy)

```powershell
pwsh -File scripts/maui/install.ps1
```

One command: generates a local `CN=SwebKit` signing certificate, syncs the csproj
thumbprint, bumps the package version so Windows treats the install as an upgrade
(preserving your config), publishes the Release MSIX, trusts the certificate (one UAC
prompt per machine), installs, and launches. Idempotent — safe to re-run after a pull.
Flags: `-SkipInstall`, `-NoLaunch`.

Full detail: [docs/packaging-and-install.md](../docs/packaging-and-install.md).

## Tauri

### Fresh installer

```powershell
pwsh -File scripts/tauri/build-msi.ps1
```

Cleans the previous output, then runs the four steps in the only order that works:
`npm install` + `npm run build` → `dotnet publish` the sidecar (self-contained, into
`src-tauri/binaries/sidecar`) → `tauri build --bundles msi`. Verifies the sidecar `.exe`
actually landed — that folder is gitignored and bundled as a resource, so a stale or
empty one produces a valid installer whose installed app dies with "Sidecar binary not
found".

Output: `src-tauri/target/release/bundle/msi/SwebKit_<version>_x64_en-US.msi`.
Transcript: `scripts/logs/build-msi.log`.

| Flag                    | Effect                                                          |
| ----------------------- | --------------------------------------------------------------- |
| `-Bundles msi\|nsis\|all` | Installer formats to produce (default `msi`)                     |
| `-SkipNpmInstall`       | Build with `node_modules` as-is                                  |
| `-NoClean`              | Incremental: keep the previous `dist`/sidecar/bundle output      |
| `-FullClean`            | Also `cargo clean` for a true from-scratch build (much slower)   |
| `-Install`              | Launch the produced MSI with `msiexec` on success               |

Mirrors [.github/workflows/release.yml](../.github/workflows/release.yml) — keep the two
in sync.

### Test the frontend against the sidecar (fresh build, no installer)

```powershell
pwsh -File scripts/tauri/test-frontend.ps1
```

Rebuilds both production artifacts from scratch — the vite bundle and the published
sidecar `.exe` — starts the sidecar on `127.0.0.1:5199`, serves `web/dist` statically on
`127.0.0.1:1421`, and opens a browser. Ctrl+C stops both. Use it to verify a change
against the real bundle without paying for a full installer build.

The sidecar's `%AppData%\SwebKit` is redirected to a throwaway `scripts/.test-appdata`
by default, so a test run cannot damage your saved profiles, templates or monitoring
rules. Tauri-native features (secret storage, native dialogs, shell) don't exist in a
plain browser; everything that talks to the sidecar over HTTP behaves identically.

| Flag                     | Effect                                                        |
| ------------------------ | ------------------------------------------------------------- |
| `-Port <n>`              | Preview port (default 1421, clear of the dev server's 1420)   |
| `-SidecarPort <n>`       | Sidecar port (default 5199)                                   |
| `-UseRealAppData`        | Use your real config instead of a throwaway folder            |
| `-FrameworkDependent`    | Faster sidecar publish; no longer what the installer ships    |
| `-SkipNpmInstall`        | Build with `node_modules` as-is                               |
| `-NoBrowser`             | Print the URL instead of opening a browser                    |

### Dev loop (hot reload)

```powershell
pwsh -File scripts/tauri/run-dev.ps1     # or double-click run-dev.cmd
```

Starts the sidecar (`dotnet run`, port 5199), the Vite dev server (port 1420) and the
Tauri window, each in its own console, skipping any tier that is already up. Logs:
`scripts/logs/{sidecar,vite,tauri}.log`.

### End-to-end tests

Playwright starts its own sidecar and Vite on isolated ports — no script needed:

```powershell
cd web
npx playwright test
```

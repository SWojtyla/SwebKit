# Scripts & Build Tooling Pitfalls

Recurring traps in `scripts/tauri/` (dev launcher, MSI build) and the frontend build they drive.
Add an entry whenever a bug here costs more than one debugging session.

## PowerShell scripts must be pure ASCII

`run-dev.cmd` / `stop-dev.cmd` fall back to Windows PowerShell 5.1 (`powershell.exe`) when `pwsh`
is not installed, which is the default on a fresh Windows machine. 5.1 reads a BOM-less `.ps1` as
the system ANSI codepage (cp1252), not UTF-8. An em-dash `—` is UTF-8 `E2 80 94`, and cp1252 `0x94`
is `”`, which PowerShell treats as a **string delimiter**. An em-dash inside a double-quoted string
therefore ends the string early, and the whole script fails to parse ("Missing closing '}'").
It works fine under `pwsh` 7, so it slips through review.

**Keep every `.ps1` in `scripts/` ASCII-only**, comments included. Use `-` and `...`, not `—` and
`…`. To check a script without running it:

```powershell
$e = $null; [void][System.Management.Automation.Language.Parser]::ParseFile('scripts\tauri\run-dev.ps1', [ref]$null, [ref]$e); $e
```

Run that in `powershell.exe`, not `pwsh`.

## `npm run build` type-checks test files, vitest does not

`npm run build` is `tsc -b && vite build`, and `web/tsconfig.json` includes all of `src/`, so
`*.test.ts` files are type-checked by the build. Vitest strips types without checking them, so a
test can pass under `npm run test:unit` and still break `build-msi.ps1`.

Typical case: `await apiFetch("/x").catch((e) => e)` infers `T = unknown`, so `err` is `unknown`
and every `err.message` is TS18046. Pin the types: `apiFetch<never>("/x").catch((e: ApiError) => e)`.
Run `npx tsc -b` in `web/` before pushing test changes.

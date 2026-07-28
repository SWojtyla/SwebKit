import { defineConfig, devices } from "@playwright/test";
import { fileURLToPath } from "node:url";
import path from "node:path";

/**
 * Playwright E2E configuration for SwebKit.
 *
 * The tests spin up the .NET sidecar and the Vite dev server automatically on
 * isolated ports so they do not collide with a developer's running instances.
 */

/**
 * The sidecar persists profiles, message templates, monitoring rules and the
 * scheduled-message store under %APPDATA%\SwebKit. Without an override the e2e
 * run would read — and write — the developer's real configuration: the suite
 * saves templates, sends messages and edits settings. `SWEBKIT_APPDATA_ROOT`
 * (see AppDataPaths) redirects all of that into a throwaway folder.
 */
const e2eAppDataRoot = path.resolve(
  path.dirname(fileURLToPath(import.meta.url)),
  ".e2e-appdata",
);

/**
 * Sidecar/Vite ports default to the values every dev has always used, but can be
 * overridden so multiple checkouts (e.g. parallel worktrees) can run the suite
 * at the same time without binding the same two ports.
 */
const sidecarPort = process.env.PLAYWRIGHT_SIDECAR_PORT ?? "5198";
const vitePort = process.env.PLAYWRIGHT_VITE_PORT ?? "1419";

export default defineConfig({
  testDir: "./e2e",
  // The first navigation of a run pays for Vite's cold compile of the whole app,
  // which can exceed the 30s default on a cold cache and fail an otherwise
  // healthy test.
  timeout: 60 * 1000,
  fullyParallel: false,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 2 : 0,
  workers: 1,
  reporter: "list",
  use: {
    baseURL: `http://localhost:${vitePort}`,
    trace: "on-first-retry",
    screenshot: "only-on-failure",
  },

  projects: [
    {
      name: "chromium",
      use: { ...devices["Desktop Chrome"] },
    },
  ],

  webServer: [
    {
      command: `dotnet run --project ../src-sidecar/SwebKit.Sidecar.csproj --urls http://127.0.0.1:${sidecarPort}`,
      url: `http://127.0.0.1:${sidecarPort}/health`,
      timeout: 120 * 1000,
      reuseExistingServer: false,
      env: { SWEBKIT_APPDATA_ROOT: e2eAppDataRoot },
    },
    {
      command: `cross-env VITE_SIDECAR_URL=http://127.0.0.1:${sidecarPort} npx vite --port ${vitePort}`,
      url: `http://localhost:${vitePort}`,
      timeout: 60 * 1000,
      reuseExistingServer: false,
    },
  ],
});

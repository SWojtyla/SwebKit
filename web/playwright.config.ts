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
    baseURL: "http://localhost:1419",
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
      command: "dotnet run --project ../src-sidecar/SwebKit.Sidecar.csproj --urls http://127.0.0.1:5198",
      url: "http://127.0.0.1:5198/health",
      timeout: 120 * 1000,
      reuseExistingServer: false,
      env: { SWEBKIT_APPDATA_ROOT: e2eAppDataRoot },
    },
    {
      command: "cross-env VITE_SIDECAR_URL=http://127.0.0.1:5198 npx vite --port 1419",
      url: "http://localhost:1419",
      timeout: 60 * 1000,
      reuseExistingServer: false,
    },
  ],
});

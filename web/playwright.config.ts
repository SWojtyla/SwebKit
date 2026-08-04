import { defineConfig, devices } from "@playwright/test";
import { e2eAppDataRoot, sidecarPort, vitePort } from "./e2e/test-config";

/**
 * Playwright E2E configuration for SwebKit.
 *
 * The tests spin up the .NET sidecar and the Vite dev server automatically on
 * isolated ports so they do not collide with a developer's running instances.
 *
 * The throwaway `.e2e-appdata` directory and any leftover sidecar process are
 * handled by `e2e/global-setup.ts` and `e2e/global-teardown.ts`, so the config
 * itself does not try to delete a directory that may still be locked by the
 * previous sidecar run.
 */

export default defineConfig({
  testDir: "./e2e",
  globalSetup: "./e2e/global-setup.ts",
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

  webServer: {
    command: `cross-env VITE_SIDECAR_URL=http://127.0.0.1:${sidecarPort} npx vite --port ${vitePort}`,
    url: `http://localhost:${vitePort}`,
    timeout: 60 * 1000,
    reuseExistingServer: false,
  },
});

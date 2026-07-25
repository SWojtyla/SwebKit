import { defineConfig, devices } from "@playwright/test";

/**
 * Playwright E2E configuration for SwebKit.
 *
 * The tests spin up the .NET sidecar and the Vite dev server automatically on
 * isolated ports so they do not collide with a developer's running instances.
 */
export default defineConfig({
  testDir: "./e2e",
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
    },
    {
      command: "cross-env VITE_SIDECAR_URL=http://127.0.0.1:5198 npx vite --port 1419",
      url: "http://localhost:1419",
      timeout: 60 * 1000,
      reuseExistingServer: false,
    },
  ],
});

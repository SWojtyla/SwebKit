import type { ChildProcess } from "node:child_process";
import { resetE2EAppData, startSidecar, stopSidecar } from "./test-config";

/**
 * Playwright global setup.
 *
 * Kills any leftover sidecar, resets the throwaway `.e2e-appdata` directory,
 * starts the .NET sidecar, and returns a teardown that stops the sidecar once
 * all tests finish. The sidecar is no longer managed by `playwright.config.ts`'s
 * `webServer` so we avoid races between setup and the sidecar startup.
 */
export default async function globalSetup() {
  await resetE2EAppData();
  const sidecar: ChildProcess = await startSidecar();
  return async () => {
    stopSidecar(sidecar);
  };
}

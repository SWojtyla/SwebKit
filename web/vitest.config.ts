import { defineConfig } from "vitest/config";
import path from "path";

/// Unit tests for the pure modules under `src/lib` and the API Client's
/// presentation logic. Component behaviour is covered by Playwright in `e2e/`,
/// which exercises it against the real app — this runner deliberately needs no
/// DOM shim.
export default defineConfig({
  resolve: {
    alias: {
      "@": path.resolve(__dirname, "./src"),
    },
  },
  test: {
    environment: "node",
    include: ["src/**/*.test.ts"],
    // Playwright owns `e2e/`; without this, vitest tries to collect its specs.
    exclude: ["e2e/**", "node_modules/**", "dist/**"],
  },
});

import js from "@eslint/js";
import globals from "globals";
import reactHooks from "eslint-plugin-react-hooks";
import reactRefresh from "eslint-plugin-react-refresh";
import tseslint from "typescript-eslint";

export default tseslint.config(
  { ignores: ["dist", "playwright-report", "test-results", "src-tauri", "node_modules"] },
  {
    extends: [js.configs.recommended, ...tseslint.configs.recommended],
    files: ["**/*.{ts,tsx}"],
    languageOptions: {
      ecmaVersion: 2022,
      globals: globals.browser,
    },
    plugins: {
      "react-hooks": reactHooks,
      "react-refresh": reactRefresh,
    },
    rules: {
      ...reactHooks.configs.recommended.rules,

      // The point of adding ESLint here: a missing dependency silently produces a
      // stale closure, which is exactly the class of bug the context memoization
      // work could otherwise introduce. Keep this an error.
      "react-hooks/exhaustive-deps": "error",
      "react-hooks/rules-of-hooks": "error",

      // react-hooks v7 ships the React Compiler diagnostics. They surface real
      // smells, but they fire ~90 times on code that predates them and several are
      // idiomatic-but-flagged (assigning a callback ref during render, for one).
      // Warnings keep the signal visible without making `npm run lint` useless as a
      // gate; promote them individually as the code is cleaned up.
      "react-hooks/refs": "warn",
      "react-hooks/set-state-in-effect": "warn",
      "react-hooks/purity": "warn",
      "react-hooks/immutability": "warn",
      "react-hooks/static-components": "warn",
      "react-hooks/incompatible-library": "warn",
      "react-hooks/preserve-manual-memoization": "warn",

      "react-refresh/only-export-components": ["warn", { allowConstantExport: true }],

      // `_`-prefixed args are the established way to mark a deliberately unused
      // parameter that must stay for positional reasons.
      "@typescript-eslint/no-unused-vars": [
        "error",
        { argsIgnorePattern: "^_", varsIgnorePattern: "^_", caughtErrors: "none" },
      ],
    },
  },
  {
    // Node globals, and test files legitimately reach for non-null assertions on
    // fixtures they just created.
    files: ["e2e/**/*.ts", "*.config.{ts,js}"],
    languageOptions: { globals: globals.node },
  },
  {
    // Tests poke at deliberately malformed payloads, where `any` is the point.
    files: ["e2e/**/*.ts", "**/*.test.ts", "**/*.test.tsx"],
    rules: { "@typescript-eslint/no-explicit-any": "off" },
  },
);

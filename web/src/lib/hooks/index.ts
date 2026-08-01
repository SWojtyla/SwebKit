// Barrel re-exporting every domain's React Query hooks so existing
// `import { useX } from "@/lib/hooks"` call sites keep working unchanged.
// See docs/features/active/tauri-react-primary-tool/technical-plan.md
// (Module 2, 2.4) for the rationale behind this per-domain split.

export * from "./useProfile";
export * from "./useServiceBus";
export * from "./useAks";
export * from "./useRedis";
export * from "./useStorage";
export * from "./useApiClient";
export * from "./useAgent";
export * from "./useMonitoring";
export * from "./useCommandPalette";

// Re-exported from the original hooks.ts for callers that imported it from
// "@/lib/hooks" instead of "@/lib/useNotifyMutation" directly.
export { useNotifyMutation } from "../useNotifyMutation";

import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import tailwindcss from "@tailwindcss/vite";
import path from "path";

export default defineConfig({
    plugins: [react(), tailwindcss()],
    resolve: {
        alias: {
            "@": path.resolve(import.meta.dirname, "./src"),
        },
    },
    clearScreen: false,
    server: {
        port: 1420,
        strictPort: true,
        watch: {
            // The e2e sidecar writes its throwaway appdata (collections.json, …) under
            // web/ while tests run — each write used to trip Vite's watcher and force a
            // full page reload mid-test, wiping in-memory state and aborting in-flight
            // navigations (ERR_ABORTED).
            ignored: ["**/.e2e-appdata*/**"],
        },
    },
    envPrefix: ["VITE_", "TAURI_"],
    build: {
        target: "es2022",
        sourcemap: false,
        rollupOptions: {
            output: {
                // Split the heaviest third-party deps out of the app chunk so no single
                // file trips Vite's 500 kB warning and the browser can cache them apart
                // from application code. Vite 8 (Rolldown) only accepts the function form.
                manualChunks(id) {
                    if (!id.includes("node_modules")) return;
                    if (
                        /[\\/]node_modules[\\/](react|react-dom|react-router)[\\/]/.test(
                            id,
                        )
                    )
                        return "react";
                    if (id.includes("node_modules/@tanstack/react-query/"))
                        return "query";
                    if (id.includes("node_modules/lucide-react/"))
                        return "icons";
                },
            },
        },
    },
});

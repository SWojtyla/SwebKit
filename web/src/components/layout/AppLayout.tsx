import { useState, useEffect, useCallback, useRef } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { NavLink, Outlet, useNavigate, useLocation } from "react-router";
import {
    LayoutDashboard,
    MessageSquare,
    Ship,
    Code2,
    Database,
    Table2,
    FolderOpen,
    Bot,
    Settings,
    Search,
    Circle,
    Activity,
    PanelLeftClose,
    PanelLeftOpen,
    Sun,
    Moon,
    Layers,
    Beaker,
    Keyboard,
    Clock,
    AlertTriangle,
} from "lucide-react";
import { CommandPalette } from "./CommandPalette";
import { PinnedRail } from "./PinnedRail";
import { setNotificationBellSlot } from "./notification-bell-slot";
import { EnvironmentBadge } from "./EnvironmentBadge";
import { KeyboardShortcutsPanel } from "./KeyboardShortcutsPanel";
import { GlobalAgentPanel } from "@/components/agent/GlobalAgentPanel";
import { DemoTour } from "./DemoTour";
import {
    useDemoMode,
    useHealth,
    useProfile,
    useToggleDemoMode,
    useUserSettings,
    useUpdateUserSettings,
    useWorkspaceWarmup,
} from "@/lib/hooks";
import { classifyEnvironment, environmentBadgeTitle } from "@/lib/env-badge";
import {
    loadViewPreference,
    saveViewPreference,
} from "@/lib/stores/panel-preferences";
import { notifyScreenRouteChanged } from "@/lib/stores/screen-state";
import { initDeepLinks, type DeepLinkContext } from "@/lib/deep-links";
import { useSettingsStore, migrateTheme } from "@/lib/stores/settings";
import { useAgentPanelStore } from "@/lib/stores/agent-panel";
import { useMonitoringStream } from "@/lib/hooks/useMonitoring";
import {
    onSidecarLifecycleEvent,
    restartSidecar,
    showNotification,
} from "@/lib/tauri-bridge";
import { initSidecarBaseUrl } from "@/lib/api";
import { useNotification } from "./notification-context";
import { ActivityIndicator } from "@/components/shared/ActivityIndicator";
import { useServiceHealth } from "@/components/dashboard/useServiceHealth";
import {
    localTimeZoneAbbrev,
    localTimeZoneName,
    localUtcOffsetLabel,
} from "@/lib/datetime";

const navItems = [
    { to: "/", label: "Dashboard", icon: LayoutDashboard, end: true },
    { to: "/service-bus", label: "Service Bus", icon: MessageSquare },
    { to: "/aks", label: "AKS", icon: Ship },
    { to: "/api-client", label: "API Client", icon: Code2 },
    { to: "/redis", label: "Redis", icon: Database },
    { to: "/sql", label: "SQL", icon: Table2 },
    { to: "/storage", label: "Storage", icon: FolderOpen },
    { to: "/agent", label: "AI Agent", icon: Bot },
    { to: "/monitoring", label: "Monitoring", icon: Activity },
    { to: "/settings", label: "Settings", icon: Settings },
];

export function AppLayout() {
    const [paletteOpen, setPaletteOpen] = useState(false);
    const [shortcutsOpen, setShortcutsOpen] = useState(false);
    const [navCollapsed, setNavCollapsed] = useState(false);
    // Shared store, not local state: the dashboard command bar (and anything else)
    // can dock the panel open without owning this component's internals.
    const agentPanelOpen = useAgentPanelStore((s) => s.open);
    const setAgentPanelOpen = useAgentPanelStore((s) => s.setOpen);
    const toggleAgentPanel = useAgentPanelStore((s) => s.toggle);
    const navigate = useNavigate();
    const location = useLocation();
    const onAgentPage = location.pathname === "/agent";

    // The full /agent page and this panel are two views onto the identical global conversation
    // (see useGlobalAgentConversation) — showing both at once would just be the same messages
    // twice, so the panel auto-closes whenever the user navigates to the dedicated page instead.
    useEffect(() => {
        if (onAgentPage) setAgentPanelOpen(false);
    }, [onAgentPage, setAgentPanelOpen]);

    // Screen-state route trigger (agent-workspace-awareness M1): provider-less pages still get
    // a route+title snapshot published on navigation; provider pages republish their own.
    useEffect(() => {
        notifyScreenRouteChanged();
    }, [location.pathname]);

    const { data: health } = useHealth();
    const { data: demoData } = useDemoMode();
    // Environment badge (ux-power-pack §3): one classification drives the top-bar
    // pill, the status-bar label and — for PRD — the banner under the header.
    const { data: profile } = useProfile();
    const env = profile ? classifyEnvironment(profile.config) : null;
    const serviceHealth = useServiceHealth();
    const toggleDemoMode = useToggleDemoMode();
    const { theme, toggleTheme, setTheme } = useSettingsStore();
    const { data: userSettings } = useUserSettings();
    const updateUserSettings = useUpdateUserSettings();
    useWorkspaceWarmup();

    // Route restore: capture the stored route lazily at mount — before the save
    // effect below can overwrite it with "/" on this very launch — then navigate
    // back to it once settings resolve, but only while still sitting on "/"
    // (any explicit navigation away means the user already chose a page).
    const [initialRoute] = useState(() =>
        loadViewPreference<string>("last-route", ""),
    );
    const restoredRef = useRef(false);
    useEffect(() => {
        if (restoredRef.current || !userSettings) return;
        restoredRef.current = true;
        if (userSettings.restoreLastWorkspaceOnStartup === false) return;
        if (location.pathname !== "/") return;
        if (initialRoute && initialRoute !== "/") {
            navigate(initialRoute, { replace: true });
        }
    }, [userSettings, location.pathname, navigate, initialRoute]);

    useEffect(() => {
        saveViewPreference("last-route", location.pathname + location.search);
    }, [location.pathname, location.search]);

    // Theme lives in the sidecar's user-settings.json, not just this session's Zustand store —
    // without this, a restart always came back to the "dark" default no matter what was picked.
    // Applied once per app launch, on the first settings load; later local changes push the other
    // direction (see the theme toggle button and AppearanceSettings' card clicks).
    const themeHydratedRef = useRef(false);
    useEffect(() => {
        if (themeHydratedRef.current || !userSettings) return;
        themeHydratedRef.current = true;
        // Retired theme ids migrate to the nearest Aurora mode — a stale
        // user-settings.json should land somewhere sensible, not the default.
        const migrated = migrateTheme(userSettings.theme);
        if (!migrated) return;
        if (migrated !== theme) setTheme(migrated);
        if (migrated !== userSettings.theme) {
            updateUserSettings.mutate((prev) => ({ ...prev, theme: migrated }));
        }
    }, [userSettings, theme, setTheme, updateUserSettings]);

    // Apply font-size and density from user settings. The root font-size is a simple rem scaler;
    // density is surfaced as a data attribute for future CSS and for select controls to reflect.
    useEffect(() => {
        if (!userSettings) return;
        const fontSizeMap: Record<string, string> = {
            small: "14px",
            medium: "16px",
            large: "18px",
        };
        document.documentElement.style.fontSize =
            fontSizeMap[userSettings.fontSize] ?? "16px";
        document.documentElement.dataset.density =
            userSettings.density ?? "comfortable";
    }, [userSettings]);
    const sidecarOk = health?.status === "ok";
    const isDemoMode = demoData?.isDemoMode ?? false;
    const [reconnecting, setReconnecting] = useState(false);
    const queryClient = useQueryClient();
    const { notify } = useNotification();

    // Always-mounted monitoring subscription (agent-workspace-awareness M3): OS notifications for
    // fired alerts and completed proactive investigations must reach the user while the app is
    // minimized or while they sit on a page that never subscribes to the stream itself. This is
    // the single notification site — page-level subscriptions feed their own feeds and
    // deliberately don't toast, so an event can't double-notify.
    useMonitoringStream(
        (evt) => {
            // A firing suppressed by a silence window or rule mute is audit info only
            // (monitoring-closed-loop item 3): quiet in-app toast, no OS notification —
            // making noise here would defeat the point of silencing.
            if (evt.suppressed) {
                notify(
                    "info",
                    `${evt.ruleName} — silenced`,
                    evt.suppressedBy
                        ? `${evt.message} (${evt.suppressedBy})`
                        : evt.message,
                    undefined,
                    "/monitoring",
                );
                return;
            }
            void showNotification(evt.ruleName, evt.message);
            notify(
                evt.severity === "Critical" ? "error" : "success",
                evt.ruleName,
                evt.message,
                undefined,
                "/monitoring",
            );
        },
        (insight) => {
            void showNotification(
                "Investigation ready",
                `${insight.ruleName} — ${insight.summary.slice(0, 200)}`,
            );
            notify(
                "info",
                "Investigation ready",
                `${insight.ruleName} — ${insight.summary.slice(0, 120)}`,
                undefined,
                "/monitoring",
            );
        },
        undefined,
        // Terminal investigation outcomes toast too — a fired alert that produced no insight
        // was previously completely silent (resource not on the Map, AI disabled, no
        // tool-calling profile), which read as "monitoring is broken". "Started" is skipped:
        // the running card on the Monitoring page covers the in-flight state.
        (status) => {
            if (status.stage === "Started") return;
            notify(
                status.stage === "Failed" ? "error" : "info",
                `AI investigation ${status.stage === "Skipped" ? "skipped" : "failed"}`,
                `${status.ruleName}${status.reason ? ` — ${status.reason}` : ""}`,
                undefined,
                "/monitoring",
            );
        },
        undefined,
        // An opted-in investigation parked a remediation proposal (monitoring-closed-loop 1c) —
        // invalidate the approvals list instantly so the pending card shows without waiting for
        // the 30s poll or the finished report, and toast "AI proposes X" so it surfaces even when
        // nobody is watching the monitoring page.
        (proposal) => {
            queryClient.invalidateQueries({ queryKey: ["pending-approvals"] });
            notify(
                "info",
                `AI proposes a fix — ${proposal.ruleName}`,
                proposal.summary,
                undefined,
                "/monitoring?tab=reports",
            );
        },
    );

    // The sidecar previously had no recovery path if it crashed mid-session: `restart_sidecar`
    // existed as a Tauri command but nothing ever called it, so a crash silently broke the app
    // until the user manually relaunched. `useHealth`'s 10s poll already detects the outage
    // (`sidecarOk` goes false); this adds the missing recovery action.
    const handleReconnect = useCallback(async () => {
        setReconnecting(true);
        try {
            const port = await restartSidecar();
            if (port !== null) {
                await initSidecarBaseUrl();
            }
            await queryClient.invalidateQueries();
            notify("success", "Reconnected to the sidecar");
        } catch (err) {
            notify(
                "error",
                "Reconnect failed",
                err instanceof Error ? err.message : String(err),
            );
        } finally {
            setReconnecting(false);
        }
    }, [queryClient, notify]);

    // Complements handleReconnect (manual, user-triggered) with the automatic side: the Rust side
    // now supervises the sidecar child process itself and respawns it on an unexpected exit without
    // waiting for the frontend's health poll to notice (see sidecar.rs's watch_for_crash) — this
    // just reflects that in the UI instead of requiring the user to notice "Disconnected" and click
    // Reconnect themselves, which was the whole gap: a crash used to be invisible here until someone
    // manually relaunched the entire app.
    useEffect(() => {
        let disposed = false;
        let unlisten: (() => void) | undefined;

        onSidecarLifecycleEvent({
            onCrashed: () => {
                notify(
                    "info",
                    "Sidecar disconnected",
                    "Attempting automatic recovery…",
                );
            },
            onRestarted: async (port) => {
                void port; // the Rust side already updated its own state; re-resolving here just syncs ours
                await initSidecarBaseUrl();
                await queryClient.invalidateQueries();
                notify("success", "Sidecar recovered automatically");
            },
            onRecoveryFailed: () => {
                notify(
                    "error",
                    "Sidecar recovery failed",
                    "Use the Reconnect button below, or restart the app.",
                );
            },
        }).then((dispose) => {
            if (disposed) dispose();
            else unlisten = dispose;
        });

        return () => {
            disposed = true;
            unlisten?.();
        };
    }, [queryClient, notify]);

    // `swebkit://` deep links (distribution-onboarding): the Tauri deep-link and
    // single-instance plugins emit URL events; cold-start links are queued Rust-side and
    // drained by initDeepLinks. Alias resolution reads the latest profile via the ref so
    // links arriving before (or after) a profile reload both resolve correctly.
    const deepLinkContextRef = useRef<DeepLinkContext>({});
    useEffect(() => {
        deepLinkContextRef.current = {
            serviceBusNamespaces: profile?.serviceBusNamespaces,
            sqlConnections: profile?.config.sqlConfig?.connections,
        };
    }, [profile]);

    useEffect(() => {
        let disposed = false;
        let unlisten: (() => void) | undefined;
        void initDeepLinks({
            navigate,
            notify: (message) => notify("info", "Deep link", message),
            context: () => deepLinkContextRef.current,
        }).then((dispose) => {
            if (disposed) dispose();
            else unlisten = dispose;
        });
        return () => {
            disposed = true;
            unlisten?.();
        };
    }, [navigate, notify]);

    const contextTitle =
        navItems.find((n) => n.to === location.pathname)?.label ?? "SwebKit";
    const areaHealth = (
        [
            { id: "service-bus", label: "Service Bus" },
            { id: "aks", label: "AKS" },
            { id: "redis", label: "Redis" },
            { id: "sql", label: "SQL" },
            { id: "storage", label: "Storage" },
        ] as const
    ).map((area) => ({
        ...area,
        state: serviceHealth[area.id]?.connectivity ?? "checking",
    }));

    // Spec §8: no footer — the status bar's contents fold into a top-bar
    // status popover. The trigger dot rolls every signal up to one colour.
    const [statusOpen, setStatusOpen] = useState(false);
    const statusRef = useRef<HTMLDivElement>(null);
    useEffect(() => {
        if (!statusOpen) return;
        const onDown = (e: MouseEvent) => {
            if (!statusRef.current?.contains(e.target as Node)) {
                setStatusOpen(false);
            }
        };
        const onKey = (e: KeyboardEvent) => {
            if (e.key === "Escape") setStatusOpen(false);
        };
        document.addEventListener("mousedown", onDown);
        document.addEventListener("keydown", onKey);
        return () => {
            document.removeEventListener("mousedown", onDown);
            document.removeEventListener("keydown", onKey);
        };
    }, [statusOpen]);

    const statusTone = !sidecarOk
        ? "bad"
        : areaHealth.some(
                (a) => a.state === "unavailable" || a.state === "degraded",
            )
          ? "bad"
          : areaHealth.some((a) => a.state === "checking")
            ? "warn"
            : "ok";

    const isDarkTheme = theme === "dark" || theme === "clay-dark";
    const themeLabel = {
        light: "Aurora Light",
        dark: "Aurora Dark",
        clay: "Clay Light",
        "clay-dark": "Clay Dark",
    }[theme];

    const handleKeyDown = useCallback(
        (e: KeyboardEvent) => {
            if ((e.ctrlKey || e.metaKey) && (e.key === "k" || e.key === "K")) {
                e.preventDefault();
                setPaletteOpen((prev) => !prev);
            } else if (
                (e.ctrlKey || e.metaKey) &&
                (e.key === "g" || e.key === "G")
            ) {
                e.preventDefault();
                navigate("/settings");
            } else if (
                (e.ctrlKey || e.metaKey) &&
                (e.key === "b" || e.key === "B")
            ) {
                e.preventDefault();
                setNavCollapsed((prev) => !prev);
            } else if (
                (e.ctrlKey || e.metaKey) &&
                e.shiftKey &&
                (e.key === "l" || e.key === "L")
            ) {
                // Not Ctrl+Shift+A: Chrome's built-in "Search tabs" shortcut swallows that combo before it
                // ever reaches page JS (confirmed empirically — the keydown event never fires at all), and
                // the packaged app's WebView2 shell is the same Chromium engine, so it would hit the same
                // wall for real users, not just in tests.
                e.preventDefault();
                if (!onAgentPage) toggleAgentPanel();
            } else if (
                ((e.key === "?" && e.shiftKey) ||
                    (e.key === "/" && e.shiftKey)) &&
                (e.target === document.body ||
                    e.target === document.documentElement)
            ) {
                e.preventDefault();
                setShortcutsOpen(true);
            }
        },
        [navigate, onAgentPage, toggleAgentPanel],
    );

    useEffect(() => {
        window.addEventListener("keydown", handleKeyDown);
        return () => window.removeEventListener("keydown", handleKeyDown);
    }, [handleKeyDown]);

    return (
        <div className="aurora-bg theme-transition flex h-screen w-screen overflow-hidden bg-background text-foreground">
            <aside
                className={`flex flex-col bg-sidebar transition-all duration-200 ${navCollapsed ? "w-14" : "w-[232px]"}`}
                style={{ borderRight: "var(--shell-sidebar-edge)" }}
            >
                <div className="flex h-12 items-center gap-2 px-3">
                    <div
                        className="flex h-5 w-5 shrink-0 items-center justify-center rounded-[var(--radius-sm)] bg-primary text-primary-foreground"
                        title="SwebKit"
                    >
                        <Layers className="h-3 w-3" />
                    </div>
                    {!navCollapsed && (
                        <span className="font-display text-base font-semibold tracking-tight">
                            SwebKit
                        </span>
                    )}
                    <button
                        onClick={() => setNavCollapsed(!navCollapsed)}
                        className="ml-auto rounded-[var(--radius-sm)] p-1 text-muted-foreground hover:bg-surface-2 hover:text-foreground"
                        data-testid="nav-collapse-toggle"
                    >
                        {navCollapsed ? (
                            <PanelLeftOpen className="h-4 w-4" />
                        ) : (
                            <PanelLeftClose className="h-4 w-4" />
                        )}
                    </button>
                </div>
                <nav className="flex-1 space-y-1 p-2">
                    {navItems.map(({ to, label, icon: Icon, end }) => (
                        <NavLink
                            key={to}
                            to={to}
                            end={end}
                            data-testid={`nav-${label.toLowerCase().replace(/\s+/g, "-")}`}
                            className={({ isActive }) =>
                                `flex h-8 items-center gap-2 rounded-md px-2 text-sm font-medium transition-all duration-200 ${
                                    navCollapsed ? "justify-center px-0" : ""
                                } ${
                                    isActive
                                        ? "bg-sidebar-active text-sidebar-active-foreground shadow-[var(--shell-navsel-shadow)]"
                                        : "text-sidebar-foreground hover:bg-surface-2 hover:text-foreground"
                                }`
                            }
                        >
                            <Icon className="h-4 w-4" />
                            {!navCollapsed && label}
                        </NavLink>
                    ))}
                </nav>
                <PinnedRail collapsed={navCollapsed} />
                {/* Docked notification bell: NotificationSystem portals the bell +
                    history popover into this footer slot so it shares layout with
                    the rail instead of floating over the last pinned item. */}
                <div
                    ref={setNotificationBellSlot}
                    className="flex shrink-0 justify-center border-t p-2"
                    data-testid="notification-bell-slot"
                />
            </aside>

            <div className="flex flex-1 flex-col overflow-hidden">
                {/* Top bar */}
                <header
                    className="glass flex h-12 items-center gap-2 px-3"
                    style={{ borderBottom: "var(--shell-topbar-edge)" }}
                    data-testid="top-bar"
                >
                    <span
                        className="text-sm font-semibold"
                        data-testid="context-title"
                    >
                        {contextTitle}
                    </span>
                    {env && <EnvironmentBadge env={env} />}
                    <button
                        onClick={() => setPaletteOpen(true)}
                        className="shell-search"
                        data-testid="command-palette-trigger"
                    >
                        <Search className="h-3.5 w-3.5" />
                        <span className="hidden sm:inline">Search…</span>
                        <kbd className="hidden sm:inline">Ctrl K</kbd>
                    </button>
                    <div className="ml-auto flex items-center gap-1.5">
                        <ActivityIndicator />
                        <button
                            onClick={() => {
                                toggleTheme();
                                if (userSettings) {
                                    updateUserSettings.mutate((prev) => ({
                                        ...prev,
                                        theme: useSettingsStore.getState()
                                            .theme,
                                    }));
                                }
                            }}
                            className="shell-icon-btn"
                            data-testid="theme-toggle"
                            title={`Theme: ${themeLabel}`}
                        >
                            {isDarkTheme ? (
                                <Sun className="h-4 w-4" />
                            ) : (
                                <Moon className="h-4 w-4" />
                            )}
                        </button>
                        <button
                            onClick={() => toggleDemoMode.mutate(!isDemoMode)}
                            disabled={toggleDemoMode.isPending}
                            className="shell-icon-btn gap-1 text-xs"
                            data-active={isDemoMode || undefined}
                            data-testid="demo-mode-toggle"
                            title="Toggle demo mode"
                        >
                            <Beaker className="h-3.5 w-3.5" />
                            {toggleDemoMode.isPending
                                ? "..."
                                : isDemoMode
                                  ? "Demo"
                                  : "Live"}
                        </button>
                        {!onAgentPage && (
                            <button
                                onClick={toggleAgentPanel}
                                className="shell-icon-btn"
                                data-active={agentPanelOpen || undefined}
                                data-testid="global-agent-panel-toggle"
                                title="AI Agent (Ctrl+Shift+L)"
                            >
                                <Bot className="h-4 w-4" />
                            </button>
                        )}
                        <button
                            onClick={() => setShortcutsOpen(true)}
                            className="shell-icon-btn"
                            data-testid="keyboard-shortcuts-btn"
                            title="Keyboard shortcuts (?)"
                        >
                            <Keyboard className="h-4 w-4" />
                        </button>

                        {/* Spec §8: the old footer folds into this status popover.
                            All `status-bar-*` testids survive on the moved elements. */}
                        <div className="relative" ref={statusRef}>
                            <button
                                onClick={() => setStatusOpen((v) => !v)}
                                className="shell-icon-btn"
                                data-testid="status-popover-trigger"
                                title="System status"
                                aria-expanded={statusOpen}
                            >
                                <Circle
                                    className={`h-2.5 w-2.5 ${
                                        statusTone === "ok"
                                            ? "fill-success text-success"
                                            : statusTone === "warn"
                                              ? "fill-warning text-warning"
                                              : "fill-destructive text-destructive"
                                    }`}
                                />
                            </button>
                            {statusOpen && (
                                <div
                                    className="absolute right-0 top-[calc(100%+8px)] z-50 w-64 rounded-lg border bg-popover p-3 text-xs text-popover-foreground shadow-lg"
                                    data-testid="status-bar"
                                >
                                    <div className="flex items-center justify-between">
                                        <div className="flex items-center gap-1.5">
                                            <Circle
                                                className={`h-2 w-2 ${sidecarOk ? "fill-success text-success" : "fill-destructive text-destructive"}`}
                                            />
                                            <span data-testid="status-bar-connection">
                                                {sidecarOk
                                                    ? "Connected"
                                                    : "Disconnected"}
                                            </span>
                                            {!sidecarOk && (
                                                <button
                                                    onClick={handleReconnect}
                                                    disabled={reconnecting}
                                                    title={
                                                        reconnecting
                                                            ? "Reconnecting…"
                                                            : undefined
                                                    }
                                                    className="ml-1 rounded border px-1.5 py-0.5 text-[11px] hover:bg-accent disabled:opacity-50"
                                                    data-testid="status-bar-reconnect"
                                                >
                                                    {reconnecting
                                                        ? "Reconnecting…"
                                                        : "Reconnect"}
                                                </button>
                                            )}
                                        </div>
                                        {health?.version && (
                                            <span className="text-muted-foreground">
                                                v{health.version}
                                            </span>
                                        )}
                                    </div>
                                    <div
                                        className="mt-2 space-y-1"
                                        data-testid="status-bar-area-health"
                                    >
                                        {areaHealth.map(
                                            ({ id, label, state }) => {
                                                const stateLabel = {
                                                    "not-configured":
                                                        "Not configured",
                                                    checking: "Checking",
                                                    connected: "Connected",
                                                    degraded: "Degraded",
                                                    unavailable: "Unavailable",
                                                }[state];
                                                const stateClass =
                                                    state === "connected"
                                                        ? "fill-success text-success"
                                                        : state ===
                                                                "checking" ||
                                                            state === "degraded"
                                                          ? "fill-warning text-warning"
                                                          : state ===
                                                              "not-configured"
                                                            ? "fill-muted-foreground text-muted-foreground"
                                                            : "fill-destructive text-destructive";

                                                return (
                                                    <div
                                                        key={id}
                                                        className="flex items-center gap-1.5"
                                                        data-testid={`status-bar-health-${id}`}
                                                        aria-label={`${label}: ${stateLabel}`}
                                                        title={`${label}: ${stateLabel}`}
                                                    >
                                                        <Circle
                                                            className={`h-1.5 w-1.5 ${stateClass}`}
                                                        />
                                                        <span>{label}</span>
                                                        <span className="ml-auto text-muted-foreground">
                                                            {stateLabel}
                                                        </span>
                                                    </div>
                                                );
                                            },
                                        )}
                                    </div>
                                    <div className="mt-2 space-y-1 border-t pt-2">
                                        {env && (
                                            <div
                                                className={
                                                    env.tier === "prd"
                                                        ? "font-semibold text-destructive"
                                                        : env.tier === "stg"
                                                          ? "text-warning"
                                                          : ""
                                                }
                                                title={environmentBadgeTitle(
                                                    env,
                                                )}
                                                data-testid="status-bar-env"
                                            >
                                                {env.label}
                                            </div>
                                        )}
                                        {isDemoMode && (
                                            <div
                                                className="text-warning"
                                                data-testid="status-bar-demo"
                                            >
                                                Demo Mode
                                            </div>
                                        )}
                                        <div
                                            className="flex items-center gap-1"
                                            title={`All times shown in local time: ${localTimeZoneName()} (${localUtcOffsetLabel()})`}
                                            data-testid="status-bar-timezone"
                                        >
                                            <Clock className="h-3 w-3" />
                                            {localTimeZoneAbbrev()}
                                        </div>
                                        <div className="text-muted-foreground">
                                            {themeLabel} theme
                                        </div>
                                    </div>
                                </div>
                            )}
                        </div>
                    </div>
                </header>

                {env?.tier === "prd" && (
                    <div
                        className="flex items-center gap-2 border-b bg-destructive/10 px-3 py-1.5 text-xs font-medium text-destructive"
                        data-testid="env-prd-banner"
                    >
                        <AlertTriangle className="h-3.5 w-3.5 shrink-0" />
                        Production profile — mutations guarded
                    </div>
                )}

                <main
                    className="flex-1 overflow-auto"
                    style={{
                        margin: 'var(--shell-sheet-margin)',
                        background: 'var(--shell-sheet-bg)',
                        borderRadius: 'var(--shell-sheet-radius)',
                        boxShadow: 'var(--shell-sheet-shadow)',
                    }}
                >
                    <Outlet />
                </main>
            </div>

            <GlobalAgentPanel
                open={agentPanelOpen}
                onClose={() => setAgentPanelOpen(false)}
            />

            <CommandPalette
                open={paletteOpen}
                onClose={() => setPaletteOpen(false)}
            />
            <KeyboardShortcutsPanel
                open={shortcutsOpen}
                onClose={() => setShortcutsOpen(false)}
            />
            <DemoTour />
        </div>
    );
}

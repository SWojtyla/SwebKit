import { useState, useEffect, useCallback } from "react";
import { NavLink, Outlet, useNavigate } from "react-router-dom";
import {
  LayoutDashboard,
  MessageSquare,
  Ship,
  Code2,
  Database,
  FolderOpen,
  Bot,
  Settings,
  Search,
  Circle,
  Activity,
} from "lucide-react";
import { CommandPalette } from "./CommandPalette";
import { useHealth } from "@/lib/hooks";

const navItems = [
  { to: "/", label: "Dashboard", icon: LayoutDashboard, end: true },
  { to: "/service-bus", label: "Service Bus", icon: MessageSquare },
  { to: "/aks", label: "AKS", icon: Ship },
  { to: "/api-client", label: "API Client", icon: Code2 },
  { to: "/redis", label: "Redis", icon: Database },
  { to: "/storage", label: "Storage", icon: FolderOpen },
  { to: "/agent", label: "AI Agent", icon: Bot },
  { to: "/monitoring", label: "Monitoring", icon: Activity },
  { to: "/settings", label: "Settings", icon: Settings },
];

export function AppLayout() {
  const [paletteOpen, setPaletteOpen] = useState(false);
  const navigate = useNavigate();
  const { data: health } = useHealth();
  const sidecarOk = health?.status === "ok";

  const handleKeyDown = useCallback((e: KeyboardEvent) => {
    if ((e.ctrlKey || e.metaKey) && (e.key === "k" || e.key === "K")) {
      e.preventDefault();
      setPaletteOpen((prev) => !prev);
    } else if ((e.ctrlKey || e.metaKey) && (e.key === "g" || e.key === "G")) {
      e.preventDefault();
      navigate("/settings");
    }
  }, [navigate]);

  useEffect(() => {
    window.addEventListener("keydown", handleKeyDown);
    return () => window.removeEventListener("keydown", handleKeyDown);
  }, [handleKeyDown]);

  return (
    <div className="flex h-screen w-screen overflow-hidden bg-background text-foreground">
      <aside className="flex w-56 flex-col border-r bg-card">
        <div className="flex h-14 items-center gap-2 border-b px-4">
          <span className="text-lg font-bold tracking-tight">SwebKit</span>
        </div>
        <nav className="flex-1 space-y-1 p-2">
          {navItems.map(({ to, label, icon: Icon, end }) => (
            <NavLink
              key={to}
              to={to}
              end={end}
              data-testid={`nav-${label.toLowerCase().replace(/\s+/g, "-")}`}
              className={({ isActive }) =>
                `flex items-center gap-3 rounded-md px-3 py-2 text-sm font-medium transition-colors ${
                  isActive
                    ? "bg-primary text-primary-foreground"
                    : "text-muted-foreground hover:bg-accent hover:text-accent-foreground"
                }`
              }
            >
              <Icon className="h-4 w-4" />
              {label}
            </NavLink>
          ))}
        </nav>
      </aside>

      <div className="flex flex-1 flex-col overflow-hidden">
        {/* Top bar */}
        <header className="flex h-14 items-center gap-3 border-b px-4" data-testid="top-bar">
          <button
            onClick={() => setPaletteOpen(true)}
            className="flex items-center gap-2 rounded-md border bg-muted px-3 py-1.5 text-sm text-muted-foreground transition-colors hover:bg-accent"
            data-testid="command-palette-trigger"
          >
            <Search className="h-4 w-4" />
            <span>Search...</span>
            <kbd className="ml-4 rounded border bg-card px-1.5 py-0.5 text-xs">Ctrl+K</kbd>
          </button>
        </header>

        <main className="flex-1 overflow-auto">
          <Outlet />
        </main>

        {/* Status bar */}
        <footer className="flex h-7 items-center gap-4 border-t px-4 text-xs text-muted-foreground" data-testid="status-bar">
          <div className="flex items-center gap-1.5">
            <Circle className={`h-2 w-2 ${sidecarOk ? "fill-green-500 text-green-500" : "fill-destructive text-destructive"}`} />
            <span data-testid="status-bar-connection">{sidecarOk ? "Connected" : "Disconnected"}</span>
          </div>
          {health?.version && (
            <span>v{health.version}</span>
          )}
          <span className="ml-auto">SwebKit</span>
        </footer>
      </div>

      <CommandPalette open={paletteOpen} onClose={() => setPaletteOpen(false)} />
    </div>
  );
}

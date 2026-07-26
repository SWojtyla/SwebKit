import { useState, useEffect, useRef } from "react";
import { useNavigate } from "react-router-dom";
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
  Activity,
} from "lucide-react";

const commands = [
  { to: "/", label: "Dashboard", icon: LayoutDashboard, keywords: "home dashboard overview" },
  { to: "/service-bus", label: "Service Bus", icon: MessageSquare, keywords: "service bus queues topics messages" },
  { to: "/aks", label: "AKS", icon: Ship, keywords: "aks kubernetes pods deployments helm" },
  { to: "/api-client", label: "API Client", icon: Code2, keywords: "api client requests http rest" },
  { to: "/redis", label: "Redis", icon: Database, keywords: "redis cache keys hash list set" },
  { to: "/storage", label: "Storage", icon: FolderOpen, keywords: "storage blobs containers azure" },
  { to: "/agent", label: "AI Agent", icon: Bot, keywords: "ai agent chat assistant" },
  { to: "/monitoring", label: "Monitoring", icon: Activity, keywords: "monitoring alerts rules health" },
  { to: "/settings", label: "Settings", icon: Settings, keywords: "settings config preferences" },
];

interface CommandPaletteProps {
  open: boolean;
  onClose: () => void;
}

export function CommandPalette({ open, onClose }: CommandPaletteProps) {
  const [query, setQuery] = useState("");
  const [selectedIndex, setSelectedIndex] = useState(0);
  const navigate = useNavigate();
  const inputRef = useRef<HTMLInputElement>(null);

  const filtered = commands.filter((c) => {
    const q = query.toLowerCase();
    return c.label.toLowerCase().includes(q) || c.keywords.includes(q);
  });

  useEffect(() => {
    if (open) {
      setQuery("");
      setSelectedIndex(0);
      setTimeout(() => inputRef.current?.focus(), 50);
    }
  }, [open]);

  useEffect(() => {
    setSelectedIndex(0);
  }, [query]);

  const handleKeyDown = (e: React.KeyboardEvent) => {
    if (e.key === "ArrowDown") {
      e.preventDefault();
      setSelectedIndex((i) => Math.min(i + 1, filtered.length - 1));
    } else if (e.key === "ArrowUp") {
      e.preventDefault();
      setSelectedIndex((i) => Math.max(i - 1, 0));
    } else if (e.key === "Enter") {
      e.preventDefault();
      if (filtered[selectedIndex]) {
        navigate(filtered[selectedIndex].to);
        onClose();
      }
    } else if (e.key === "Escape") {
      e.preventDefault();
      onClose();
    }
  };

  if (!open) return null;

  return (
    <div
      className="fixed inset-0 z-50 flex items-start justify-center bg-black/50 pt-24"
      onClick={onClose}
      onKeyDown={(e) => { if (e.key === "Escape") { e.preventDefault(); onClose(); } }}
      tabIndex={-1}
      data-testid="command-palette-overlay"
    >
      <div
        className="w-full max-w-lg rounded-lg border bg-card shadow-lg"
        onClick={(e) => e.stopPropagation()}
        data-testid="command-palette"
      >
        <div className="flex items-center gap-2 border-b px-4 py-3">
          <Search className="h-4 w-4 text-muted-foreground" />
          <input
            ref={inputRef}
            type="text"
            placeholder="Search commands..."
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            onKeyDown={handleKeyDown}
            className="flex-1 bg-transparent text-sm outline-none placeholder:text-muted-foreground"
            data-testid="command-palette-input"
          />
          <kbd className="rounded border px-1.5 py-0.5 text-xs text-muted-foreground">ESC</kbd>
        </div>
        <div className="max-h-72 overflow-auto p-2">
          {filtered.length === 0 && (
            <div className="px-3 py-4 text-sm text-muted-foreground">No commands found</div>
          )}
          {filtered.map((cmd, i) => {
            const Icon = cmd.icon;
            return (
              <button
                key={cmd.to}
                onClick={() => {
                  navigate(cmd.to);
                  onClose();
                }}
                onMouseEnter={() => setSelectedIndex(i)}
                className={`flex w-full items-center gap-3 rounded-md px-3 py-2 text-left text-sm transition-colors ${
                  i === selectedIndex ? "bg-accent" : "hover:bg-accent"
                }`}
                data-testid={`command-palette-item-${cmd.label.toLowerCase().replace(/\s+/g, "-")}`}
              >
                <Icon className="h-4 w-4 text-muted-foreground" />
                <span>{cmd.label}</span>
              </button>
            );
          })}
        </div>
      </div>
    </div>
  );
}

import { useState } from "react";
import { X, FileText } from "lucide-react";
import { useAksResourceYaml } from "@/lib/hooks";

interface YamlViewerProps {
  ns: string;
  kind: string;
  name: string;
  onClose: () => void;
}

export function YamlViewer({ ns, kind, name, onClose }: YamlViewerProps) {
  const { data: yaml, isLoading } = useAksResourceYaml(ns, kind, name);
  const [copied, setCopied] = useState(false);

  const handleCopy = () => {
    navigator.clipboard.writeText(yaml ?? "");
    setCopied(true);
    setTimeout(() => setCopied(false), 2000);
  };

  return (
    <div className="flex h-full flex-col" data-testid="yaml-viewer">
      <div className="flex items-center gap-2 border-b px-4 py-2">
        <FileText className="h-4 w-4" />
        <span className="text-sm font-medium">{kind}/{name}</span>
        <span className="text-xs text-muted-foreground">YAML</span>
        <div className="ml-auto flex items-center gap-2">
          <button
            onClick={handleCopy}
            className="rounded border px-2 py-1 text-xs hover:bg-accent"
            data-testid="yaml-copy"
          >
            {copied ? "Copied!" : "Copy"}
          </button>
          <button onClick={onClose} className="rounded p-1 hover:bg-accent">
            <X className="h-4 w-4" />
          </button>
        </div>
      </div>
      <div className="flex-1 overflow-auto bg-black p-3">
        {isLoading ? (
          <div className="text-green-400 text-xs font-mono">Loading YAML...</div>
        ) : (
          <pre className="whitespace-pre-wrap break-all text-xs font-mono text-green-400" data-testid="yaml-content">
            {yaml ?? "No YAML available"}
          </pre>
        )}
      </div>
    </div>
  );
}

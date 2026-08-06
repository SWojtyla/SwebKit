import { useEffect, useId, useState, isValidElement } from "react";
import ReactMarkdown from "react-markdown";
import type { Components, ExtraProps } from "react-markdown";
import { useSettingsStore } from "@/lib/stores/settings";

function getMermaidTheme(theme: string): "default" | "dark" {
  return theme === "dark" || theme === "fathom-dark" ? "dark" : "default";
}

export function MermaidBlock({ code }: { code: string }) {
  const theme = useSettingsStore((s) => s.theme);
  const [svg, setSvg] = useState("");
  const [error, setError] = useState<string | null>(null);
  const id = useId().replace(/:/g, "");

  useEffect(() => {
    let cancelled = false;
    const renderId = `mermaid-${id}-${Math.floor(Math.random() * 1_000_000)}`;

    import("mermaid")
      .then((mermaid) => {
        mermaid.default.initialize({
          startOnLoad: false,
          securityLevel: "strict",
          theme: getMermaidTheme(theme),
        });
        return mermaid.default.render(renderId, code.trim());
      })
      .then(({ svg }) => {
        if (!cancelled) setSvg(svg);
      })
      .catch((err: unknown) => {
        if (!cancelled) setError(String((err as Error)?.message ?? err));
      });

    return () => {
      cancelled = true;
    };
  }, [code, theme, id]);

  return (
    <div className="my-2 rounded border bg-card p-2" data-testid="mermaid-diagram">
      {error ? (
        <div className="text-xs text-destructive">{error}</div>
      ) : (
        <div className="overflow-x-auto" dangerouslySetInnerHTML={{ __html: svg }} />
      )}
      <details className="mt-1">
        <summary className="cursor-pointer text-xs text-muted-foreground hover:text-foreground">
          Source
        </summary>
        <pre className="mt-1 overflow-x-auto rounded bg-muted p-2 text-xs">
          <code>{code}</code>
        </pre>
      </details>
    </div>
  );
}

type CodeElementProps = React.ComponentPropsWithoutRef<"code"> &
  ExtraProps & {
    inline?: boolean;
  };

function CodeElement({
  inline,
  className,
  children,
  node,
  ...rest
}: CodeElementProps) {
  const match = /language-(\w+)/.exec(className || "");
  const language = match?.[1] ?? "";
  const code = String(children).replace(/\n$/, "");

  if (!inline && language === "mermaid") {
    return <MermaidBlock code={code} />;
  }

  return (
    <code className={className} {...rest}>
      {children}
    </code>
  );
}

type PreElementProps = React.ComponentPropsWithoutRef<"pre"> & ExtraProps;

function PreElement({ children, node, ...rest }: PreElementProps) {
  const first = Array.isArray(children) ? children[0] : children;
  if (isValidElement(first) && first.type === MermaidBlock) {
    return <>{children}</>;
  }
  return <pre {...rest}>{children}</pre>;
}

const components: Components = {
  code: CodeElement as unknown as Components["code"],
  pre: PreElement as unknown as Components["pre"],
};

export function AgentMarkdown({ content, className }: { content: string; className?: string }) {
  return (
    <div className={className}>
      <ReactMarkdown components={components}>{content}</ReactMarkdown>
    </div>
  );
}

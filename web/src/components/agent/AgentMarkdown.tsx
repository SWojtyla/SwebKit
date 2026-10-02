import {
    useEffect,
    useId,
    useMemo,
    useState,
    createElement,
    isValidElement,
    type JSX,
} from "react";
import ReactMarkdown from "react-markdown";
import type { Components, ExtraProps } from "react-markdown";
import { useSettingsStore } from "@/lib/stores/settings";

function getMermaidTheme(theme: string): "default" | "dark" {
    return theme === "dark" ||
        theme === "fathom-dark" ||
        theme === "letterpress-dark" ||
        theme === "cascade-dark"
        ? "dark"
        : "default";
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
                if (!cancelled)
                    setError(String((err as Error)?.message ?? err));
            });

        return () => {
            cancelled = true;
        };
    }, [code, theme, id]);

    return (
        <div
            className="my-2 rounded border bg-card p-2"
            data-testid="mermaid-diagram"
        >
            {error ? (
                <div className="text-xs text-destructive">{error}</div>
            ) : (
                <div
                    className="overflow-x-auto"
                    dangerouslySetInnerHTML={{ __html: svg }}
                />
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

// `inline` stopped arriving in react-markdown v9 — block code always lands inside
// <pre>, whose descendant selectors below strip the inline chip styling back off.
function CodeElement({
    className,
    children,
    node: _node,
    ...rest
}: CodeElementProps) {
    const match = /language-(\w+)/.exec(className || "");
    const language = match?.[1] ?? "";
    const code = String(children).replace(/\n$/, "");

    if (language === "mermaid") {
        return <MermaidBlock code={code} />;
    }

    return (
        <code
            className={`rounded bg-muted px-1 py-0.5 font-mono text-[0.85em] break-words ${className ?? ""}`}
            {...rest}
        >
            {children}
        </code>
    );
}

type PreElementProps = React.ComponentPropsWithoutRef<"pre"> & ExtraProps;

function PreElement({ children, node: _node, ...rest }: PreElementProps) {
    if (children == null) return null;
    const first = Array.isArray(children) ? children[0] : children;
    if (isValidElement(first) && first.type === MermaidBlock) {
        return <>{children}</>;
    }
    return (
        <pre
            className="my-2 overflow-x-auto rounded-md border border-border bg-muted p-3 font-mono text-xs leading-relaxed [&_code]:rounded-none [&_code]:bg-transparent [&_code]:p-0 [&_code]:[font-size:inherit] [&_code]:break-normal"
            {...rest}
        >
            {children}
        </pre>
    );
}

/** Factory for plain styled markdown elements — strips react-markdown's `node`
 *  prop before it reaches the DOM. */
function mdEl<Tag extends keyof JSX.IntrinsicElements>(
    tag: Tag,
    className: string,
) {
    function MdElement({
        node: _node,
        ...rest
    }: ExtraProps & Record<string, unknown>) {
        return createElement(tag, { className, ...rest });
    }
    return MdElement;
}

function MdAnchor({
    node: _node,
    ...rest
}: ExtraProps & React.ComponentPropsWithoutRef<"a">) {
    return (
        <a
            target="_blank"
            rel="noreferrer"
            className="break-all text-primary underline underline-offset-2 hover:opacity-80"
            {...rest}
        />
    );
}

export function AgentMarkdown({
    content,
    className,
    renderVisualBlocks = true,
}: {
    content: string;
    className?: string;
    renderVisualBlocks?: boolean;
}) {
    const components = useMemo<Components>(
        () => ({
            code: CodeElement as unknown as Components["code"],
            pre: PreElement as unknown as Components["pre"],
            // @tailwindcss/typography is not installed, so `prose` classes are
            // no-ops — every element gets styled explicitly here instead.
            h1: mdEl(
                "h1",
                "mb-1.5 mt-3 text-base font-semibold first:mt-0",
            ) as unknown as Components["h1"],
            h2: mdEl(
                "h2",
                "mb-1.5 mt-3 text-sm font-semibold first:mt-0",
            ) as unknown as Components["h2"],
            h3: mdEl(
                "h3",
                "mb-1 mt-2.5 text-sm font-semibold first:mt-0",
            ) as unknown as Components["h3"],
            h4: mdEl(
                "h4",
                "mb-1 mt-2.5 text-sm font-medium first:mt-0",
            ) as unknown as Components["h4"],
            h5: mdEl(
                "h5",
                "mb-1 mt-2 text-sm font-medium first:mt-0",
            ) as unknown as Components["h5"],
            h6: mdEl(
                "h6",
                "mb-1 mt-2 text-sm font-medium text-muted-foreground first:mt-0",
            ) as unknown as Components["h6"],
            p: mdEl(
                "p",
                "my-1.5 leading-relaxed first:my-0 last:my-0",
            ) as unknown as Components["p"],
            ul: mdEl(
                "ul",
                "my-1.5 list-disc space-y-0.5 pl-5",
            ) as unknown as Components["ul"],
            ol: mdEl(
                "ol",
                "my-1.5 list-decimal space-y-0.5 pl-5",
            ) as unknown as Components["ol"],
            li: mdEl(
                "li",
                "leading-relaxed marker:text-muted-foreground",
            ) as unknown as Components["li"],
            blockquote: mdEl(
                "blockquote",
                "my-1.5 border-l-2 border-border pl-3 italic text-muted-foreground",
            ) as unknown as Components["blockquote"],
            a: MdAnchor as unknown as Components["a"],
            table: mdEl(
                "table",
                "my-2 block max-w-full overflow-x-auto border-collapse text-xs",
            ) as unknown as Components["table"],
            th: mdEl(
                "th",
                "border border-border bg-muted/60 px-2 py-1 text-left font-semibold",
            ) as unknown as Components["th"],
            td: mdEl(
                "td",
                "border border-border px-2 py-1 align-top",
            ) as unknown as Components["td"],
            hr: mdEl("hr", "my-3 border-border") as unknown as Components["hr"],
            strong: mdEl(
                "strong",
                "font-semibold",
            ) as unknown as Components["strong"],
            em: mdEl("em", "italic") as unknown as Components["em"],
        }),
        [],
    );
    const displayedContent = useMemo(() => {
        if (renderVisualBlocks) return content;
        const withoutSections = content.replace(
            /^#{1,6}\s+.*\r?\n```(?:mermaid|json|topology|cytoscape|timeline)\r?\n[\s\S]*?```\s*/gm,
            "",
        );
        return withoutSections.replace(
            /```(?:mermaid|json|topology|cytoscape|timeline)\r?\n[\s\S]*?```\s*/g,
            "",
        );
    }, [content, renderVisualBlocks]);

    return (
        <div
            className={`min-w-0 max-w-full break-words [overflow-wrap:anywhere] [&_pre]:max-w-full [&_pre]:overflow-x-auto [&_code]:break-all [&_table]:block [&_table]:max-w-full [&_table]:overflow-x-auto [&_img]:max-w-full ${className ?? ""}`}
        >
            <ReactMarkdown components={components}>
                {displayedContent}
            </ReactMarkdown>
        </div>
    );
}

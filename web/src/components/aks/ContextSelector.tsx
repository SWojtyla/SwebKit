import { useMemo } from "react";
import type { KubeContextInfo } from "@/lib/types";
import { loadViewPreference } from "@/lib/stores/panel-preferences";
import { SearchableSelect, type SearchableSelectItem } from "@/components/shared/SearchableSelect";

interface ContextSelectorProps {
  contexts: KubeContextInfo[] | undefined;
  currentContext: string | null;
  isLoading?: boolean;
  /** Context the switch is targeting, so the button can say "Switching to X…" rather
   * than just sitting disabled with the old context's name still showing. */
  pendingContext?: string | null;
  onChange: (context: string, defaultNamespace?: string) => void;
}

type ContextItem = SearchableSelectItem & { ctx: KubeContextInfo };

export function ContextSelector({ contexts, currentContext, isLoading, pendingContext, onChange }: ContextSelectorProps) {
  const items = useMemo<ContextItem[]>(
    () =>
      (contexts ?? []).map((ctx) => ({
        value: ctx.name,
        label: ctx.name,
        subtitle:
          ctx.cluster || ctx.namespace
            ? `${ctx.cluster ?? ""}${ctx.cluster && ctx.namespace ? " · " : ""}${ctx.namespace ? `ns: ${ctx.namespace}` : ""}`
            : undefined,
        ctx,
      })),
    [contexts],
  );

  return (
    <SearchableSelect
      items={items}
      value={currentContext}
      onChange={(item) => onChange(item.value, item.ctx.namespace ?? undefined)}
      placeholder="Select context..."
      filterPlaceholder="Filter contexts..."
      isLoading={isLoading}
      loadingLabel={`Switching to ${pendingContext ?? "…"}`}
      // Current first (built in), then most-recently-used — persisted by the
      // workspace on each successful switch — then alphabetical.
      sortItems={(list) => {
        const mru = loadViewPreference<string[]>("aks-context-mru", []);
        const mruRank = new Map(mru.map((name, i) => [name, i]));
        return [...list].sort((a, b) => {
          const aCurrent = a.value === currentContext;
          const bCurrent = b.value === currentContext;
          if (aCurrent && !bCurrent) return -1;
          if (!aCurrent && bCurrent) return 1;
          const aMru = mruRank.get(a.value) ?? Number.MAX_SAFE_INTEGER;
          const bMru = mruRank.get(b.value) ?? Number.MAX_SAFE_INTEGER;
          if (aMru !== bMru) return aMru - bMru;
          return a.label.localeCompare(b.label);
        });
      }}
      testId="aks-context"
      buttonTestId="aks-context-select"
      filterTestId="aks-context-filter"
      listAriaLabel="Kubernetes contexts"
      buttonClassName="min-w-[12rem]"
    />
  );
}

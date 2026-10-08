import { useCallback, useEffect, useState } from "react";
import type { SbEntityInfo } from "@/lib/types";
import type { AdvancedFilterRule } from "./filterTypes";
import { createFilterRule, isRuleConfigured } from "./filterTypes";
import { hasActiveFilters } from "./filterLogic";
import {
    loadSbPreferences,
    saveSbPreferences,
    type SbListPreferences,
    type RowDensity,
} from "@/lib/stores/sb-preferences";
import {
    loadSavedFilters,
    addSavedFilter,
    deleteSavedFilter,
    type SbSavedFilter,
} from "@/lib/stores/sb-filters";

const DEFAULT_LIST_PREFS: SbListPreferences = {
    peekCount: 50,
    autoRefreshInterval: 0,
    rowDensity: "default" as RowDensity,
    visibleColumns: ["subject", "sequenceNumber", "enqueuedAt"],
    customColumns: [],
    nsbMode: false,
};

/**
 * The list's control state, lifted to the page so the ribbon (Messages/View tabs) and the
 * message list's own chrome (chips bar, advanced panel, column panel) read/write the same
 * source. Previously each control only lived inside MessageList, so a page-level command
 * surface could not reach it.
 */
export function useSbListControls(
    nsId: string | null,
    entity: SbEntityInfo | null,
) {
    const entityPath = entity?.entityPath;

    const [textFilter, setTextFilter] = useState("");
    const [advancedRules, setAdvancedRules] = useState<AdvancedFilterRule[]>([]);
    const [advancedEnabled, setAdvancedEnabled] = useState(false);
    const [filtersEnabled, setFiltersEnabled] = useState(true);
    const [pinnedSessionId, setPinnedSessionId] = useState<string | null>(null);
    const [showColumnToggle, setShowColumnToggle] = useState(false);
    const [customColumnInput, setCustomColumnInput] = useState("");

    const [savedFilters, setSavedFilters] = useState<SbSavedFilter[]>([]);
    const [showSavedFilters, setShowSavedFilters] = useState(false);
    const [saveFilterName, setSaveFilterName] = useState("");
    const [showSaveFilterInput, setShowSaveFilterInput] = useState(false);

    const [prefs, setPrefs] = useState<SbListPreferences>(() =>
        nsId && entityPath
            ? loadSbPreferences(nsId, entityPath)
            : DEFAULT_LIST_PREFS,
    );

    // Reload prefs + saved filters when the entity changes.
    useEffect(() => {
        if (nsId && entityPath) {
            setPrefs(loadSbPreferences(nsId, entityPath));
            setSavedFilters(loadSavedFilters(nsId, entityPath));
        }
    }, [nsId, entityPath]);

    // Persist prefs on change.
    useEffect(() => {
        if (nsId && entityPath) {
            saveSbPreferences(nsId, entityPath, prefs);
        }
    }, [prefs, nsId, entityPath]);

    const activeRuleCount = advancedRules.filter(
        (r) => r.enabled && isRuleConfigured(r),
    ).length;
    const anyFiltersActive = hasActiveFilters(
        textFilter,
        pinnedSessionId,
        advancedRules,
    );
    const canSaveFilter = anyFiltersActive;

    const clearAllFilters = useCallback(() => {
        setTextFilter("");
        setPinnedSessionId(null);
        setAdvancedRules([]);
    }, []);

    const addRule = useCallback(() => {
        setAdvancedEnabled(true);
        setAdvancedRules((rules) => [...rules, createFilterRule()]);
    }, []);

    const saveFilter = useCallback(() => {
        if (!nsId || !entity || !saveFilterName.trim()) return;
        const filter: SbSavedFilter = {
            name: saveFilterName.trim(),
            text: textFilter,
            filtersEnabled,
            advancedEnabled,
            advancedRules,
            pinnedSessionId,
        };
        setSavedFilters(addSavedFilter(nsId, entity.entityPath, filter));
        setShowSaveFilterInput(false);
        setSaveFilterName("");
    }, [
        nsId,
        entity,
        saveFilterName,
        textFilter,
        filtersEnabled,
        advancedEnabled,
        advancedRules,
        pinnedSessionId,
    ]);

    const applySavedFilter = useCallback((f: SbSavedFilter) => {
        setTextFilter(f.text);
        setFiltersEnabled(f.filtersEnabled);
        setAdvancedEnabled(f.advancedEnabled);
        setAdvancedRules(f.advancedRules);
        setPinnedSessionId(f.pinnedSessionId);
        setShowSavedFilters(false);
    }, []);

    const removeSavedFilter = useCallback(
        (f: SbSavedFilter) => {
            if (!nsId || !entity) return;
            setSavedFilters(deleteSavedFilter(nsId, entity.entityPath, f.name));
        },
        [nsId, entity],
    );

    const toggleBuiltInColumn = useCallback((col: string) => {
        setPrefs((p) => {
            const next = new Set(p.visibleColumns);
            if (next.has(col)) next.delete(col);
            else next.add(col);
            return { ...p, visibleColumns: [...next] };
        });
    }, []);

    const addCustomColumn = useCallback(() => {
        const col = customColumnInput.trim();
        if (!col) return;
        setPrefs((p) =>
            p.customColumns.includes(col)
                ? p
                : { ...p, customColumns: [...p.customColumns, col] },
        );
        setCustomColumnInput("");
    }, [customColumnInput]);

    const removeCustomColumn = useCallback((col: string) => {
        setPrefs((p) => ({
            ...p,
            customColumns: p.customColumns.filter((c) => c !== col),
        }));
    }, []);

    return {
        prefs,
        setPrefs,
        textFilter,
        setTextFilter,
        advancedRules,
        setAdvancedRules,
        advancedEnabled,
        setAdvancedEnabled,
        filtersEnabled,
        setFiltersEnabled,
        pinnedSessionId,
        setPinnedSessionId,
        savedFilters,
        showSavedFilters,
        setShowSavedFilters,
        saveFilterName,
        setSaveFilterName,
        showSaveFilterInput,
        setShowSaveFilterInput,
        showColumnToggle,
        setShowColumnToggle,
        customColumnInput,
        setCustomColumnInput,
        activeRuleCount,
        anyFiltersActive,
        canSaveFilter,
        clearAllFilters,
        addRule,
        saveFilter,
        applySavedFilter,
        removeSavedFilter,
        toggleBuiltInColumn,
        addCustomColumn,
        removeCustomColumn,
    };
}

export type SbListControls = ReturnType<typeof useSbListControls>;

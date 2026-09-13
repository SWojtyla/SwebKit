import { useId, useMemo } from "react";
import { useSbQueues, useSbTopics } from "@/lib/hooks";

interface Props {
  nsId: string | null;
  value: string;
  onChange: (value: string) => void;
  testId: string;
  className?: string;
  placeholder?: string;
}

/**
 * A free-text target-entity field with autocomplete suggestions drawn from the namespace's real
 * queues/topics — the same `useSbQueues`/`useSbTopics` data `EntityCommandPalette` already
 * fetches and lists. Previously `MessageComposer`/`BatchSendPanel` asked the user to type an
 * entity path from memory with zero validation until send actually failed.
 *
 * Deliberately a native `<input list>` + `<datalist>` rather than a new bespoke dropdown
 * component: it's a real combobox (typeahead, keyboard nav, free text still allowed for an
 * entity not yet loaded) with no new dependency and no new one-off widget to maintain.
 */
export function EntityPathInput({ nsId, value, onChange, testId, className, placeholder }: Props) {
  const listId = useId();
  const { data: queues } = useSbQueues(nsId);
  const { data: topics } = useSbTopics(nsId);

  const options = useMemo(() => {
    const paths = [...(queues ?? []), ...(topics ?? [])].map((e) => e.entityPath);
    return Array.from(new Set(paths)).sort();
  }, [queues, topics]);

  return (
    <>
      <input
        type="text"
        list={listId}
        data-testid={testId}
        value={value}
        onChange={(e) => onChange(e.target.value)}
        placeholder={placeholder ?? "queue or topic name"}
        className={className ?? "w-full rounded-md border bg-background px-2 py-1.5 text-sm"}
      />
      <datalist id={listId}>
        {options.map((path) => (
          <option key={path} value={path} />
        ))}
      </datalist>
    </>
  );
}

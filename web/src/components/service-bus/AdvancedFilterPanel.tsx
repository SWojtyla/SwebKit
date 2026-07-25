import type { AdvancedFilterRule, FilterField } from "./filterTypes";
import {
  getOperatorOptions,
  defaultOperatorForField,
  requiresPropertyName,
  getValuePlaceholder,
  createFilterRule,
} from "./filterTypes";

interface Props {
  rules: AdvancedFilterRule[];
  onChange: (rules: AdvancedFilterRule[]) => void;
}

export function AdvancedFilterPanel({ rules, onChange }: Props) {
  const addRule = () => {
    onChange([...rules, createFilterRule()]);
  };

  const removeRule = (id: string) => {
    onChange(rules.filter((r) => r.id !== id));
  };

  const updateRule = (id: string, patch: Partial<AdvancedFilterRule>) => {
    onChange(rules.map((r) => (r.id === id ? { ...r, ...patch } : r)));
  };

  const changeField = (id: string, field: FilterField) => {
    const newOperator = defaultOperatorForField(field);
    onChange(
      rules.map((r) =>
        r.id === id ? { ...r, field, operator: newOperator } : r,
      ),
    );
  };

  return (
    <div data-testid="advanced-filter-panel" className="border-b bg-muted/30 p-2">
      {rules.length === 0 ? (
        <div className="px-2 py-1 text-xs text-muted-foreground">
          No advanced rules. Add a rule to refine the filtered result set.
        </div>
      ) : (
        <div className="space-y-1">
          {rules.map((rule, i) => {
            const operators = getOperatorOptions(rule.field);
            return (
              <div
                key={rule.id}
                data-testid="advanced-rule"
                data-rule-index={i}
                className="flex items-center gap-1.5"
              >
                <label className="flex items-center gap-1 text-xs">
                  <input
                    type="checkbox"
                    checked={rule.enabled}
                    onChange={(e) => updateRule(rule.id, { enabled: e.target.checked })}
                  />
                  On
                </label>
                <select
                  data-testid="rule-field"
                  value={rule.field}
                  onChange={(e) => changeField(rule.id, e.target.value as FilterField)}
                  className="rounded border bg-card px-1.5 py-1 text-xs"
                >
                  <option value="application-property">Application Property</option>
                  <option value="enqueued-time">Enqueued Time</option>
                  <option value="delivery-count">Delivery Count</option>
                  <option value="sequence-number">Sequence Number</option>
                </select>
                <select
                  data-testid="rule-operator"
                  value={rule.operator}
                  onChange={(e) =>
                    updateRule(rule.id, { operator: e.target.value as typeof rule.operator })
                  }
                  className="rounded border bg-card px-1.5 py-1 text-xs"
                >
                  {operators.map((op) => (
                    <option key={op.value} value={op.value}>
                      {op.label}
                    </option>
                  ))}
                </select>
                {requiresPropertyName(rule.field) && (
                  <input
                    type="text"
                    data-testid="rule-property"
                    value={rule.propertyName}
                    onChange={(e) => updateRule(rule.id, { propertyName: e.target.value })}
                    placeholder="Property name"
                    className="w-28 rounded border bg-card px-2 py-1 text-xs"
                  />
                )}
                <input
                  type="text"
                  data-testid="rule-value"
                  value={rule.value}
                  onChange={(e) => updateRule(rule.id, { value: e.target.value })}
                  placeholder={getValuePlaceholder(rule.field)}
                  className="flex-1 rounded border bg-card px-2 py-1 text-xs"
                />
                <button
                  type="button"
                  data-testid="rule-remove"
                  title="Remove rule"
                  onClick={() => removeRule(rule.id)}
                  className="rounded px-1.5 py-0.5 text-xs text-muted-foreground hover:bg-accent hover:text-foreground"
                >
                  ✕
                </button>
              </div>
            );
          })}
        </div>
      )}
      <div className="mt-1 flex items-center justify-between">
        <span className="text-xs text-muted-foreground">
          Advanced rules use logical AND over enabled rules.
        </span>
        <button
          type="button"
          data-testid="rule-add"
          onClick={addRule}
          className="rounded border px-2 py-0.5 text-xs hover:bg-accent"
        >
          + Add Rule
        </button>
      </div>
    </div>
  );
}

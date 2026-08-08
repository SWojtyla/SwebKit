import { Wand2 } from "lucide-react";
import type { VariableGeneratorDefinition, VariableGeneratorKind } from "@/lib/types";

interface GeneratorConfigProps {
  generator: VariableGeneratorDefinition;
  onChange: (generator: VariableGeneratorDefinition) => void;
  testIdPrefix: string;
}

const DEFAULTS: Record<VariableGeneratorKind, Partial<VariableGeneratorDefinition>> = {
  Integer: { minInt: 0, maxInt: 100 },
  Decimal: { minDecimal: 0, maxDecimal: 100, decimalPlaces: 2 },
  Boolean: { trueWeightPercent: 50 },
  Guid: {},
  DateTime: {},
  List: { values: [] },
  Faker: { fakerCategory: "person.firstName" },
  Template: { template: "" },
};

export function GeneratorConfig({ generator, onChange, testIdPrefix }: GeneratorConfigProps) {
  const handleKindChange = (kind: VariableGeneratorKind) => {
    onChange({
      ...DEFAULTS[kind],
      ...generator,
      kind,
    });
  };

  const update = (patch: Partial<VariableGeneratorDefinition>) => {
    onChange({ ...generator, ...patch });
  };

  return (
    <div className="flex flex-1 flex-wrap items-center gap-1" data-testid={`${testIdPrefix}-generator-config`}>
      <Wand2 className="h-3 w-3 text-primary" />
      <select
        value={generator.kind}
        onChange={(e) => handleKindChange(e.target.value as VariableGeneratorKind)}
        className="min-w-0 flex-1 rounded border bg-background px-2 py-1 text-xs"
        data-testid={`${testIdPrefix}-generator-kind`}
      >
        <option value="Guid">GUID</option>
        <option value="DateTime">Timestamp</option>
        <option value="Integer">Integer</option>
        <option value="Decimal">Decimal</option>
        <option value="Boolean">Boolean</option>
        <option value="List">List</option>
        <option value="Faker">Faker</option>
        <option value="Template">Template</option>
      </select>

      {generator.kind === "Integer" && (
        <>
          <input
            type="number"
            value={generator.minInt ?? 0}
            onChange={(e) => update({ minInt: e.target.value === "" ? null : Number(e.target.value) })}
            placeholder="Min"
            className="w-16 rounded border bg-background px-2 py-1 text-xs font-mono"
            data-testid={`${testIdPrefix}-generator-min-int`}
          />
          <input
            type="number"
            value={generator.maxInt ?? 100}
            onChange={(e) => update({ maxInt: e.target.value === "" ? null : Number(e.target.value) })}
            placeholder="Max"
            className="w-16 rounded border bg-background px-2 py-1 text-xs font-mono"
            data-testid={`${testIdPrefix}-generator-max-int`}
          />
        </>
      )}

      {generator.kind === "Decimal" && (
        <>
          <input
            type="number"
            step="0.01"
            value={generator.minDecimal ?? 0}
            onChange={(e) => update({ minDecimal: e.target.value === "" ? null : Number(e.target.value) })}
            placeholder="Min"
            className="w-16 rounded border bg-background px-2 py-1 text-xs font-mono"
            data-testid={`${testIdPrefix}-generator-min-decimal`}
          />
          <input
            type="number"
            step="0.01"
            value={generator.maxDecimal ?? 100}
            onChange={(e) => update({ maxDecimal: e.target.value === "" ? null : Number(e.target.value) })}
            placeholder="Max"
            className="w-16 rounded border bg-background px-2 py-1 text-xs font-mono"
            data-testid={`${testIdPrefix}-generator-max-decimal`}
          />
          <input
            type="number"
            value={generator.decimalPlaces ?? 2}
            onChange={(e) => update({ decimalPlaces: e.target.value === "" ? 2 : Number(e.target.value) })}
            placeholder="Places"
            className="w-16 rounded border bg-background px-2 py-1 text-xs font-mono"
            data-testid={`${testIdPrefix}-generator-decimal-places`}
          />
        </>
      )}

      {generator.kind === "Boolean" && (
        <input
          type="number"
          min={0}
          max={100}
          value={generator.trueWeightPercent ?? 50}
          onChange={(e) => update({ trueWeightPercent: e.target.value === "" ? null : Number(e.target.value) })}
          placeholder="True %"
          className="w-20 rounded border bg-background px-2 py-1 text-xs font-mono"
          data-testid={`${testIdPrefix}-generator-true-weight`}
        />
      )}

      {(generator.kind === "List" || generator.kind === "Faker" || generator.kind === "Template") && (
        <input
          type="text"
          value={
            generator.kind === "List"
              ? (generator.values ?? []).join(", ")
              : generator.kind === "Faker"
                ? generator.fakerCategory ?? ""
                : generator.template ?? ""
          }
          onChange={(e) => {
            const patch: Partial<VariableGeneratorDefinition> =
              generator.kind === "List"
                ? { values: e.target.value.split(",").map((item) => item.trim()).filter(Boolean) }
                : generator.kind === "Faker"
                  ? { fakerCategory: e.target.value }
                  : { template: e.target.value };
            update(patch);
          }}
          placeholder={
            generator.kind === "List"
              ? "one, two, three"
              : generator.kind === "Faker"
                ? "person.firstName"
                : "{{otherVariable}}"
          }
          className="min-w-0 flex-1 rounded border bg-background px-2 py-1 text-xs font-mono"
          data-testid={`${testIdPrefix}-generator-input`}
        />
      )}
    </div>
  );
}

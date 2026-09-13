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

// The only categories `VariableGeneratorService.GenerateFakerValue` on the sidecar actually
// implements — anything else fails at send time with "Unsupported faker category '...'". This
// used to be a free-text field, which invited typing any Faker.js/Bogus-style path (e.g.
// "address.city") that looks plausible but silently doesn't work until the request is sent. A
// closed dropdown makes an invalid category impossible to pick instead of just hard to guess.
const FAKER_CATEGORIES: { value: string; label: string }[] = [
  { value: "person.firstName", label: "Person — first name" },
  { value: "person.lastName", label: "Person — last name" },
  { value: "person.fullName", label: "Person — full name" },
  { value: "internet.email", label: "Internet — email address" },
  { value: "phone.number", label: "Phone — number" },
  { value: "company.name", label: "Company — name" },
];

const GENERATOR_HELP: Record<VariableGeneratorKind, string> = {
  Integer: "A random whole number between Min and Max (inclusive), picked fresh each request.",
  Decimal: "A random decimal between Min and Max, rounded to the given number of places.",
  Boolean: "\"true\" or \"false\", weighted by the percentage chance of true.",
  Guid: "A new random UUID, e.g. 3fa85f64-5717-4562-b3fc-2c963f66afa6.",
  DateTime: "The current date and time (UTC, ISO 8601) at the moment the request is sent.",
  List: "One value chosen at random from the comma-separated list each time.",
  Faker: "Realistic-looking sample data from the category you pick below.",
  Template: "Combines this text with other variables: any {{variableName}} is replaced with that variable's current value. A name that doesn't match an existing variable is left as literal text, unchanged — it will not error.",
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
    <div className="flex flex-1 flex-col gap-1" data-testid={`${testIdPrefix}-generator-config`}>
      <div className="flex flex-1 flex-wrap items-center gap-1">
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

        {generator.kind === "Faker" && (
          <select
            value={generator.fakerCategory ?? FAKER_CATEGORIES[0].value}
            onChange={(e) => update({ fakerCategory: e.target.value })}
            className="min-w-0 flex-1 rounded border bg-background px-2 py-1 text-xs"
            data-testid={`${testIdPrefix}-generator-input`}
          >
            {FAKER_CATEGORIES.map((c) => (
              <option key={c.value} value={c.value}>{c.label}</option>
            ))}
          </select>
        )}

        {(generator.kind === "List" || generator.kind === "Template") && (
          <input
            type="text"
            value={
              generator.kind === "List"
                ? (generator.values ?? []).join(", ")
                : generator.template ?? ""
            }
            onChange={(e) => {
              const patch: Partial<VariableGeneratorDefinition> =
                generator.kind === "List"
                  ? { values: e.target.value.split(",").map((item) => item.trim()).filter(Boolean) }
                  : { template: e.target.value };
              update(patch);
            }}
            placeholder={
              generator.kind === "List"
                ? "one, two, three"
                : "order-{{orderId}}"
            }
            className="min-w-0 flex-1 rounded border bg-background px-2 py-1 text-xs font-mono"
            data-testid={`${testIdPrefix}-generator-input`}
          />
        )}
      </div>

      <p className="pl-4 text-[11px] leading-snug text-muted-foreground" data-testid={`${testIdPrefix}-generator-help`}>
        {GENERATOR_HELP[generator.kind]}
      </p>
    </div>
  );
}

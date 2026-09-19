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
  { value: "person.jobTitle", label: "Person — job title" },
  { value: "internet.email", label: "Internet — email address" },
  { value: "internet.username", label: "Internet — username" },
  { value: "internet.url", label: "Internet — URL" },
  { value: "internet.ip", label: "Internet — IP address" },
  { value: "phone.number", label: "Phone — number" },
  { value: "company.name", label: "Company — name" },
  { value: "company.catchPhrase", label: "Company — catchphrase" },
  { value: "address.city", label: "Address — city" },
  { value: "address.streetAddress", label: "Address — street address" },
  { value: "address.zipCode", label: "Address — zip code" },
  { value: "address.country", label: "Address — country" },
  { value: "address.fullAddress", label: "Address — full address" },
  { value: "lorem.word", label: "Lorem — word" },
  { value: "lorem.sentence", label: "Lorem — sentence" },
  { value: "lorem.paragraph", label: "Lorem — paragraph" },
  { value: "commerce.productName", label: "Commerce — product name" },
  { value: "commerce.price", label: "Commerce — price" },
  { value: "date.past", label: "Date — in the past" },
  { value: "date.future", label: "Date — in the future" },
  { value: "date.recent", label: "Date — recent" },
  { value: "date.between", label: "Date — between two dates" },
];

const isDateCategory = (category: string | null | undefined) => category?.startsWith("date.") ?? false;

/** datetime-local inputs give "YYYY-MM-DDTHH:mm" in local time; the model stores a full ISO instant (UTC). */
const toIso = (local: string): string | null => (local ? new Date(local).toISOString() : null);
const toLocalInput = (iso: string | null | undefined): string => {
  if (!iso) return "";
  const d = new Date(iso);
  if (isNaN(d.getTime())) return "";
  const pad = (n: number) => String(n).padStart(2, "0");
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
};

const GENERATOR_HELP: Record<VariableGeneratorKind, string> = {
  Integer: "A random whole number between Min and Max (inclusive), picked fresh each request.",
  Decimal: "A random decimal between Min and Max, rounded to the given number of places.",
  Boolean: "\"true\" or \"false\", weighted by the percentage chance of true.",
  Guid: "A new random UUID, e.g. 3fa85f64-5717-4562-b3fc-2c963f66afa6.",
  DateTime: "The current date and time (UTC, ISO 8601) at the moment the request is sent.",
  List: "One value chosen at random from the comma-separated list each time.",
  Faker: "Realistic-looking sample data from the category you pick below.",
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

        {generator.kind === "Faker" && isDateCategory(generator.fakerCategory) && (
          <>
            <input
              type="datetime-local"
              value={toLocalInput(generator.fakerDateAfter)}
              onChange={(e) => update({ fakerDateAfter: toIso(e.target.value) })}
              title="Earliest date that can be generated (inclusive); empty = the category's own default start"
              className="w-40 rounded border bg-background px-2 py-1 text-xs font-mono"
              data-testid={`${testIdPrefix}-generator-date-after`}
            />
            <span className="text-xs text-muted-foreground">→</span>
            <input
              type="datetime-local"
              value={toLocalInput(generator.fakerDateBefore)}
              onChange={(e) => update({ fakerDateBefore: toIso(e.target.value) })}
              title="Latest date that can be generated (inclusive); empty = the category's own default end"
              className="w-40 rounded border bg-background px-2 py-1 text-xs font-mono"
              data-testid={`${testIdPrefix}-generator-date-before`}
            />
          </>
        )}

        {generator.kind === "List" && (
          <input
            type="text"
            value={(generator.values ?? []).join(", ")}
            onChange={(e) => update({ values: e.target.value.split(",").map((item) => item.trim()).filter(Boolean) })}
            placeholder="one, two, three"
            className="min-w-0 flex-1 rounded border bg-background px-2 py-1 text-xs font-mono"
            data-testid={`${testIdPrefix}-generator-input`}
          />
        )}
      </div>

      <p className="pl-4 text-[11px] leading-snug text-muted-foreground" data-testid={`${testIdPrefix}-generator-help`}>
        {GENERATOR_HELP[generator.kind]}
        {generator.kind === "Faker" && isDateCategory(generator.fakerCategory) && (
          <>
            {" "}
            {generator.fakerCategory === "date.between"
              ? "Both bounds are required for 'between'."
              : "Leave a bound empty to use the category's own range."}
          </>
        )}
      </p>
    </div>
  );
}

import type { ReleaseTrainRecord, ReleaseTrainStage } from "./types";

function formatDate(iso: string | null): string {
  if (!iso) return "—";
  try {
    return new Date(iso).toLocaleString();
  } catch {
    return iso;
  }
}

function stageSummary(stage: ReleaseTrainStage | undefined): string {
  if (!stage) return "—";
  return `${stage.stageName} (${stage.state}${stage.result ? ` / ${stage.result}` : ""})`;
}

function componentRows(train: ReleaseTrainRecord): string[][] {
  return train.components.map((c) => [
    c.componentName,
    c.version,
    c.status,
    c.tagName ?? "—",
    c.pullRequestId?.toString() ?? "—",
    c.pipelineRunId ?? "—",
    ["TST", "STG", "PRD"].map((slot) => stageSummary(c.stages.find((s) => s.slot === slot))).join(" | "),
    c.remarks ?? "",
  ]);
}

export function formatReleaseTrainMarkdown(train: ReleaseTrainRecord): string {
  const lines = [
    `## Release Train: ${train.name}`,
    train.label ? `**Label:** ${train.label}` : "",
    `**Status:** ${train.status}`,
    `**Created:** ${formatDate(train.createdAt)}`,
    train.overallRemarks ? `**Overall remarks:** ${train.overallRemarks}` : "",
    "",
    "| Component | Version | Status | Tag | PR | Run | Stages | Remarks |",
    "| --- | --- | --- | --- | --- | --- | --- | --- |",
    ...componentRows(train).map((row) => `| ${row.join(" | ")} |`),
    "",
  ];
  return lines.filter(Boolean).join("\n");
}

export function formatReleaseTrainPlain(train: ReleaseTrainRecord): string {
  const lines = [
    `Release Train: ${train.name}`,
    train.label ? `Label: ${train.label}` : "",
    `Status: ${train.status}`,
    `Created: ${formatDate(train.createdAt)}`,
    train.overallRemarks ? `Overall remarks: ${train.overallRemarks}` : "",
    "",
    "Components:",
    ...train.components.map((c) => {
      const stages = ["TST", "STG", "PRD"].map((slot) => `${slot}: ${stageSummary(c.stages.find((s) => s.slot === slot))}`).join(", ");
      return `- ${c.componentName} ${c.version} [${c.status}] tag=${c.tagName ?? "—"} pr=${c.pullRequestId ?? "—"} run=${c.pipelineRunId ?? "—"} stages={${stages}}`;
    }),
    "",
  ];
  return lines.filter(Boolean).join("\n");
}

export function formatReleaseTrainRichTable(train: ReleaseTrainRecord): string {
  // Confluence-compatible table using || header syntax
  const header = "|| Component || Version || Status || Tag || PR || Run || TST || STG || PRD || Remarks ||";
  const rows = train.components.map((c) => {
    const tst = stageSummary(c.stages.find((s) => s.slot === "TST"));
    const stg = stageSummary(c.stages.find((s) => s.slot === "STG"));
    const prd = stageSummary(c.stages.find((s) => s.slot === "PRD"));
    return `| ${c.componentName} | ${c.version} | ${c.status} | ${c.tagName ?? "—"} | ${c.pullRequestId?.toString() ?? "—"} | ${c.pipelineRunId ?? "—"} | ${tst} | ${stg} | ${prd} | ${c.remarks ?? ""} |`;
  });
  return [header, ...rows, "", train.overallRemarks ?? ""].filter((l) => l !== undefined).join("\n");
}

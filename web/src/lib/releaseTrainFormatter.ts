import type { ReleaseTrainComponent, ReleaseTrainRecord, ReleaseTrainStage } from "./types";

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

function mdLink(text: string, url: string | null | undefined): string {
  if (!url) return text;
  return `[${text}](${url})`;
}

function confLink(text: string, url: string | null | undefined): string {
  if (!url) return text;
  return `[${url}|${text}]`;
}

function escapePipe(text: string): string {
  return text.replace(/\|/g, "\\|");
}

function stageCell(c: { stages?: ReleaseTrainStage[] | null }, slot: string): string {
  return escapePipe(stageSummary(c.stages?.find((s) => s.slot === slot)));
}

function markdownCells(c: ReleaseTrainComponent): string[] {
  return [
    escapePipe(c.componentName),
    escapePipe(c.version),
    c.status,
    escapePipe(c.tagName ?? "—"),
    mdLink(`#${c.pullRequestId ?? "—"}`, c.pullRequestUrl),
    mdLink(c.pipelineRunId ?? "—", c.pipelineRunUrl),
    stageCell(c, "TST"),
    stageCell(c, "STG"),
    stageCell(c, "PRD"),
    escapePipe(c.remarks ?? ""),
  ];
}

function richCells(c: ReleaseTrainComponent): string[] {
  return [
    escapePipe(c.componentName),
    escapePipe(c.version),
    c.status,
    escapePipe(c.tagName ?? "—"),
    confLink(`#${c.pullRequestId ?? "—"}`, c.pullRequestUrl),
    confLink(c.pipelineRunId ?? "—", c.pipelineRunUrl),
    stageCell(c, "TST"),
    stageCell(c, "STG"),
    stageCell(c, "PRD"),
    escapePipe(c.remarks ?? ""),
  ];
}

export function formatReleaseTrainMarkdown(train: ReleaseTrainRecord): string {
  const lines = [
    `## Release Train: ${train.name}`,
    train.label ? `**Label:** ${train.label}` : "",
    `**Status:** ${train.status}`,
    `**Created:** ${formatDate(train.createdAt)}`,
    train.overallRemarks ? `**Overall remarks:** ${train.overallRemarks}` : "",
    "",
    "| Component | Version | Status | Tag | PR | Run | TST | STG | PRD | Remarks |",
    "| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |",
    ...train.components.map((c) => `| ${markdownCells(c).join(" | ")} |`),
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
      const stages = ["TST", "STG", "PRD"].map((slot) => `${slot}: ${stageSummary(c.stages.find((s) => s.slot === slot))}`).join("; ");
      const pr = c.pullRequestUrl ? `#${c.pullRequestId} (${c.pullRequestUrl})` : (c.pullRequestId?.toString() ?? "—");
      const run = c.pipelineRunUrl ? `${c.pipelineRunId} (${c.pipelineRunUrl})` : (c.pipelineRunId ?? "—");
      return `- ${c.componentName} ${c.version} [${c.status}] tag=${c.tagName ?? "—"} pr=${pr} run=${run} stages={${stages}}`;
    }),
    "",
  ];
  return lines.filter(Boolean).join("\n");
}

export function formatReleaseTrainRichTable(train: ReleaseTrainRecord): string {
  // Confluence wiki-style table using || header syntax
  const header = "|| Component || Version || Status || Tag || PR || Run || TST || STG || PRD || Remarks ||";
  const rows = train.components.map((c) => `| ${richCells(c).join(" | ")} |`);
  return [header, ...rows, "", train.overallRemarks ?? ""].filter((l) => l !== undefined).join("\n");
}

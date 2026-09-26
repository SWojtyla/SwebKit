const GENERATED_KUBERNETES_ANNOTATIONS = new Set([
  "kubectl.kubernetes.io/last-applied-configuration",
  "deployment.kubernetes.io/revision",
  "meta.helm.sh/release-name",
  "meta.helm.sh/release-namespace",
]);

function indentOf(line: string): number {
  return line.length - line.trimStart().length;
}

function mappingKey(line: string): string | null {
  const trimmed = line.trimStart();
  const colon = trimmed.indexOf(":");
  if (colon <= 0) return null;
  return trimmed.slice(0, colon).trim().replace(/^['"]|['"]$/g, "");
}

export function filterGeneratedAnnotations(yaml: string): { yaml: string; hidden: number } {
  const lines = yaml.replaceAll("\r\n", "\n").split("\n");
  const output: string[] = [];
  let metadataIndent: number | null = null;
  let annotationsIndent: number | null = null;
  let droppingIndent: number | null = null;
  let hidden = 0;

  for (const line of lines) {
    const blank = line.trim().length === 0;
    const indent = indentOf(line);

    if (droppingIndent !== null) {
      if (blank || indent > droppingIndent) continue;
      droppingIndent = null;
    }

    if (!blank && metadataIndent !== null && indent <= metadataIndent) {
      metadataIndent = null;
      annotationsIndent = null;
    }
    if (!blank && annotationsIndent !== null && indent <= annotationsIndent) {
      annotationsIndent = null;
    }

    const key = mappingKey(line);
    if (metadataIndent === null && indent === 0 && key === "metadata") {
      metadataIndent = indent;
    } else if (metadataIndent !== null && annotationsIndent === null && indent > metadataIndent && key === "annotations") {
      annotationsIndent = indent;
    } else if (annotationsIndent !== null && indent > annotationsIndent && key && GENERATED_KUBERNETES_ANNOTATIONS.has(key)) {
      hidden++;
      droppingIndent = indent;
      continue;
    }

    output.push(line);
  }

  return { yaml: output.join("\n"), hidden };
}

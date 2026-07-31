export function downloadText(filename: string, content: string, mimeType = "application/json"): void {
  downloadBlob(filename, new Blob([content], { type: mimeType }));
}

export function downloadBlob(filename: string, blob: Blob): void {
  const url = URL.createObjectURL(blob);
  const a = document.createElement("a");
  a.href = url;
  a.download = filename;
  document.body.appendChild(a);
  a.click();
  a.remove();
  URL.revokeObjectURL(url);
}

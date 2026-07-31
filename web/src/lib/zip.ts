import { zip, strToU8 } from "fflate";

export function buildZip(files: Record<string, string>): Promise<Blob> {
  const payload: Record<string, Uint8Array> = {};
  for (const [name, content] of Object.entries(files)) {
    payload[name] = strToU8(content);
  }
  return new Promise((resolve, reject) => {
    zip(payload, { level: 1 }, (err, data) => {
      if (err) {
        reject(err);
      } else {
        resolve(new Blob([data.buffer as ArrayBuffer], { type: "application/zip" }));
      }
    });
  });
}

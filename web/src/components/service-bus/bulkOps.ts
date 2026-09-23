/**
 * Runs a bulk action over items in fixed-size chunks so callers can report real
 * progress between requests. The sidecar mutations are one-request-per-call, so
 * a single call gives no progress signal until it finishes — chunking makes each
 * completed chunk a completed unit of work (the server operations are per-message
 * anyway, so partial completion semantics are unchanged).
 */
export const BULK_CHUNK_SIZE = 25;

/**
 * Processes `items` in chunks of `chunkSize`, awaiting `run` per chunk and calling
 * `onProgress(done, total)` after each chunk completes (and once up front with 0).
 * Stops at the first failing chunk — the error propagates with earlier chunks
 * already applied, matching the non-atomic per-message server semantics.
 * Returns the number of items processed.
 */
export async function runInChunks<T>(
    items: readonly T[],
    run: (chunk: T[]) => Promise<unknown>,
    onProgress: (done: number, total: number) => void,
    chunkSize = BULK_CHUNK_SIZE,
): Promise<number> {
    let done = 0;
    onProgress(0, items.length);
    for (let i = 0; i < items.length; i += chunkSize) {
        const chunk = items.slice(i, i + chunkSize);
        await run(chunk);
        done += chunk.length;
        onProgress(done, items.length);
    }
    return done;
}

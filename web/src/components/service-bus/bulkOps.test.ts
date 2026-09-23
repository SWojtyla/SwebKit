import { describe, expect, it } from "vitest";
import { runInChunks } from "./bulkOps";

describe("runInChunks", () => {
    it("processes items in fixed-size chunks and reports progress after each", async () => {
        const chunks: number[][] = [];
        const progress: number[] = [];

        const done = await runInChunks(
            Array.from({ length: 55 }, (_, i) => i),
            async (chunk) => {
                chunks.push(chunk);
            },
            (d) => progress.push(d),
            25,
        );

        expect(chunks.map((c) => c.length)).toEqual([25, 25, 5]);
        expect(chunks.flat()).toEqual(Array.from({ length: 55 }, (_, i) => i));
        expect(progress).toEqual([0, 25, 50, 55]);
        expect(done).toBe(55);
    });

    it("stops at the failed chunk and keeps earlier progress", async () => {
        const progress: number[] = [];
        let calls = 0;

        await expect(
            runInChunks(
                [1, 2, 3, 4, 5],
                async () => {
                    calls++;
                    if (calls === 2) throw new Error("boom");
                },
                (d) => progress.push(d),
                2,
            ),
        ).rejects.toThrow("boom");

        expect(calls).toBe(2);
        expect(progress).toEqual([0, 2]);
    });

    it("handles an empty selection without running", async () => {
        let calls = 0;
        const done = await runInChunks(
            [],
            async () => {
                calls++;
            },
            () => {},
        );
        expect(calls).toBe(0);
        expect(done).toBe(0);
    });
});

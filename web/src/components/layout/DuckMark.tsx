// Rubber-duck brand mark shown in place of the Layers tile under the Duck themes.
// Fixed colours on purpose: the duck stays yellow in Duck Light and Mallard Dark alike.
export function DuckMark({ className }: { className?: string }) {
    return (
        <svg viewBox="0 0 64 64" className={className} aria-hidden="true">
            <path
                d="M8 40c0-6 4-10 10-10h6c-3-3-4-6-4-10 0-8 7-14 15-14s15 6 15 14c0 3-1 6-3 8 5 1 9 3 11 6 2 4 0 10-6 14-5 4-13 6-22 6S16 58 11 54c-2-2-3-8-3-14z"
                fill="oklch(0.86 0.17 92)"
                stroke="oklch(0.62 0.13 75)"
                strokeWidth="2"
            />
            <path
                d="M48 19c5-1 10 0 12 2-2 3-7 5-12 4z"
                fill="oklch(0.72 0.17 55)"
            />
            <circle cx="40" cy="17" r="3" fill="oklch(0.24 0.06 255)" />
            <circle cx="41" cy="16" r="1" fill="#fff" />
            <path
                d="M22 42c6 4 14 4 20 0"
                fill="none"
                stroke="oklch(0.70 0.15 80)"
                strokeWidth="2.5"
                strokeLinecap="round"
            />
        </svg>
    );
}

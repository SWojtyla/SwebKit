export function serializeEnvVars(env: Record<string, string>): string {
    return Object.entries(env)
        .map(([k, v]) => `${k}=${v}`)
        .join("\n");
}

export function parseEnvVars(text: string): Record<string, string> {
    const env: Record<string, string> = {};
    for (const line of text.split("\n")) {
        const trimmed = line.trim();
        if (!trimmed || trimmed.startsWith("#")) continue;
        const eq = trimmed.indexOf("=");
        if (eq <= 0) continue;
        env[trimmed.slice(0, eq).trim()] = trimmed.slice(eq + 1);
    }
    return env;
}

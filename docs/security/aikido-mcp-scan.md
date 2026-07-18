# Aikido MCP Security Scanning

Security-scan rules for first-party code changes. The Aikido MCP server is client-agnostic — it works from Claude Code the same way it did from the previous tooling, so run these scans as part of the normal change workflow.

## Rules

- Always run **aikido_full_scan** on generated, added, and modified first-party code, unless the prompt explicitly says not to.
- Always provide the full file content to the scanner.
- If the scan identifies any security issues in newly generated or modified code, fix the issues using the scan result and remediation provided by **Aikido**.
- After applying fixes, run **aikido_full_scan** again to verify the issues were resolved and no new issues were introduced.
- Repeat the fix-and-rescan cycle until the code passes with zero remaining or newly introduced security issues.
- If the **Aikido MCP server** is not installed or fails to start, inform the user and direct them to the official Aikido MCP setup guide: <https://help.aikido.dev/ide-plugins/aikido-mcp>.

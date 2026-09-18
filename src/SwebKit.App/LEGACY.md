# LEGACY — do not modify

This project (.NET MAUI Blazor Hybrid shell) is the **former** SwebKit app,
kept in-repo as reference/backup only. The primary stack is Tauri (`src-tauri/`)
+ React (`web/`) + the .NET sidecar (`src-sidecar/`).

Do not search, read, or modify this project unless a task explicitly concerns
the legacy stack. Shared libraries (`SwebKit.Core`, `.Azure`, `.Kubernetes`,
`.Redis`, `.Sql`, `.Agents`) remain live — the sidecar consumes them.

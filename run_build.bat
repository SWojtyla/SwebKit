@echo off
set RUST_LOG=debug
node "d:\Projects\SwebKit\web\node_modules\@tauri-apps\cli\main.js" build --bundles msi > msi_output.log 2>&1
echo Exit code: %ERRORLEVEL%

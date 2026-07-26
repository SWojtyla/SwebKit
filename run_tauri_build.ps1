$env:RUST_LOG = "debug"
$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = "node"
$psi.Arguments = '"d:\Projects\SwebKit\web\node_modules\@tauri-apps\cli\main.js" build'
$psi.WorkingDirectory = "d:\Projects\SwebKit"
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError = $true
$psi.UseShellExecute = $false
$psi.CreateNoWindow = $false

$proc = New-Object System.Diagnostics.Process
$proc.StartInfo = $psi
$proc.Start() | Out-Null
$stdout = $proc.StandardOutput.ReadToEnd()
$stderr = $proc.StandardError.ReadToEnd()
$proc.WaitForExit()
Write-Host "Exit code: $($proc.ExitCode)"
Write-Host "=== STDOUT ==="
Write-Host $stdout
Write-Host "=== STDERR ==="
Write-Host $stderr

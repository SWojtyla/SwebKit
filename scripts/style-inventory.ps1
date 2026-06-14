[CmdletBinding()]
param(
    [int]$Top = 25,
    [switch]$FailOnLegacyTokens
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$legacyTokens = @(
    '--color-input-bg',
    '--color-surface-raised',
    '--color-surface-hover',
    '--font-mono',
    '--color-danger'
)

function Get-SourceFile {
    param(
        [string]$Path,
        [string[]]$Include
    )

    Get-ChildItem -Path $Path -Recurse -Include $Include -File |
    Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' }
}

function Get-LineCount {
    param([System.IO.FileInfo]$File)

    $lineCount = 0
    foreach ($line in [System.IO.File]::ReadLines($File.FullName)) {
        $lineCount++
    }

    $lineCount
}

function Get-ClassAttributes {
    param(
        [System.IO.FileInfo[]]$Files,
        [string]$ElementName
    )

    foreach ($file in $Files) {
        $text = Get-Content -LiteralPath $file.FullName -Raw
        $pattern = '<' + $ElementName + '[^>]*class="([^"]+)"'

        foreach ($match in [regex]::Matches($text, $pattern)) {
            $class = ($match.Groups[1].Value -replace '@\([^)]*\)', '' -replace '\s+', ' ').Trim()
            if (-not [string]::IsNullOrWhiteSpace($class)) {
                [pscustomobject]@{
                    Class = $class
                    File  = $file.FullName.Substring($repoRoot.Path.Length + 1)
                }
            }
        }
    }
}

Push-Location $repoRoot
try {
    $cssFiles = Get-SourceFile -Path 'src\SwebKit.App' -Include '*.css'
    $razorCssFiles = $cssFiles | Where-Object { $_.Name -like '*.razor.css' }
    $razorFiles = Get-SourceFile -Path 'src\SwebKit.App\Components' -Include '*.razor'
    $appCss = Get-Item 'src\SwebKit.App\wwwroot\app.css'

    $componentMatches = $razorFiles | Select-String -Pattern '<button', '<select', '<PageToolbar', '<Dropdown', '<AppDropdown', 'app-native-control' -SimpleMatch
    $buttonClasses = @(Get-ClassAttributes -Files $razorFiles -ElementName 'button')
    $selectClasses = @(Get-ClassAttributes -Files $razorFiles -ElementName 'select')

    $summary = [pscustomobject]@{
        AppCssLines             = Get-LineCount $appCss
        CssFileCount            = @($cssFiles).Count
        RazorCssFileCount       = @($razorCssFiles).Count
        IsolatedCssLines        = ($razorCssFiles | ForEach-Object { Get-LineCount $_ } | Measure-Object -Sum).Sum
        RazorFileCount          = @($razorFiles).Count
        ButtonOccurrences       = @(($componentMatches | Where-Object { $_.Line -like '*<button*' })).Count
        SelectOccurrences       = @(($componentMatches | Where-Object { $_.Line -like '*<select*' })).Count
        PageToolbarUsages       = @(($componentMatches | Where-Object { $_.Line -like '*<PageToolbar*' })).Count
        DropdownComponentUsages = @(($componentMatches | Where-Object { $_.Line -like '*<Dropdown*' -or $_.Line -like '*<AppDropdown*' })).Count
        AppNativeControlUsages  = @(($componentMatches | Where-Object { $_.Line -like '*app-native-control*' })).Count
    }

    Write-Host 'SwebKit style inventory'
    Write-Host '======================='
    $summary | Format-List

    Write-Host ''
    Write-Host "Top $Top button class attributes"
    $buttonClasses |
    Group-Object Class |
    Sort-Object Count -Descending |
    Select-Object -First $Top Count, Name |
    Format-Table -AutoSize

    Write-Host ''
    Write-Host "Top $Top select class attributes"
    $selectClasses |
    Group-Object Class |
    Sort-Object Count -Descending |
    Select-Object -First $Top Count, Name |
    Format-Table -AutoSize

    Write-Host ''
    Write-Host 'CSS lines by component folder'
    $razorCssFiles |
    ForEach-Object {
        $relativePath = $_.FullName.Substring($repoRoot.Path.Length + 1)
        $parts = $relativePath -split '\\'
        [pscustomobject]@{
            Folder = if ($parts.Length -ge 5) { $parts[3] } else { '(root)' }
            Lines  = Get-LineCount $_
        }
    } |
    Group-Object Folder |
    ForEach-Object {
        [pscustomobject]@{
            Folder = $_.Name
            Files  = $_.Count
            Lines  = ($_.Group | Measure-Object Lines -Sum).Sum
        }
    } |
    Sort-Object Lines -Descending |
    Format-Table -AutoSize

    Write-Host ''
    Write-Host 'Legacy token references'
    $legacyPattern = ($legacyTokens | ForEach-Object { [regex]::Escape($_) }) -join '|'
    $legacySourceFiles = $cssFiles | Where-Object { $_.FullName -ne $appCss.FullName }
    $legacyMatches = @($legacySourceFiles | Select-String -Pattern $legacyPattern)

    if ($legacyMatches.Count -eq 0) {
        Write-Host 'None found.'
    }
    else {
        $legacyMatches |
        Select-Object @{ Name = 'File'; Expression = { $_.Path.Substring($repoRoot.Path.Length + 1) } }, LineNumber, Line |
        Format-Table -AutoSize

        if ($FailOnLegacyTokens) {
            throw "Found $($legacyMatches.Count) legacy token reference(s)."
        }
    }
}
finally {
    Pop-Location
}
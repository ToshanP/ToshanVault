#requires -Version 5.1
<#
.SYNOPSIS
  Produce a clean two-file release of ToshanVault: one self-contained .exe
  plus the editable appsettings.json.

.DESCRIPTION
  Cleans .\App\ then publishes single-file self-extracting into it. Anything
  in .\App\ that looks like user data is preserved across runs:
    - the VaultDb\ subfolder (and any *.db / *.sqlite / *.sqlite3 files)
    - an existing appsettings.json (re-seeded from project default if absent)
  Everything else in .\App\ is deleted before publish.

.EXAMPLE
  pwsh tools\publish-single.ps1
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$OutDir = 'App'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

# Kill any running instance so the exe isn't locked.
Get-Process -Name 'ToshanVault.App','testhost' -ErrorAction SilentlyContinue |
    ForEach-Object { Stop-Process -Id $_.Id -Force }

# Clean .\App\ but PRESERVE anything that looks like user data:
#  - VaultDb\ subfolder (the live DB lives here)
#  - any *.db / *.sqlite / *.sqlite3 files anywhere directly in .\App\
#  - appsettings.json (so user edits to the DB path survive)
# Everything else is fair game.
if (Test-Path $OutDir) {
    $protectedDirs  = @('VaultDb')
    $protectedFiles = @('appsettings.json')
    $dbExtensions   = @('.db','.sqlite','.sqlite3')

    Get-ChildItem -LiteralPath $OutDir -Force | ForEach-Object {
        $isProtectedDir   = $_.PSIsContainer -and ($protectedDirs -contains $_.Name)
        $isProtectedFile  = (-not $_.PSIsContainer) -and ($protectedFiles -contains $_.Name)
        $isDbFile         = (-not $_.PSIsContainer) -and ($dbExtensions -contains $_.Extension.ToLowerInvariant())
        if ($isProtectedDir -or $isProtectedFile -or $isDbFile) {
            Write-Host "  preserving $($_.Name)" -ForegroundColor DarkGray
        } else {
            Remove-Item -LiteralPath $_.FullName -Recurse -Force
        }
    }
} else {
    New-Item -ItemType Directory -Path $OutDir | Out-Null
}

dotnet publish src\ToshanVault.App\ToshanVault.App.csproj `
    -c $Configuration -p:Platform=x64 -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:IncludeAllContentForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:PublishReadyToRun=false `
    -p:PublishTrimmed=false `
    -p:WindowsAppSDKSelfContained=true `
    -p:DebugType=none `
    -p:DebugSymbols=false `
    -o $OutDir --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit $LASTEXITCODE)" }

# Seed appsettings.json on first run only — never overwrite an existing
# user-edited file. (The publish step also bundles it inside the exe so
# the app still boots if this side file is missing.)
$settingsDst = Join-Path $OutDir 'appsettings.json'
if (-not (Test-Path $settingsDst)) {
    Copy-Item 'src\ToshanVault.App\appsettings.json' $settingsDst
    Write-Host "Seeded appsettings.json from project default." -ForegroundColor Yellow
}

Write-Host "`nPublished to $root\$OutDir`:" -ForegroundColor Green
Get-ChildItem $OutDir | Format-Table Name,@{n='Size';e={
    if ($_.PSIsContainer)    { '(folder)' }
    elseif ($_.Length -gt 1MB) { "$([math]::Round($_.Length/1MB,2)) MB" }
    else                       { "$($_.Length) B" }
}} -AutoSize


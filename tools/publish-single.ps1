#requires -Version 5.1
<#
.SYNOPSIS
  Produce a clean two-file release of ToshanVault: one self-contained .exe
  plus the editable appsettings.json.

.DESCRIPTION
  Publishes single-file self-extracting into a temp staging folder, then
  copies *only* ToshanVault.App.exe into .\App\. Leaves any other content
  in .\App\ (e.g. VaultDb\, backups\, a user-edited appsettings.json)
  untouched. If appsettings.json does not yet exist in .\App\ it is seeded
  from the project default; if it exists it is preserved.

  This script REFUSES to delete anything from .\App\. An earlier version
  did `Remove-Item -Recurse App` which destroyed a user's VaultDb subfolder
  on first run. Never again.

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

# Stage into a sibling temp folder we fully own, then copy just the exe.
# Never wipe $OutDir — it may contain the user's VaultDb, backups, or an
# edited appsettings.json, and a recursive delete here once cost real data.
$stage = Join-Path $root '.publish-stage'
if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }

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
    -o $stage --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit $LASTEXITCODE)" }

if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Path $OutDir | Out-Null }

# Replace just the exe (overwrite is safe — it's a build output).
Copy-Item (Join-Path $stage 'ToshanVault.App.exe') (Join-Path $OutDir 'ToshanVault.App.exe') -Force

# Seed appsettings.json on first run only — never overwrite a user-edited one.
$settingsDst = Join-Path $OutDir 'appsettings.json'
if (-not (Test-Path $settingsDst)) {
    Copy-Item 'src\ToshanVault.App\appsettings.json' $settingsDst
    Write-Host "Seeded appsettings.json from project default." -ForegroundColor Yellow
}

Remove-Item -Recurse -Force $stage

Write-Host "`nPublished to $root\$OutDir`:" -ForegroundColor Green
Get-ChildItem $OutDir | Format-Table Name,@{n='Size';e={
    if ($_.PSIsContainer)    { '(folder)' }
    elseif ($_.Length -gt 1MB) { "$([math]::Round($_.Length/1MB,2)) MB" }
    else                       { "$($_.Length) B" }
}} -AutoSize


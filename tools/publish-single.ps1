#requires -Version 5.1
<#
.SYNOPSIS
  Produce a clean two-file release of ToshanVault: one self-contained .exe
  plus the editable appsettings.json.

.DESCRIPTION
  Wipes .\App\, runs `dotnet publish` in single-file self-extracting mode,
  then copies appsettings.json next to the exe so the user can edit the DB
  path without rebuilding. PDBs are suppressed.

  Output layout:
      App\
        ToshanVault.App.exe        (~98 MB, everything bundled)
        appsettings.json           (483 B, editable)

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

if (Test-Path $OutDir) { Remove-Item -Recurse -Force $OutDir }

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

# appsettings.json gets bundled inside the exe by IncludeAllContentForSelfExtract,
# but we want it editable on disk too. Copy from source.
Copy-Item "src\ToshanVault.App\appsettings.json" "$OutDir\appsettings.json" -Force

Write-Host "`nPublished to $root\$OutDir`:" -ForegroundColor Green
Get-ChildItem $OutDir | Format-Table Name,@{n='Size';e={
    if ($_.Length -gt 1MB) { "$([math]::Round($_.Length/1MB,2)) MB" }
    else { "$($_.Length) B" }
}} -AutoSize

#requires -Version 5.1
<#
.SYNOPSIS
  Seed the recipe + recipe_tag tables from the generated SQL file.

.DESCRIPTION
  Runs tools\seed-recipes.sql against .\App\VaultDb\vault.db.
  Idempotent: refuses if any recipe row already exists.

.EXAMPLE
  pwsh tools\seed-recipes.ps1
#>
[CmdletBinding()]
param(
    [string]$DbPath = (Join-Path (Split-Path $PSScriptRoot -Parent) 'App\VaultDb\vault.db')
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path $DbPath)) {
    throw "Database not found at $DbPath. Launch the app first to create the DB."
}

# --- Load Microsoft.Data.Sqlite from build output ---
$repoRoot = Split-Path $PSScriptRoot -Parent
$candidateDirs = @(
    (Join-Path $repoRoot 'src\ToshanVault.App\bin\x64\Release'),
    (Join-Path $repoRoot 'src\ToshanVault.App\bin\x64\Debug'),
    (Join-Path $repoRoot 'tests\ToshanVault.Tests\bin\x64\Release'),
    (Join-Path $repoRoot 'tests\ToshanVault.Tests\bin\x64\Debug')
) | Where-Object { Test-Path $_ }

$sqliteDll = $null
foreach ($dir in $candidateDirs) {
    $hit = Get-ChildItem $dir -Recurse -Filter 'Microsoft.Data.Sqlite.dll' -EA SilentlyContinue | Select-Object -First 1
    if ($hit) { $sqliteDll = $hit.FullName; break }
}
if (-not $sqliteDll) {
    throw "Could not find Microsoft.Data.Sqlite.dll. Run 'dotnet build ToshanVault.slnx -c Debug -p:Platform=x64' first."
}
$binDir = Split-Path $sqliteDll -Parent

function Resolve-Dll($name) {
    $p = Join-Path $binDir $name
    if (Test-Path $p) { return $p }
    $h = Get-ChildItem $binDir -Recurse -Filter $name -EA SilentlyContinue | Select-Object -First 1
    if ($h) { return $h.FullName }
    return $null
}

$rawCore = Resolve-Dll 'SQLitePCLRaw.core.dll'
$rawProv = Resolve-Dll 'SQLitePCLRaw.provider.e_sqlite3.dll'
$rawBatt = Resolve-Dll 'SQLitePCLRaw.batteries_v2.dll'
$nativeDll = Resolve-Dll 'e_sqlite3.dll'
if (-not $nativeDll) { throw "Could not find native e_sqlite3.dll under $binDir." }

$env:PATH = "$(Split-Path $nativeDll -Parent);$env:PATH"
foreach ($d in @($rawCore, $rawProv, $rawBatt, $sqliteDll)) {
    if ($d) { Add-Type -Path $d }
}
[SQLitePCL.Batteries_V2]::Init()

# --- Connect and seed ---
$conn = New-Object Microsoft.Data.Sqlite.SqliteConnection("Data Source=$DbPath")
$conn.Open()
try {
    $check = $conn.CreateCommand()
    $check.CommandText = "SELECT COUNT(*) FROM recipe"
    $existing = [int]$check.ExecuteScalar()
    if ($existing -gt 0) {
        Write-Host "Refusing to seed: recipe already has $existing row(s)." -ForegroundColor Yellow
        return
    }

    $sqlFile = Join-Path $PSScriptRoot 'seed-recipes.sql'
    if (-not (Test-Path $sqlFile)) {
        throw "SQL file not found at $sqlFile. Run 'node tools/parse-recipes-xlsx.js' first."
    }

    $sql = Get-Content $sqlFile -Raw
    $tx = $conn.BeginTransaction()
    $cmd = $conn.CreateCommand()
    $cmd.Transaction = $tx

    # Split on semicolons followed by newline (statements may contain embedded newlines in strings)
    $statements = [regex]::Split($sql, ";\s*`n") | Where-Object { $_.Trim() -ne '' }
    $count = 0
    foreach ($stmt in $statements) {
        $s = $stmt.Trim()
        if (-not $s.EndsWith(';')) { $s += ';' }
        $cmd.CommandText = $s
        $cmd.ExecuteNonQuery() | Out-Null
        $count++
    }
    $tx.Commit()

    # Count results
    $check.CommandText = "SELECT COUNT(*) FROM recipe"
    $recipeCount = [int]$check.ExecuteScalar()
    $check.CommandText = "SELECT COUNT(*) FROM recipe_tag"
    $tagCount = [int]$check.ExecuteScalar()
    $check.CommandText = "SELECT COUNT(*) FROM recipe WHERE is_favourite = 1"
    $triedCount = [int]$check.ExecuteScalar()

    Write-Host "Seeded $recipeCount recipes ($triedCount tried/favourite) with $tagCount tags." -ForegroundColor Green
}
finally {
    $conn.Close()
}

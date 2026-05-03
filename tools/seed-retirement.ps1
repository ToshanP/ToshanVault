#requires -Version 5.1
<#
.SYNOPSIS
  Seed the retirement_item table with the expense + income rows the user
  reconstructed during the 2026-05-03 session.

.DESCRIPTION
  Connects directly to .\App\VaultDb\vault.db and INSERTs the well-known
  retirement income/expense rows. Plain unencrypted columns only — no
  vault password required.

  Storage convention:
    monthly_amount_jan2025 = (annual / 12)
    For income captured as weekly rent: annual = weekly * 52, then /12.

  Run AFTER launching the app once and creating the new DB at
  .\App\VaultDb\vault.db (so the schema/migrations are in place).

  Idempotent guard: refuses to insert if any retirement_item row already
  exists, to avoid duplicating data on a second run.

.EXAMPLE
  pwsh tools\seed-retirement.ps1
#>
[CmdletBinding()]
param(
    [string]$DbPath = (Join-Path (Split-Path $PSScriptRoot -Parent) 'App\VaultDb\vault.db')
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path $DbPath)) {
    throw "Database not found at $DbPath. Launch the app first, click Yes on the database-missing prompt, set your vault password, then re-run this script."
}

# Locate Microsoft.Data.Sqlite + native engine. Prefer the app's own build
# output (Release first, then Debug) since it ships the exact set of DLLs
# needed for this runtime; fall back to the nuget cache if neither exists.
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

$conn = New-Object Microsoft.Data.Sqlite.SqliteConnection("Data Source=$DbPath")
$conn.Open()
try {
    $check = $conn.CreateCommand()
    $check.CommandText = "SELECT COUNT(*) FROM retirement_item"
    $existing = [int]$check.ExecuteScalar()
    if ($existing -gt 0) {
        Write-Host "Refusing to seed: retirement_item already has $existing row(s)." -ForegroundColor Yellow
        return
    }

    # (label, kind, annual_or_weekly, isWeekly)
    # Expenses captured as Annual; Income captured as Weekly rent.
    $rows = @(
        @{ label='Council';                       kind='Expense'; v=2200;  weekly=$false },
        @{ label='Electricity';                   kind='Expense'; v=2000;  weekly=$false },
        @{ label='Gas';                           kind='Expense'; v=2000;  weekly=$false },
        @{ label='Water';                         kind='Expense'; v=1600;  weekly=$false },
        @{ label='Grocery & Petrol';              kind='Expense'; v=18000; weekly=$false },
        @{ label='Car Insurance';                 kind='Expense'; v=3000;  weekly=$false },
        @{ label='Property Insurance';            kind='Expense'; v=2000;  weekly=$false },
        @{ label='Health Insurance';              kind='Expense'; v=3500;  weekly=$false },
        @{ label='Unexpected / Leisure Expenses'; kind='Expense'; v=12000; weekly=$false },
        @{ label='Investment Property Expenses';  kind='Expense'; v=12000; weekly=$false },
        @{ label='Internet';                      kind='Expense'; v=1200;  weekly=$false },
        @{ label='Mobile';                        kind='Expense'; v=1200;  weekly=$false },
        @{ label='Gaurdian Vaults';               kind='Expense'; v=1200;  weekly=$false },
        @{ label='Credit Card';                   kind='Expense'; v=5000;  weekly=$false },
        @{ label='Redbank Plains';                kind='Income';  v=840;   weekly=$true  },
        @{ label='Pimpama';                       kind='Income';  v=660;   weekly=$true  }
    )

    $tx  = $conn.BeginTransaction()
    $ins = $conn.CreateCommand()
    $ins.Transaction = $tx
    $ins.CommandText = @"
INSERT INTO retirement_item(label, kind, monthly_amount_jan2025, inflation_pct, indexed)
VALUES (@label, @kind, @monthly, 0, 0);
"@
    [void]$ins.Parameters.Add((New-Object Microsoft.Data.Sqlite.SqliteParameter('@label',   [DBNull]::Value)))
    [void]$ins.Parameters.Add((New-Object Microsoft.Data.Sqlite.SqliteParameter('@kind',    [DBNull]::Value)))
    [void]$ins.Parameters.Add((New-Object Microsoft.Data.Sqlite.SqliteParameter('@monthly', [DBNull]::Value)))

    foreach ($r in $rows) {
        $annual  = if ($r.weekly) { [double]$r.v * 52.0 } else { [double]$r.v }
        $monthly = $annual / 12.0
        $ins.Parameters['@label'].Value   = $r.label
        $ins.Parameters['@kind'].Value    = $r.kind
        $ins.Parameters['@monthly'].Value = $monthly
        [void]$ins.ExecuteNonQuery()
        Write-Host ("  + {0,-32} {1,-7} annual={2,10:N2}  monthly={3,10:N2}" -f $r.label, $r.kind, $annual, $monthly)
    }
    $tx.Commit()
    Write-Host "`nSeeded $($rows.Count) rows into retirement_item." -ForegroundColor Green
}
finally {
    $conn.Dispose()
}

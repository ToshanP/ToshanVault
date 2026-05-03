#requires -Version 5.1
<#
.SYNOPSIS
  Seed the gold_item table with the ornament inventory (last updated 30/01/2022).

.DESCRIPTION
  Connects to .\App\VaultDb\vault.db and INSERTs gold ornament rows.
  Run AFTER the app has created the DB (migrations applied).

  Idempotent: refuses if any gold_item row already exists.

.EXAMPLE
  pwsh tools\seed-gold.ps1
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
    $check.CommandText = "SELECT COUNT(*) FROM gold_item"
    $existing = [int]$check.ExecuteScalar()
    if ($existing -gt 0) {
        Write-Host "Refusing to seed: gold_item already has $existing row(s)." -ForegroundColor Yellow
        return
    }

    # 1 tola = 11.6638 grams
    $tolaToGrams = 11.6638

    # (item_name, purity, qty, tola)
    # Diamond items default to 18K, all others to 22K
    $rows = @(
        @{ name='Sangam (Mum)';                     purity='22K'; qty=1;  tola=12   },
        @{ name='Tortoise Set';                     purity='22K'; qty=1;  tola=1.5  },
        @{ name='Elephant teeth set';               purity='22K'; qty=1;  tola=3    },
        @{ name='Bangles';                          purity='22K'; qty=14; tola=15   },
        @{ name='Magalsutra';                       purity='22K'; qty=1;  tola=1    },
        @{ name='Mangalsutra Set';                  purity='22K'; qty=1;  tola=4    },
        @{ name='Ear rings';                        purity='22K'; qty=11; tola=5    },
        @{ name='Choker Set';                       purity='22K'; qty=1;  tola=2    },
        @{ name='Bracelet with ring';               purity='22K'; qty=1;  tola=1    },
        @{ name='Jadtar set (Red / Green)';         purity='22K'; qty=1;  tola=2    },
        @{ name='Jadtar set';                       purity='22K'; qty=1;  tola=2    },
        @{ name='Neckless (Tineben''s)';            purity='22K'; qty=1;  tola=2    },
        @{ name='Lucky (Toshan)';                   purity='22K'; qty=1;  tola=2    },
        @{ name='Rudrask neckless';                 purity='22K'; qty=1;  tola=1    },
        @{ name='Rudrask lucky';                    purity='22K'; qty=1;  tola=1    },
        @{ name='Chains';                           purity='22K'; qty=9;  tola=9    },
        @{ name='Ruby set';                         purity='22K'; qty=1;  tola=1    },
        @{ name='Emerald Safair set';               purity='22K'; qty=1;  tola=1    },
        @{ name='Safair Set';                       purity='22K'; qty=1;  tola=1    },
        @{ name='Big Pendant set';                  purity='22K'; qty=1;  tola=2    },
        @{ name='Necklace Set (Gold)';              purity='22K'; qty=1;  tola=2    },
        @{ name='Pendants';                         purity='22K'; qty=8;  tola=1.5  },
        @{ name='Kidia Neckless';                   purity='22K'; qty=3;  tola=0.5  },
        @{ name='Bracelets';                        purity='22K'; qty=5;  tola=6    },
        @{ name='Devu Gold Watch Bracelet';         purity='22K'; qty=1;  tola=3    },
        @{ name='Sathiya Set';                      purity='22K'; qty=1;  tola=1.5  },
        @{ name='Anklet';                           purity='22K'; qty=1;  tola=1    },
        @{ name='Prachi 18 Kadu';                   purity='18K'; qty=1;  tola=1    },
        @{ name='Diamond Mangulsutra';              purity='18K'; qty=1;  tola=1    },
        @{ name='Diamond Set';                      purity='18K'; qty=1;  tola=1    },
        @{ name='Diamond Earrings (Mum)';           purity='18K'; qty=1;  tola=0.5  },
        @{ name='Diamond Ring (Dad)';               purity='18K'; qty=1;  tola=0.5  },
        @{ name='Diamond Ring (Mum)';               purity='18K'; qty=1;  tola=0.5  },
        @{ name='Diamond Engagement Ring';          purity='18K'; qty=1;  tola=0.5  },
        @{ name='Diamond Bangle';                   purity='18K'; qty=1;  tola=0.5  },
        @{ name='Diamond Earring';                  purity='18K'; qty=1;  tola=0.5  },
        @{ name='Diamond Kadi';                     purity='18K'; qty=1;  tola=0.5  },
        @{ name='Diamond Pendant Earning Set';      purity='18K'; qty=1;  tola=3    },
        @{ name='Devu Bracelet';                    purity='22K'; qty=1;  tola=1    },
        @{ name='Prachi & Saloni Pendant';          purity='22K'; qty=2;  tola=1    },
        @{ name='Devu Neckless (Christmas Gift)';   purity='22K'; qty=1;  tola=1    },
        @{ name='Saloni Kanthi';                    purity='22K'; qty=1;  tola=1    },
        @{ name='Toshan 50th B''Day Gift Chain (From Foi)';             purity='22K'; qty=1; tola=1   },
        @{ name='Toshan 50th B''Day Gift Chain (From Mum)';             purity='22K'; qty=1; tola=1   },
        @{ name='Devu 40th B''Day Gift Bracelet (From Tinaben and Dakshafoi)'; purity='22K'; qty=1; tola=0.5 },
        @{ name='Devu 40th B''Day Gift Bracelet (From Mum)';           purity='22K'; qty=1; tola=1.5 },
        @{ name='Sathiya Set (From Foi)';           purity='22K'; qty=1;  tola=1.5  },
        @{ name='Tulsi Kanthi (From Foi)';          purity='22K'; qty=1;  tola=2    },
        @{ name='Rudrask Bracelet (from Mum)';      purity='22K'; qty=1;  tola=0.5  },
        @{ name='Niruben Mang Tiko';                purity='22K'; qty=1;  tola=0.5  },
        @{ name='Toshan Rings (Currently on Toshan''s fingers)'; purity='22K'; qty=2; tola=1 },
        @{ name='Devu Ring (Wenty)';                purity='22K'; qty=1;  tola=0.5  },
        @{ name='Devu Birthday gift Necklace';      purity='22K'; qty=1;  tola=1    },
        @{ name='Kadu (Dubai shop)';                purity='22K'; qty=1;  tola=3    }
    )

    $tx = $conn.BeginTransaction()
    $count = 0
    foreach ($r in $rows) {
        $grams = [math]::Round($r.tola * $tolaToGrams, 2)
        $cmd = $conn.CreateCommand()
        $cmd.Transaction = $tx
        $cmd.CommandText = "INSERT INTO gold_item (item_name, purity, qty, tola, grams_calc, notes) VALUES (@name, @purity, @qty, @tola, @grams, NULL)"
        $cmd.Parameters.Add((New-Object Microsoft.Data.Sqlite.SqliteParameter('@name',   $r.name)))   | Out-Null
        $cmd.Parameters.Add((New-Object Microsoft.Data.Sqlite.SqliteParameter('@purity', $r.purity))) | Out-Null
        $cmd.Parameters.Add((New-Object Microsoft.Data.Sqlite.SqliteParameter('@qty',    $r.qty)))    | Out-Null
        $cmd.Parameters.Add((New-Object Microsoft.Data.Sqlite.SqliteParameter('@tola',   $r.tola)))   | Out-Null
        $cmd.Parameters.Add((New-Object Microsoft.Data.Sqlite.SqliteParameter('@grams',  $grams)))    | Out-Null
        $cmd.ExecuteNonQuery() | Out-Null
        $count++
        Write-Host ("  + {0,-55} {1}  qty={2,-3} tola={3,-5} grams={4}" -f $r.name, $r.purity, $r.qty, $r.tola, $grams)
    }
    $tx.Commit()
    Write-Host "`nSeeded $count rows into gold_item." -ForegroundColor Green
}
finally {
    $conn.Close()
}

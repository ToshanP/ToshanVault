# Toshan Personal Vault — Project Instructions

> **Single source of truth.** Update this file whenever decisions change, packages change,
> issues are discovered/resolved, or phases complete. Newest history at the top.

---

## 1. Purpose

A single-user, single-PC Windows 11 desktop app that replaces `Toshan.xlsx` and provides
a searchable, encrypted, portable home for:

- **Budget** — monthly budget, retirement forecast (Jan 2025 baseline), gold ornaments
- **Vault (ImportantDetails)** — bank accounts, website logins, memberships, secret Q&A
- **Recipes** — YouTube-based recipe library with notes
- **Closed Accounts** — archive of closed bank accounts

---

## 2. Confirmed Decisions

| # | Topic | Decision |
|---|-------|----------|
| D1 | Deployment | Single-user / single-PC; portable folder so it can be copied to another PC |
| D2 | Auth | Windows Hello primary; password fallback `!Arvind@Nivas83!` |
| D3 | Password storage | PBKDF2-SHA256 verifier (≥ 200k iterations) embedded in DB only — **never** plaintext in source/binary |
| D4 | Storage | SQLite single file. Sensitive fields encrypted with AES-GCM (per-row IV); KEK derived from password, wraps a random DEK |
| D5 | Hello binding | DPAPI-wrapped KEK protected by KeyCredentialManager signature for Hello unlock |
| D6 | Auto-lock | Default 10 min idle (configurable in Settings) |
| D7 | Clipboard | Auto-clear 30 s after revealing a secret |
| D8 | Importer | One-time wizard reading `Toshan.xlsx` via ClosedXML |
| D9 | Gold price | Live API (goldapi.io). Cached locally. Manual override allowed. API key stored encrypted in `meta` table |
| D10 | Target OS | Windows 11 only |
| D11 | .NET | **.NET 10** (user request, supersedes earlier .NET 8 plan) |
| D12 | Project layout | App / Core / Data / Importer / Tests |
| D13 | DataGrid | `CommunityToolkit.WinUI.UI.Controls.DataGrid` v7.1.2 (v8 toolkit dropped DataGrid) |
| D14 | Charts | `LiveChartsCore.SkiaSharpView.WinUI` 2.0 RC |
| D15 | Markdown notes | `Markdig` (recipes notes rendering) |

---

## 3. Architecture

```
ToshanVault.sln  (slnx format)
├── src/
│   ├── ToshanVault.App         WinUI 3 — Views, ViewModels, navigation, DI host
│   ├── ToshanVault.Core        Domain models, services, encryption, calc engines
│   ├── ToshanVault.Data        SQLite repos (Dapper), migrations, encrypted-field helpers
│   └── ToshanVault.Importer    ClosedXML readers + map-to-DB logic
└── tests/
    └── ToshanVault.Tests       MSTest WinUI test app (crypto / importer / calc)
```

**Layering rules**
- `Core` references nothing internal.
- `Data` references `Core`.
- `Importer` references `Core` + `Data`.
- `App` references `Core` + `Data` + `Importer`.
- `Tests` references `Core` + `Data` + `Importer`.

---

## 4. Project Structure / File Locations

```
C:\Work\ToshanVault\
├── project-instructions.md              (this file — source of truth)
├── Book1.xlsx                           (recipe source spreadsheet)
├── ToshanVault.slnx
├── App\                                 (single-file publish output)
│   ├── ToshanVault.App.exe              (~98 MB self-extracting)
│   ├── appsettings.json                 (DB path config — preserved across publishes)
│   └── VaultDb\vault.db                 (live database — preserved across publishes)
├── src\
│   ├── ToshanVault.App\
│   ├── ToshanVault.Core\
│   ├── ToshanVault.Data\
│   └── ToshanVault.Importer\
├── tests\
│   └── ToshanVault.Tests\
└── tools\
    ├── publish-single.ps1               (single-file publish script)
    ├── seed-retirement.ps1              (seed 16 retirement rows)
    ├── seed-gold.ps1                    (seed 54 gold ornament rows)
    ├── seed-recipes.ps1                 (seed 80 recipes from SQL)
    ├── seed-recipes.sql                 (generated recipe INSERT statements)
    └── parse-recipes-xlsx.js            (Excel→SQL converter for recipes)
```

Plan + todo tracking live in session workspace:
`C:\Users\tpate\.copilot\session-state\face5148-645d-4b5e-b4e8-1b138e9a16e9\plan.md`

---

## 5. Tech Stack & NuGet Packages

| Project | Package | Version | Purpose |
|---------|---------|---------|---------|
| App | `CommunityToolkit.Mvvm` | 8.4.0 | Source-gen MVVM |
| App | `CommunityToolkit.WinUI.UI.Controls.DataGrid` | 7.1.2 | Data grids |
| App | `CommunityToolkit.WinUI.Controls.SettingsControls` | 8.2.250402 | Settings page cards |
| App | `Microsoft.Extensions.DependencyInjection` | 9.0.0 | DI container |
| App | `LiveChartsCore.SkiaSharpView.WinUI` | 2.0.0-rc5.4 | Retirement projection chart |
| App | `Markdig` | 0.38.0 | Recipe notes markdown |
| Core | `CommunityToolkit.Mvvm` | 8.4.0 | Observable models |
| Data | `Microsoft.Data.Sqlite` | 9.0.0 | SQLite ADO |
| Data | `Dapper` | 2.1.66 | Lightweight ORM |
| Importer | `ClosedXML` | 0.104.2 | Excel reader |
| Tests | `FluentAssertions` | 6.12.2 | Test assertions |

**Target framework:** `net10.0-windows10.0.26100.0` (App + Tests),
`net10.0` (Core / Data / Importer libs).
**Min Windows version:** 10.0.17763 (template default; we only support 11 in practice).

---

## 6. Security Model (detail)

1. **First run** — user sets a master password (initial value: `!Arvind@Nivas83!`).
   - Random 16-byte salt generated and stored in `meta(salt)`.
   - PBKDF2(SHA256, 200k iterations, salt) derives a 32-byte KEK.
   - 32-byte DEK randomly generated and AES-GCM-encrypted with KEK; stored in
     `meta(dek_wrapped, dek_iv, dek_tag)`.
   - PBKDF2 verifier (separate iteration) stored in `meta(pwd_verifier)` so wrong
     passwords are rejected before attempting decryption.
2. **Subsequent runs** — password → derive KEK → unwrap DEK.
3. **Encrypted fields** — every value in `vault_field.value_enc` is AES-GCM(DEK, per-row IV).
4. **Hello opt-in** — once enrolled, KEK is wrapped a second way:
   `KeyCredentialManager.RequestSignAsync(challenge)` produces a deterministic signature
   per-PC; the signature is run through HKDF to produce a wrapping key, which encrypts
   the KEK. Stored in `meta(hello_blob)`. On Hello sign-in, app re-signs the same challenge,
   re-derives the wrapping key, unwraps KEK.
5. **Auto-lock** — `IdleDetector` watches input via `GetLastInputInfo`. After threshold,
   navigate to lock page and zero out KEK/DEK in memory.
6. **Clipboard auto-clear** — on copy, schedule a `Task.Delay(30s)` then `Clipboard.Clear()`
   only if clipboard hash matches the value we set (avoid clearing user's later copy).
7. **Backups** — exported as encrypted `.tvbackup` blob (AES-GCM with KEK + magic header).
   File can be restored on another PC if user knows the master password.

---

## 7. Data Model (SQLite)

```sql
-- Header
meta(key TEXT PRIMARY KEY, value BLOB);
-- keys: schema_ver, salt, pwd_verifier, pwd_verifier_iter, kek_iter,
--       dek_wrapped, dek_iv, dek_tag,
--       hello_blob (nullable), gold_api_key_enc, gold_api_iv, idle_minutes, clip_seconds
-- Encoding:
--   schema_ver, pwd_verifier_iter, kek_iter — 4-byte big-endian int (BLOB)
--   salt, pwd_verifier, dek_wrapped, dek_iv, dek_tag, hello_blob — raw bytes

-- Budget
budget_category(id INTEGER PK, name TEXT, type TEXT);     -- Income/Fixed/Variable
budget_item(id INTEGER PK, category_id INTEGER, label TEXT, amount REAL,
            frequency TEXT, notes TEXT, sort_order INTEGER);

retirement_item(id INTEGER PK, label TEXT, kind TEXT,            -- Income/Expense
                monthly_amount_jan2025 REAL, inflation_pct REAL, indexed INTEGER,
                start_age INTEGER NULL, end_age INTEGER NULL, notes TEXT);

gold_item(id INTEGER PK, item_name TEXT, purity TEXT, qty REAL,
          tola REAL, grams_calc REAL, notes TEXT);
gold_price_cache(currency TEXT PK, price_per_gram_24k REAL, fetched_at TEXT);

-- Vault (encrypted columns marked _enc)
vault_entry(id INTEGER PK, kind TEXT, name TEXT, category TEXT, tags TEXT,
            updated_at TEXT, created_at TEXT);
vault_field(id INTEGER PK, entry_id INTEGER, label TEXT,
            value_enc BLOB, iv BLOB, tag BLOB, is_secret INTEGER);

-- Recipes
recipe(id INTEGER PK, title TEXT, author TEXT, cuisine TEXT, rating INTEGER,
       youtube_url TEXT, thumbnail_path TEXT, notes_md TEXT, is_favourite INTEGER,
       is_tried INTEGER DEFAULT 0, category TEXT DEFAULT 'Other',
       added_at TEXT);
recipe_tag(recipe_id INTEGER, tag TEXT, PRIMARY KEY(recipe_id, tag));

-- Bank accounts (active + closed in one table; Phase 5c)
bank_account(id INTEGER PK,
             bank TEXT NOT NULL, account_name TEXT NOT NULL,
             bsb TEXT, account_number TEXT,
             account_type TEXT NOT NULL CHECK(account_type IN
               ('Savings','Cheque','TermDeposit','Offset','CreditCard','Mortgage','Other')),
             holder_name TEXT,
             interest_rate_pct REAL,                 -- nullable, used by future mortgage / retirement plan
             notes TEXT,
             is_closed INTEGER NOT NULL DEFAULT 0,   -- 0=active, 1=closed
             closed_date TEXT, close_reason TEXT,
             vault_entry_id INTEGER REFERENCES vault_entry(id) ON DELETE SET NULL,
             created_at TEXT NOT NULL, updated_at TEXT NOT NULL);
-- indexes: ix_bank_account_is_closed, ix_bank_account_vault_entry_id
-- Internet-banking credentials (username, client_id, password, up to 10 Q/A) live
-- in vault_entry/vault_field (kind='bank_login') and are encrypted at rest per §6.
-- The legacy closed_account stub from earlier drafts is dropped in migration 003.

-- Bank-account internet-banking credentials (Phase 5c.5 — multi-owner extension)
-- One bank_account can have N credential rows (e.g. "Toshan", "Devangini") so
-- joint accounts can store both spouses' logins independently. Migration 006.
bank_account_credential(id INTEGER PK,
    bank_account_id INTEGER NOT NULL REFERENCES bank_account(id) ON DELETE CASCADE,
    owner_label TEXT NOT NULL,                  -- "Toshan" / "Devangini" / etc
    vault_entry_id INTEGER REFERENCES vault_entry(id) ON DELETE SET NULL,
    created_at TEXT NOT NULL, updated_at TEXT NOT NULL,
    UNIQUE(bank_account_id, owner_label));
-- AFTER DELETE trigger trg_bank_credential_after_delete cascades vault_entry
-- removal so unlinked credential rows don't leave orphan vault entries.
-- vault_entry kinds for credentials: 'bank_login' (Bank), 'web_login' (Vault),
-- 'insurance_login' (Insurance). All sit on the same vault_field machinery.
-- Per-kind label namespaces prevent collision: 'bank_login.*', no prefix for
-- web_login (legacy), 'insurance.*' for insurance_login.

-- Polymorphic attachments (Phase 5d — migration 009; CHECK extended in 010)
-- AES-GCM encrypted payload via the same vault DEK; never stored in plaintext.
attachment(id INTEGER PK,
    target_kind TEXT NOT NULL CHECK(target_kind IN
       ('bank_account','vault_entry','insurance')),  -- 'insurance' added in 010
    target_id INTEGER NOT NULL,
    file_name TEXT NOT NULL, mime_type TEXT,
    size_bytes INTEGER NOT NULL CHECK(size_bytes >= 0),
    iv BLOB NOT NULL, ciphertext BLOB NOT NULL, tag BLOB NOT NULL,
    created_at TEXT NOT NULL);
-- index ix_attachment_target(target_kind, target_id)
-- AFTER DELETE triggers cascade: trg_attachment_after_{bank,vault,insurance}_delete
-- Migration 010 rebuilds the table (SQLite cannot ALTER a CHECK) preserving rows.

-- Insurance (Phase 5e — first-class entity, migration 010)
insurance(id INTEGER PK,
    insurer_company TEXT NOT NULL, policy_number TEXT,
    insurance_type TEXT,                        -- free text: Health/Car/Home/...
    website TEXT, renewal_date TEXT,            -- ISO yyyy-MM-dd, nullable
    owner TEXT,                                 -- policy owner (free text dropdown)
    notes TEXT,                                 -- plaintext RTF notes (migrated from vault_field)
    vault_entry_id INTEGER REFERENCES vault_entry(id) ON DELETE SET NULL,
    sort_order INTEGER NOT NULL DEFAULT 0,
    created_at TEXT NOT NULL, updated_at TEXT NOT NULL);
-- indexes: ix_insurance_renewal, ix_insurance_vault_entry
-- AFTER DELETE trigger trg_insurance_after_delete removes the linked
-- vault_entry of kind='insurance_login' (cascades to its vault_field rows).

-- Insurance multi-owner credentials (Phase 5f — migration 019)
-- Mirrors bank_account_credential pattern. One policy can have N credential
-- rows (one per family member who logs in to the insurer's portal).
insurance_credential(id INTEGER PK,
    insurance_id INTEGER NOT NULL REFERENCES insurance(id) ON DELETE CASCADE,
    owner TEXT NOT NULL,                        -- "Toshan" / "Devangini" / etc
    vault_entry_id INTEGER NOT NULL REFERENCES vault_entry(id) ON DELETE CASCADE,
    created_at TEXT NOT NULL, updated_at TEXT NOT NULL,
    UNIQUE(insurance_id, owner));
-- index ix_insurance_credential_insurance, ix_insurance_credential_entry
-- AFTER DELETE trigger trg_insurance_credential_after_delete removes linked vault_entry.
-- Back-fill: existing insurance.vault_entry_id rows → owner='Toshan'.

-- Vault item multi-owner credentials (Phase 5f — migration 020)
-- Each vault_entry (kind='web_login') can have N credential rows for different owners.
web_credential(id INTEGER PK,
    entry_id INTEGER NOT NULL REFERENCES vault_entry(id) ON DELETE CASCADE,
    owner TEXT NOT NULL,                        -- "Toshan" / "Devangini" / etc
    vault_entry_id INTEGER NOT NULL REFERENCES vault_entry(id),
    created_at TEXT NOT NULL, updated_at TEXT NOT NULL,
    UNIQUE(entry_id, owner));
-- index ix_web_credential_entry, ix_web_credential_vault
-- AFTER DELETE trigger trg_web_credential_after_delete removes linked vault_entry
-- ONLY IF vault_entry_id != entry_id (legacy back-filled rows point to self).
-- Back-fill: existing entries with username/password fields → owner='Toshan'.

 -- Mint Investment (migration 022)
-- Operational Perth Mint-style schedule, separate from jewellery inventory and
-- retirement projections. $500/fortnight funding accumulates in the model until
-- it can fund the configured 1 oz working unit; ticking a purchase records
-- physical ounces and reduces calculated Mint account cash.
mint_investment_plan(id INTEGER PK CHECK (id=1),
    enabled INTEGER NOT NULL DEFAULT 1,
    account_start_date TEXT NOT NULL,
    fortnightly_contribution_aud REAL NOT NULL DEFAULT 500,
    working_unit_ounces REAL NOT NULL DEFAULT 1,
    price_per_ounce_aud REAL NOT NULL,
    reminder_lead_days INTEGER NOT NULL DEFAULT 14,
    consolidation_target_ounces REAL NOT NULL DEFAULT 10,
    notes TEXT);
mint_investment_purchase(due_date TEXT PK,
    completed_date TEXT NOT NULL,
    ounces REAL NOT NULL,
    price_per_ounce_aud REAL NOT NULL,
    notes TEXT);
-- Retirement Planning mortgage payoff (migration 023)
-- Payoff projection uses entered minimum repayment + additional repayment per
-- period. `term_years` remains for original-loan reference/dashboard display,
-- but it no longer drives the period payment used by the calculator.
retirement_plan.minimum_payment_per_period REAL NOT NULL DEFAULT 0;
```

Migrations live in `Data\Migrations\*.sql` and are applied in order based on
`meta(schema_ver)`.

---

## 8. UI Map (final — see `UI-Mockups.md` for sketches)

1. Login (Hello + password)
2. Dashboard (KPI tiles + recent activity)
3. Budget → Monthly | Retirement | Gold (Pivot inside page)
4. Vault — grouped by category (collapsible banners), owner-initial credential avatars per entry
5. Recipes — GridView + WebView2 detail
6. Bank Accounts — list with masked BSB/acct#, owner-initial credential avatars, edit / notes popup / close
7. Insurance — renewal countdown tiles, owner-initial credential avatars with Q&A
8. Mint Investment — Perth Mint account funding schedule, tickable 1 oz purchases, Mint cash + physical ounces summary
9. Settings — security / data / integrations / about
10. First-run Import Wizard

**Shared UI components:**
- **NotesWindow** — standalone `Window` (not ContentDialog) for full-height rich text editing. Called via `NotesWindow.ShowAsync(title, existingRtf)` → returns `(bool saved, string? newRtf)`. Used by: Bank Accounts, Vault items, Insurance policies, General Notes.
- **OwnerPickerDialog** — `ContentDialog` listing available owners (from `KnownOwners` minus existing). Used by: Bank Accounts, Insurance, Vault before opening a credential dialog.
- **RichNotesField** — reusable `UserControl` with RichEditBox + toolbar (bold/italic/underline, font, size, colours). Wrapped inside NotesWindow for the full-height experience.
- **AttachmentsPanel** — reusable panel with file-picker + decrypt-open + delete. Used by Bank, Vault, Insurance edit dialogs.
- **SecretFieldHelpers** — static helper to add PasswordBox with reveal toggle to any panel.

---

## 9. Phases & Status (latest first)

> Status legend: ⏳ pending · 🔧 in progress · ✅ done · ⚠ blocked

| # | Phase | Status | Notes |
|---|-------|--------|-------|
| 1 | scaffold | ✅ done | Solution + 5 projects + NuGet packages added; build green at x64; rename TosanVault→ToshanVault complete (2026-05-02). I1 + I2 resolved; see §11. |
| 2 | auth-encryption | ✅ done | `ToshanVault.Core/Security/*` (PBKDF2 KEK + verifier, AES-GCM, Vault lifecycle, exceptions) + `ToshanVault.Data/Schema/*` (MigrationRunner, MetaRepository, 001_init.sql for `meta` table). 26/26 tests pass; `dotnet test` working (resolves I6 for non-UI tests). Three-reviewer adversarial pass triggered hardening (DB-level write-once via plain INSERT, iter-count pinning, init zeroise-on-failure, 4-byte schema_ver, VaultMeta IDisposable, Hello extension seam). UI integration (login screen, idle lock, clipboard timer) deferred to later phases. |
| 3 | data-layer | ✅ done | All 9 §7 tables in `002_data.sql`, domain models, Dapper repos (with snake↔Pascal mapping, enum-as-text, ISO DateTimeOffset). Adversarial review by 3 models (gpt-5.3-codex, claude-opus-4.6, gpt-5.4) — all FIX-AND-SHIP. Fixes: (a) `gold_price_cache` UPSERT guarded by `excluded.fetched_at > current` so stale fetches can't clobber fresh; (b) `budget_item.category_id` FK switched from `ON DELETE CASCADE` → `ON DELETE RESTRICT` to prevent silent child loss; (c) `VaultFieldRepository` Insert/Update wrap UTF-8 plaintext in try/finally + `CryptographicOperations.ZeroMemory` (matches existing decrypt-path discipline); (d) `RecipeRepository.SetTagsAsync` adds explicit catch + `RollbackAsync`; (e) dropped `ux_budget_category_name` (not in spec). Deferred: I8 (Vault TOCTOU + IClock), I9 (MetaRepository generic API), I10 (cross-repo unit-of-work). 43/43 tests pass; build green. |
| 4 | importer | 🔧 partial / ⚠ blocked | `XlsxSanitizer` shipped + 7 tests pass. Strips `xl/drawings/`, `xl/media/`, `xl/charts/`, `xl/embeddings/`, `xl/diagrams/` parts and patches `[Content_Types].xml`, sheet `.rels`, sheet XML `<drawing>/<legacyDrawing>/<legacyDrawingHF>/<picture>` refs. Works around ClosedXML 0.104.2/0.105.0 picture-name validation crash on real `Toshan.xlsx`. Sheet readers + `ImportService` **paused** pending user re-organization of `Toshan.xlsx` into one-sheet-per-table with headers (3 of 4 sheets are free-form text blocks — see I11). Adversarial review (gpt-5.3-codex, FIX-AND-SHIP) closed: added `<legacyDrawingHF>` strip. 50/50 tests pass. |
| 5e | insurance | ✅ done | First-class `insurance` entity (migration 010): insurer/policy#/type (free text)/website/renewal_date + nullable FK to `vault_entry` (kind=`insurance_login`) for credentials. `InsuranceRepository` (Dapper CRUD, sorted by renewal nulls-last); `InsuranceCredentialsService` mirrors `WebCredentialsService` with `insurance.*` label prefix and lazy vault_entry creation on first non-empty save; supports username/password/notes (single set, not multi-owner — joint policies deferred). Attachment table CHECK extended via 12-step rename-rebuild-copy recipe (preserves all existing rows + recreates indexes/triggers); new `trg_attachment_after_insurance_delete` and `trg_insurance_after_delete` cascades. New `DateOnly`/`DateOnly?` Dapper handlers in `DapperSetup` for `renewal_date`. `InsurancePage` (search + tile grid; renewal countdown badge — red ≤30d, amber ≤60d), `InsuranceDialog` (with attachments panel), `InsuranceCredentialsDialog` (username/password/RichNotes). Wired into `MainShellPage` (shield glyph \uE83D) and DI in `AppHost`. Build green; **84/84 tests pass** (+8 new InsuranceRepository + InsuranceCredentialsService tests covering round-trip, FK SET NULL on entry delete, cascade on insurance delete, lazy-no-create on empty save, narrow LoadLabels). |
| 5f | multi-owner-credentials | ✅ done | Extended Insurance and Vault to multi-owner credentials (matching Bank Accounts pattern). Migration 019 (insurance_credential) + 020 (web_credential). Services rewritten: `InsuranceCredentialsService.SaveAsync(insuranceId, owner, entryName, fields)` and `WebCredentialsService.SaveCredentialsAsync(entryId, owner, entryName, fields)`. New repos: `InsuranceCredentialRepository`, `WebCredentialRepository`. UI: owner-initial avatar buttons with `OwnerPickerDialog`, delete support. Insurance dialog now includes Q&A (10 pairs). All existing credentials back-filled as owner='Toshan'. Build green; **129/129 tests pass**. |
| 5g | notes-popup | ✅ done | Extracted notes editing into standalone `NotesWindow` (separate `Window`, not ContentDialog) for full-height rich text editing. Notes icon buttons replace inline notes in edit dialogs. Insurance.notes column added (plaintext, migration via `MigrateNotesToColumnAsync` decrypts old vault_field notes). Notes removed from Bank Account edit dialogs, Insurance edit dialogs — now accessed via dedicated icon on each tile. Build green; tests pass. |
| 5d | attachments + rich notes + multi-owner bank + tile previews | ✅ done | Several user-driven UX deltas merged (post-5c, pre-5e): (1) **Attachments** — migration 009 polymorphic `attachment` table; `AttachmentService` AES-GCM encrypts payloads with the vault DEK; `AttachmentsPanel` reusable WinUI control with file-picker + open-temp-decrypted + delete; wired into `BankAccountDialog` and `VaultEntryDialog` (existing rows only). (2) **Rich notes** — `RichNotesField` reusable RichEditBox wrapper with bold/italic/underline + font + size + foreground/background colour pickers; persisted as RTF in encrypted vault_field rows (`varbinary(max)`-equivalent BLOB). Bank Notes + Bank-credentials Notes + Vault Additional-Details all migrated to RichNotesField. (3) **Multi-owner bank credentials** — migration 006 `bank_account_credential` table + label `bank_login.*` namespace; `BankCredentialsService` rewritten for owner-keyed save/load; tile actions show male/female icons (\uE13D / \uE13E) per owner instead of "Edit/Toshan/Devangini" buttons. (4) **Bank dialog adds** — Website + Card PIN + Phone-banking PIN encrypted fields (PINs via `SecretFieldHelpers`). (5) **Vault tile preview** — Number + Website narrow-decrypted via new `WebCredentialsService.LoadLabelsAsync(IReadOnlyCollection<string>)` (Dapper IN expansion); tile grew 240×140→260×170. `VaultPage.OnNavigatedTo` has dedicated `catch (VaultLockedException) { _nav.NavigateToLogin(); }` to defeat the swallow-by-broad-catch H finding. (6) **App-wide UX** — `MainWindow.Maximize` re-asserted on activation (full screen on launch); `LoginPage` master-password autofocus (immediate Focus → Low-priority dispatcher → PasswordBox.Loaded fallbacks); Enter-on-login fires the unlock button; per-tile-page `AutoSuggestBox` search across name/owner/number/website. All work passed Anvil 🟡/🔴 reviews; tests grew 63→76→84. |
| 5c | bank-accounts | ✅ done | Single `bank_account` table with `is_closed` flag (Option A); migration 003 drops legacy `closed_account` stub. Domain: `BankAccount` POCO + `BankAccountType` enum (incl. `Mortgage`; `interest_rate_pct` retained for future retirement plan). Repo: Insert/Update validation, transactional+idempotent `CloseAsync`, `ReopenAsync` (throws if not closed), `GetActive`/`GetClosed`. Internet-banking credentials (username, client_id, password, up to 10 Q/A pairs) stored in vault_entry (kind='bank_login') + vault_field encrypted at rest per §6; FK `vault_entry_id ON DELETE SET NULL`. `BankAccountsPage` (list + Add/Edit/Credentials/Close) with masked BSB (hidden) and account# (last 4); `ClosedAccountsPage` rewritten to show closed rows with Reopen. Three new dialogs (`BankAccountDialog`, `CredentialsDialog`, `CloseConfirmDialog`); credentials dialog uses `UpsertFieldAsync` that deletes empty fields rather than persisting empty encrypted blobs. Build green; 57/57 tests pass (+7 new BankAccountRepository tests, -1 obsolete ClosedAccount test); smoke launch alive ≥8 s; three-reviewer pass per Anvil 🔴 protocol. |
| 5a | app-foundation | ✅ done | `AppHost` (MEDI composition root, idempotent `Build()` from `App.OnLaunched`), `AppPaths` (`%LOCALAPPDATA%\ToshanVault\vault.db`, env override `TOSHANVAULT_DATA_DIR`), `IdleLockService` (1Hz `DispatcherTimer`, `GetLastInputInfo` probe, default 10 min), `NavigationService`, `LoginPage` (first-run vs unlock, runs migrations on `OnNavigatedTo`, 750 ms backoff stub on `WrongPasswordException`), `MainShellPage` (NavigationView with 5 main + Lock/About + Settings, idle-lock event wiring), 5 placeholder pages (Dashboard/Budget/Vault/Recipes/ClosedAccounts). Re-pivoted from Phase 4 (xlsx blocked on free-form spreadsheet) so UI exists to drive data shape. Build green; 50/50 prior tests still pass. Smoke launch verified (required `<WindowsAppSDKSelfContained>true</WindowsAppSDKSelfContained>` — see I12). App test ref dropped: WindowsAppSDK auto-init `<Module>` cctor crashes `dotnet test` (see I12). |
| 5b | budget-pages | ✅ done | Weekly Budget page (Income / Fixed / Variable) shipped |
| 6 | vault-page | ✅ done | Vault with category grouping, coloured banners, drag-drop |
| 7 | recipes-page | ✅ done | Recipes grid with tried marker, category, import from xlsx |
| 8 | closed-accounts-page | ✅ done | Integrated into bank-accounts with is_closed flag |
| 9 | dashboard | ✅ done | 7-tile dashboard with KPIs, backup button, nav integration |
| 10 | settings-backup | ⏳ | |
| 11 | polish-tests | ⏳ | |
| 12 | package | ✅ done | Single-file exe publish via `tools\publish-single.ps1` |

Live todo tracking is in the session SQL DB (`todos`, `todo_deps` tables).

### 9.1 Detailed scaffold progress

| Step | Status |
|------|--------|
| Install Microsoft.WindowsAppSDK.WinUI.CSharp.Templates | ✅ |
| Create `ToshanVault.slnx` | ✅ |
| Create `ToshanVault.App` (winui-navview, net10.0, target 10.0.22621.0) | ✅ |
| Create `ToshanVault.Core` (winui-lib, net10.0) | ✅ |
| Create `ToshanVault.Data` (winui-lib, net10.0) | ✅ |
| Create `ToshanVault.Importer` (winui-lib, net10.0) | ✅ |
| Create `ToshanVault.Tests` (winui-unittest, net10.0, target 10.0.22621.0) | ✅ |
| Add all projects to slnx | ✅ |
| Wire project references (Data→Core, Importer→Core+Data, App→all, Tests→all) | ✅ |
| Add NuGet packages (see §5 table) | ✅ |
| Replace incompatible `CommunityToolkit.WinUI.Controls.DataGrid` with `UI.Controls.DataGrid` 7.1.2 | ✅ |
| Rename project family `TosanVault` → `ToshanVault` (folder, slnx, 5 csprojs, namespaces, code, docs) | ✅ |
| Resolve I2 — `Directory.Build.props` + `<Platforms>x64</Platforms>` in every csproj + slnx `<Configurations>` block + per-project `<Platform Solution="*\|x64" Project="x64" />` mappings | ✅ |
| Resolve I1 — Tests csproj: + MSTest.TestAdapter 3.6.4, MSTest.TestFramework 3.6.4, Microsoft.NET.Test.Sdk 17.11.1, Microsoft.WindowsAppSDK 2.0.1, Microsoft.Windows.SDK.BuildTools 10.0.28000.1839; `<GenerateProgramFile>false</GenerateProgramFile>` + `<IsTestProject>true</IsTestProject>`; fixed typo `WinUIDSKReferences`→`WinUISDKReferences` | ✅ |
| First clean build (`dotnet build ToshanVault.slnx -c Debug -p:Platform=x64`) | ✅ green (0 errors, 2 NU1603 warnings about LiveCharts rc5.4→rc6 resolution) |
| Custom DI host (`AppHost`) wiring | ✅ Phase 5a |
| Replace generated NavigationView shell with our 8-section nav | ✅ Phase 5a (5 main + Lock/About + Settings cog) |
| Theming (Mica, system accent) | ⏳ deferred to a polish pass |
| Migration 006 — `bank_account_credential` (multi-owner) + cascade trigger | ✅ Phase 5d |
| Migration 007–008 — RichNotesField/Vault polish migrations (label renames, indexes) | ✅ Phase 5d |
| Migration 009 — polymorphic `attachment` table + per-target cascade triggers | ✅ Phase 5d |
| Migration 010 — `insurance` table + 12-step rebuild of `attachment` to extend CHECK to `'insurance'` | ✅ Phase 5e |
| `RichNotesField` reusable control (RTF in encrypted BLOB; font/size/colour) | ✅ Phase 5d |
| `AttachmentsPanel` reusable control (encrypted file-picker + decrypt-to-temp + delete) | ✅ Phase 5d |
| `DateOnly`/`DateOnly?` Dapper handlers (`yyyy-MM-dd` TEXT) | ✅ Phase 5e |
| Master-password autofocus + Enter-to-submit on Login | ✅ Phase 5d |
| `MainWindow.Maximize` on launch / activation | ✅ Phase 5d |

---

## 10. Build & Run

```powershell
cd "C:\Work\ToshanVault"
dotnet build ToshanVault.slnx -c Debug -p:Platform=x64
# Run the app:
dotnet run --project src\ToshanVault.App\ToshanVault.App.csproj -c Debug -p:Platform=x64
```

Tests:
```powershell
dotnet test tests\ToshanVault.Tests\ToshanVault.Tests.csproj -c Debug -p:Platform=x64
# 133/133 passing as of 2026-05-03.
```

Single-file publish:
```powershell
pwsh tools\publish-single.ps1
# Output: App\ToshanVault.App.exe (~98 MB) + App\appsettings.json
# Database: App\VaultDb\vault.db (preserved across publishes)
```

Seed scripts (run AFTER app creates the DB on first launch):
```powershell
pwsh tools\seed-retirement.ps1    # 16 retirement income/expense rows
pwsh tools\seed-gold.ps1          # 54 gold ornament rows
node tools\parse-recipes-xlsx.js  # regenerates tools\seed-recipes.sql from Book1.xlsx
pwsh tools\seed-recipes.ps1       # 80 recipes from the generated SQL
```

Only `Debug|x64` and `Release|x64` solution configurations are declared in
`ToshanVault.slnx`. `AnyCPU`/`x86`/`ARM64` were intentionally dropped because
WinUI 3 packaged apps cannot run on `AnyCPU` and we only ship Win 11 x64.

---

## 11. Known Issues / Open Items

| Id | Severity | Issue | Owner action |
|----|----------|-------|--------------|
| I1 | ~~High~~ Resolved 2026-05-02 | Test project missing MSTest + WindowsAppSDK references | Added to `tests\ToshanVault.Tests\ToshanVault.Tests.csproj`: `MSTest.TestAdapter` 3.6.4, `MSTest.TestFramework` 3.6.4, `Microsoft.NET.Test.Sdk` 17.11.1, `Microsoft.WindowsAppSDK` 2.0.1, `Microsoft.Windows.SDK.BuildTools` 10.0.28000.1839. Also set `<GenerateProgramFile>false</GenerateProgramFile>` + `<IsTestProject>true</IsTestProject>` to avoid duplicate `Main` entry point from Test.Sdk, and fixed template typo `WinUIDSKReferences`→`WinUISDKReferences`. |
| I2 | ~~High~~ Resolved 2026-05-02 | App build error: *"Packaged .NET applications with an app host exe cannot be ProcessorArchitecture=AnyCPU"* | Standardised on x64 across the whole solution: created `ToshanVault\Directory.Build.props` (default `<Platforms>x64</Platforms>`), set `<Platforms>x64</Platforms>` explicitly in every csproj, and added a `<Configurations>` block + per-project `<Platform Solution="*\|x64" Project="x64" />` mappings to `ToshanVault.slnx` so `Debug\|x64` is a valid solution configuration. App + Tests `<RuntimeIdentifiers>` reduced to `win-x64`. Build now green. |
| I3 | ~~Med~~ Resolved | `Toshan.xlsx` location | **Found at** `C:\Work\ToshanVault\Toshan.xlsx` (92 KB, dated 2026-05-02). Travels with the project folder when copied to the new PC. |
| I4 | ~~Low~~ Resolved 2026-05-02 | goldapi.io API key not yet provided | Switched to free no-auth APIs: `api.gold-api.com/price/XAU` (USD/oz) + `api.frankfurter.app` (USD→AUD). Cached AUD/g 24K in `gold_price_cache` with 1 h TTL; falls back to last cached value on network failure. Manual override still possible by editing the row in the future Settings page if needed. |
| I5 | Low | App build emits `NETSDK1198: A publish profile with the name 'win-AnyCPU.pubxml' was not found` | Cosmetic only — `<PublishProfile>win-$(Platform).pubxml</PublishProfile>` evaluates fine for `Platform=x64`. Generate `Properties\PublishProfiles\win-x64.pubxml` during the **package** phase. |
| I6 | ~~Med~~ Resolved 2026-05-02 | `dotnet test` against `ToshanVault.Tests` failed at runtime | Re-templated `ToshanVault.Tests` as a non-packaged net10.0-windows MSTest console (no `<UseWinUI>`, no `Package.appxmanifest`, no `UnitTestApp.xaml`). Now executable via plain `dotnet test`; 26/26 passing. UI tests, if ever required, will land in a separate packaged test project. |
| I7 | Low | PBKDF2 KEK = 200 000 / verifier = 100 000 iterations are below current OWASP 2024 baseline (~600 000 SHA-256) | Matches §6 spec and was a deliberate user choice during phase 2 planning (2026-05-02). Defer to a future "crypto-hardening" mini-phase: bump constants, add a one-shot migration that re-derives verifier and re-wraps DEK on next unlock. Iter counts are validated against `CryptoConstants` on read so attacker-driven downgrade via DB tamper is rejected. |
| I8 | Low | `VaultFieldRepository` operations are TOCTOU-vulnerable to concurrent `Vault.Lock()`; auto-stamped `UpdatedAt` uses `DateTimeOffset.UtcNow` directly (no `IClock`/`TimeProvider`) which makes timestamp tests rely on `Task.Delay` | Acceptable for single-user / single-window MVP. Revisit when (a) introducing background workers that may touch the vault, or (b) writing flake-prone time-sensitive tests. Add `TimeProvider` injection + DEK ref-count or repo-level lock guard at that point. |
| I9 | Low | `MetaRepository` only exposes bootstrap-key APIs; no generic typed get/set for non-vault meta keys (`gold_api_key_enc`, `gold_api_iv`, `idle_minutes`, `clip_seconds`, etc.) | Add generic `Get(key)`/`Set(key, value)` API alongside the write-once bootstrap APIs in the **settings-backup** phase (#10). Bootstrap keys must remain write-once via the existing typed API. |
| I10 | Low | Repos always open their own connection; no cross-repo transactional unit-of-work for aggregate writes (vault_entry+fields, recipe+tags batch, importer multi-table inserts) | Acceptable now — only `RecipeRepository.SetTagsAsync` needs intra-repo transactions and already has one. Add a `IUnitOfWork`/transaction-aware overload pattern before **importer** (#4) so it can roll back the whole xlsx ingest on failure. |
| I11 | Med | `Toshan.xlsx` is hand-curated free-form text (3 of 4 sheets blank-row-separated label/value blocks, not tabular); does not map to §7 schema. Full importer (sheet readers + `ImportService`) blocked. | User to manually re-organize source xlsx into one-sheet-per-table with first-row headers matching §7 column names; importer phase resumes after. `XlsxSanitizer` landed independently — needed regardless to defeat ClosedXML drawing-name validation crash on the real file. **2026-05-02 update**: re-organize attempt only changed 2 of 8 sheets (Budget shrank, two new headerless sheets added). User pivoted to "build entry UI first, then re-format spreadsheet to match what UI accepts". |
| I12 | Med | `Microsoft.WindowsAppSDK` 2.0.x emits a `<Module>` static cctor (`AutoInitialize.InitializeWindowsAppSDK`) into every consuming assembly; it calls `Bootstrap.Initialize` on first type load. (a) Crashes `dotnet test` with `COMException 0x80040154 REGDB_E_CLASSNOTREG` if the test project references `ToshanVault.App` — even with `<WindowsAppSdkBootstrapInitialize>false</WindowsAppSdkBootstrapInitialize>` set on the test project (the cctor is baked into App.dll). (b) Crashes the published exe with the same error if the WinAppSDK 2.0 framework runtime isn't installed system-wide on the target machine (only 1.4–1.8 are present on this box). | (a) Don't reference `ToshanVault.App` from `ToshanVault.Tests`; rely on Tier 3 smoke launch for App-only verification (and consider extracting `Hosting/`+`Services/` into a `ToshanVault.AppShared` non-WinUI lib if test coverage of `AppHost`/`IdleLockService` becomes critical). (b) Set `<WindowsAppSDKSelfContained>true</WindowsAppSDKSelfContained>` in `ToshanVault.App.csproj` so `dotnet publish -r win-x64 --self-contained true` bundles the WinAppSDK runtime DLLs (~248 MB output). Confirmed working — published exe stays alive ≥8 s on launch. |
| I13 | ~~High~~ Resolved 2026-05-02 | `FileOpenPicker.PickSingleFileAsync()` on the **Gold Ornaments** Import button threw `COMException 0x80004005 (E_FAIL)` with an empty `Message` — InfoBar showed "Error" with no detail. Same code pattern works on `RecipesPage`, ruling out hwnd init / threading. Suspected cause: `SuggestedStartLocation = PickerLocationId.Desktop` resolves to a OneDrive-redirected folder that the unpackaged WinUI 3 picker cannot enumerate. | (a) Removed `SuggestedStartLocation` from `GoldOrnamentsPage.Import_Click`. (b) Wrapped picker call in inner try/catch → on failure, falls back to the known `C:\Work\ToshanVault\Toshan.xlsx` via the existing "Use Toshan.xlsx?" confirm dialog. (c) Switched all `catch (Exception ex) { ShowError(ex.Message); }` in `GoldOrnamentsPage` to `ShowError(FormatError(ex))` which walks `InnerException` chain + appends log path — so future "blank Error" InfoBars become impossible. (d) Added Serilog file logging app-wide so the next E_FAIL leaves a stack-trace on disk (see §13 Logging). |
| I14 | Low | Recipes page Import xlsx may hit the same `0x80004005` if user environment has the Desktop redirection issue | Apply the same picker-fallback pattern to `RecipesPage.Import_Click` as a follow-up. Currently working in this user's environment so deferred. |

---

## 12. Decision Log (chronological)

| Date | Change | Reason |
|------|--------|--------|
| 2026-05-02 | Initial plan + UI mockups approved | User confirmed |
| 2026-05-02 | Switched .NET 8 → .NET 10 | User request |
| 2026-05-02 | Switched DataGrid pkg `Controls.DataGrid` 8.x (incompatible) → `UI.Controls.DataGrid` 7.1.2 | v8 toolkit dropped DataGrid; 7.1.2 is the supported WinUI 3 version |
| 2026-05-02 | Created this `project-instructions.md` as living SoT | User request |
| 2026-05-02 | Session paused mid-scaffold with two known build errors (I1, I2) documented for resumption | User pause |
| 2026-05-02 | Project being moved to a different PC; §15 rewritten as a migration checklist | User decision |
| 2026-05-02 | Confirmed `Toshan.xlsx` location at `C:\Work\ToshanVault\Toshan.xlsx` (closes I3) | File listing |
| 2026-05-02 | Migrated to new PC; PowerShell 7 installed; `dotnet 10.0.107` SDK present | New environment |
| 2026-05-02 | Renamed project family `TosanVault` → `ToshanVault` (folder, slnx, 5 csprojs, namespaces, code, docs) | User request — correct spelling |
| 2026-05-02 | Standardised solution on x64 only; declared `<Configurations>` + per-project `<Platform Solution="*\|x64" Project="x64" />` mappings in slnx; added `Directory.Build.props` | Resolved I2; required because slnx-to-metaproj only honours configurations declared in the slnx itself, regardless of csproj `<Platforms>` |
| 2026-05-02 | Added MSTest stack + `Microsoft.NET.Test.Sdk` 17.11.1 to Tests; `<GenerateProgramFile>false</GenerateProgramFile>` to avoid duplicate `Main` | Resolved I1 |
| 2026-05-02 | Build green (`dotnet build ToshanVault.slnx -c Debug -p:Platform=x64`); scaffold phase marked done | Verified |
| 2026-05-02 | **Phase 2 (auth-encryption) plan agreed** with user: PBKDF2-SHA256 KEK 200k / verifier 100k (per §6), built-in `System.Security.Cryptography` only, in-memory SQLite for tests, library `RootNamespace` switched to dotted form (e.g. `ToshanVault.Core`), `ToshanVault.Tests` re-templated as non-packaged MSTest console. UI integration (login screen, idle lock, clipboard timer, Hello binding) explicitly deferred to later phases | User decisions during planning |
| 2026-05-02 | Tests project re-templated to non-packaged MSTest console | Resolves I6 for non-UI tests; enables `dotnet test` |
| 2026-05-02 | Library projects renamed RootNamespace `ToshanVault_X` → `ToshanVault.X`, `<UseWinUI>` removed (libs don't need it) | Conventional .NET namespacing; libs are pure |
| 2026-05-02 | Phase 2 implementation green (build + 26/26 tests). Adversarial review by 3 models surfaced 8 findings; 7 acted on, 1 deferred (I7 — see §11). Hardening: (a) `MetaRepository.WriteInitialAsync` switched from `INSERT OR REPLACE` to plain `INSERT` so DB UNIQUE constraint enforces write-once vault material, surfacing as `VaultAlreadyInitialisedException`; (b) `Vault.UnlockAsync` pins iteration counts to `CryptoConstants` and throws `TamperedDataException` on mismatch — defeats DB-tamper KDF downgrade/DoS; (c) `Vault.InitialiseAsync` wraps key derivation in `try/finally` and zeroises kek/dek/verifier locals on failure; (d) `MigrationRunner` schema_ver promoted from 1-byte to 4-byte big-endian int (with backward read of legacy 1-byte), avoiding silent overflow at version 256; (e) `VaultMeta` made `IDisposable` and zeroises all byte arrays on disposal; (f) added optional `HelloBlob` field to `VaultMeta` + `hello_blob` key to `MetaRepository` so future Hello binding does not require breaking the Core contract. §7 updated to document `kek_iter` and the 4-byte BE int encoding for iteration counts | Three-reviewer adversarial pass per Anvil 🔴 protocol |
| 2026-05-02 | **Phase 3 (data-layer) implementation green** (build + 43/43 tests). Created `002_data.sql` (all 9 §7 tables), `DomainModels.cs` (3 enums + 9 POCOs), `DapperSetup.cs` (snake↔Pascal column map, enum-as-text handlers, ISO-8601 DateTimeOffset handler), 9 repos under `ToshanVault.Data/Repositories/`. Discovered Dapper bypasses TypeHandlers when binding enum *parameters* (only works on read) — workaround: explicit `.ToString()` in the 3 repos with enum columns (BudgetCategory, BudgetItem, RetirementItem). Adversarial review by 3 models — all FIX-AND-SHIP. Fixes applied: gold cache stale-clobber guard, budget cascade→restrict, encrypt-path plaintext zeroing, recipe-tags explicit rollback, dropped non-spec `ux_budget_category_name` index. New deferred items I8 (vault TOCTOU + IClock), I9 (generic Meta API), I10 (cross-repo unit-of-work) | Three-reviewer adversarial pass per Anvil 🔴 protocol |
| 2026-05-02 | **Phase 4 partial: `XlsxSanitizer` landed**. ClosedXML 0.104.2 (and 0.105.0) crashes on `Toshan.xlsx` with `ArgumentException: Picture names cannot contain :\/?*[]` — no `LoadOptions` flag bypasses it. Wrote `src\ToshanVault.Importer\XlsxSanitizer.cs` (~165 lines) that strips drawing/media/chart/embedding/diagram parts, patches `[Content_Types].xml` Overrides, sheet `_rels` Relationships (drawing/vmlDrawing/chart/image/oleObject types), and sheet XML descendants (`<drawing>`, `<legacyDrawing>`, `<legacyDrawingHF>`, `<picture>`). Idempotent, non-mutating to source. 7 new MSTest tests (50/50 total green). Adversarial review (gpt-5.3-codex, 🟢 risk → 1 reviewer): FIX-AND-SHIP — caught missing `<legacyDrawingHF>` strip; fixed | ClosedXML upstream bug; sanitizer is the cheapest workaround |
| 2026-05-02 | **Phase 4 paused** at sanitizer. Probed `Toshan.xlsx` with sanitizer + ClosedXML: 4 sheets total — Budget (mega-sheet, no headers, income+expense in different column blocks), ImportantDetails (free-form blank-row-separated label/value blocks), Receipes (mostly tabular), Closed Accounts (free-form bank blocks). 3 of 4 sheets do not map to §7 schema. Per-sheet readers + `ImportService` deferred until user manually re-organizes xlsx into one-sheet-per-table format. Logged as I11 | User decision after sheet probe |
| 2026-05-02 | **Re-sequenced: Phase 4 importer paused, Phase 5 split into 5a (app-foundation) + 5b (budget-pages)**. User reorganized xlsx twice but only 2 of 8 sheets gained tabular form; rather than block on spreadsheet shape, build the entry UI first so the forms reveal what column shape `Toshan.xlsx` should take. Importer resumes after Phase 9 (dashboard) | User decision after second xlsx probe |
| 2026-05-02 | **Recipes feature shipped** (import + grid CRUD). Reused Phase 3 scaffold: `recipe` table (migration 002), `Recipe` POCO, `RecipeRepository` (CRUD + tag set), and AppHost DI registration all already existed; no new migration needed. NEW `RecipesImporter` (in ToshanVault.Importer) reads the legacy `Receipes` worksheet from `Toshan.xlsx`: pipes through `XlsxSanitizer` (drops embedded pictures with invalid names that ClosedXML rejects), then iterates with **forward-fill on column A** so recipes with multiple URL rows under one name (e.g. *Butter Chicken* with 5 variant rows) become 5 grid rows that share the title — matches user's "flat grid, one row per link" decision. Rows with blank URL are dropped (separators / spacers). De-dup key is `(Title.Trim(), YoutubeUrl.Trim())` so re-imports are idempotent. NEW `RecipesPage` rewrites the placeholder stub: CommunityToolkit `DataGrid` (Title / Channel / URL / ★ Favourite, sortable, single-select, double-tap-to-edit) + `AutoSuggestBox` search + toolbar buttons (Add / Edit / Delete / 📥 Import xlsx). NEW `RecipeDialog` (title required, author/url/favourite optional). Import button uses `FileOpenPicker` initialised with `MainWindow.Hwnd`; if user cancels the picker but `C:\Work\ToshanVault\Toshan.xlsx` exists, offers it as a one-click fallback. Tier-3 smoke verified end-to-end against the real spreadsheet: 182 raw rows → 101 unique recipes inserted, second import = 0 inserted / 101 skipped. Build green; **91/91 tests pass** (+6 importer unit tests for forward-fill + blank-row drop + blank-url drop + leading-orphan drop + trim). 1-reviewer adversarial pass (gpt-5.3-codex) — no significant issues. | Recipes delivery |
| 2026-05-02 | **Insurance.Owner field added** (post-5e delta). User asked for an Owner on insurance policies; clarified scope = single owner dropdown on the policy itself (mirroring `VaultEntry.Owner`) — credentials remain a single set, NOT multi-owner like bank accounts. Migration 011 = `ALTER TABLE insurance ADD COLUMN owner TEXT` (pure additive, nullable, no 12-step rebuild needed). `Insurance` POCO + `InsuranceRepository` SelectColumns/Insert/Update extended; Dapper `CustomPropertyTypeMap` resolves `owner` ↔ `Owner` automatically (no DapperSetup change). `InsuranceDialog` adds an `OwnerOptions = Enum.GetNames<VaultOwner>()` ComboBox between Insurer and PolicyNumber, defaulting to first option for new rows. `InsuranceVm` exposes Owner; tile Subtitle composes as `"Owner · Type · PolicyNumber"` with empty parts elided; search predicate matches Owner. Tests: extended round-trip Sample to set Owner=Toshan + assertion; new `Insert_AllowsNullOwner_AndUpdate_PersistsOwnerChange` covers legacy-row null path + update propagation. Build green; **85/85 tests pass** (+1). | Insurance.Owner delta |
| 2026-05-02 | **Phase 5e (Insurance) shipped**. New first-class `insurance` entity (migration 010) with renewal-date sorting + countdown badges (red ≤30d / amber ≤60d). `InsuranceRepository` Dapper CRUD; `InsuranceCredentialsService` mirrors `WebCredentialsService` with `insurance.*` label namespace and lazy vault_entry auto-create on first non-empty save (so empty saves don't leave orphan entries). Attachment table CHECK extended to `'insurance'` via SQLite 12-step rename→rebuild→copy→drop recipe (since SQLite cannot ALTER a CHECK); old indexes + 2 cascade triggers recreated and a 3rd added (`trg_attachment_after_insurance_delete`). `trg_insurance_after_delete` cascades into the linked credentials vault_entry so policy deletion does not orphan login fields. New `DateOnly` + `DateOnly?` Dapper TypeHandlers in `DapperSetup` (Dapper has no built-in `string` ↔ `DateOnly` converter). UX: `InsurancePage` (search + tiles), `InsuranceDialog` (entity + attachments panel for existing rows), `InsuranceCredentialsDialog` (username/password/RichNotes — single creds, not multi-owner; joint policies deferred until a real example surfaces). MainShell nav item with shield glyph \uE83D. Build green; 84/84 tests pass (+8: round-trip, ordering, FK SET NULL on vault_entry delete, cascade on insurance delete, lazy-no-create on empty save, narrow `LoadLabelsAsync`). | Phase 5e delivery |
| 2026-05-02 | **Phase 5d (multi-owner bank, rich notes, attachments, tile UX) shipped** as a series of user-driven deltas between 5c and 5e: (a) migration 006 `bank_account_credential` for joint accounts (`UNIQUE(bank_account_id, owner_label)`) — credentials moved out of the bank_account FK and into N-per-account rows so Toshan + Devangini logins coexist; tile actions render gendered icons (\uE13D / \uE13E) instead of named buttons. (b) Reusable `RichNotesField` (RichEditBox + font/size/B/I/U + foreground/background colour) persisted as RTF in encrypted `vault_field.value_enc` BLOBs; rolled out across Bank Notes, Bank-credentials Notes, Vault Additional Details (and later Insurance Notes). (c) Reusable `AttachmentsPanel` + `AttachmentService` + migration 009 polymorphic `attachment(target_kind, target_id, ...)` — payloads AES-GCM-encrypted with the vault DEK, opened by decrypting to a temp file. (d) Bank Account dialog gained Website + Card PIN + Phone-banking PIN. (e) Vault tile preview: Number + Website narrow-decrypted via new `WebCredentialsService.LoadLabelsAsync` (Dapper `IN @labels` expansion) — review caught H over-decryption + H broad-catch swallow of `VaultLockedException`; both fixed (split into `LoadAsync`/`LoadLabelsAsync`/`LoadInternalAsync`; dedicated `catch (VaultLockedException)` in `OnNavigatedTo`). (f) `MainWindow.Maximize` on launch + activation; `LoginPage` master-password autofocus (immediate `Focus(Keyboard)` → Low-priority dispatcher → PasswordBox.Loaded fallback) + Enter-to-submit; per-tile-page search via `AutoSuggestBox`. Tests grew 63→76→84 across these batches. | Phase 5d delivery |
| 2026-05-02 | **Phase 5c (bank accounts) implementation green**. Shipped: single `bank_account` table (Option A — `is_closed` flag instead of separate archive); migration 003 forward-migrates any pre-existing `closed_account` rows (HIGH from all 3 reviewers) before dropping the legacy stub; `BankAccountRepository` with idempotent transactional `CloseAsync`/`ReopenAsync`; `BankCredentialsService` performs vault_entry create + bank_account link + per-field upsert/delete in **a single SQLite transaction** (HIGH 3/3 reviewers — closes idle-lock partial-save window); credential field labels **namespaced under `bank_login.`** (MED 3/3 — prevents collision with user-created vault fields); `BankAccountsPage` (Add/Edit/Credentials/Close with masked BSB/acct#); rewritten `ClosedAccountsPage` with Reopen action; 3 ContentDialogs (`BankAccountDialog`, `CredentialsDialog` with up to 10 Q/A pairs, `CloseConfirmDialog`); `_busy` re-entrancy guard on every async-void click handler (MED 1/3 — prevents WinUI's "one ContentDialog per XamlRoot" `COMException`); recreate-and-relink fallback when linked vault_entry is deleted externally (MED 1/3); credentials model nulled after save to minimise plaintext lifetime in managed heap (LOW). Three-reviewer adversarial pass per Anvil 🔴 protocol (gpt-5.3-codex, claude-opus-4.6, gpt-5.5) — 5 distinct issues found across 13 raw findings; all addressed and re-verified. Build green; 63/63 tests pass (+8 BankAccount repo, +5 BankCredentialsService, +1 migration data-preservation, −1 obsolete ClosedAccount stub); smoke launch alive ≥8 s; App\ bundle re-published (~248 MB self-contained). Deferred: `ReopenAsync` no-op on already-open (LOW 1/3, behaviour change conflicts with existing throw-on-not-closed test); `gemini-3-pro-preview` reviewer (still unavailable in this env). | Phase 5c delivery |
| 2026-05-02 | **Gold Ornaments page**. Shipped: 11th nav tile reusing the pre-existing Phase 3 `gold_item` + `gold_price_cache` schema (no new migration). `GoldImporter` reads sheet "Gold Ornaments" (rows 4+, cols A=Description / B=Qty / C=Tola), defaults purity to 22K, computes `grams_calc = tola × 11.6638038`. `GoldPriceService` (App layer) fetches USD/oz from `api.gold-api.com/price/XAU` and AUD/USD from `api.frankfurter.app` (both free, no key — supersedes I4) and upserts AUD-per-gram-24K into the cache with a 1 h TTL; falls back to last cached value on network failure. Pure valuation math (`PurityFraction`, `EstimateValue`) extracted to `ToshanVault.Core.Models.GoldValueCalculator` so it's testable from the non-App test project (I12). DataGrid columns: Description / Qty / Tola / Grams / Purity / Est. Value (AUD) / Notes. Footer shows total grams + total estimated AUD. +15 tests (GoldImporter parsing + tola constant; GoldValueCalculator karat fractions + diamond/zero-price edge cases). | User asked for Gold tile w/ live price |
| 2026-05-02 | **Recipes — Tried marker + auto-categorisation**. Migration 012 adds `recipe.is_tried INTEGER DEFAULT 0` and `recipe.category TEXT DEFAULT 'Other'`; one-shot UPDATE auto-classifies existing rows by `LOWER(title) LIKE '%egg%'`/`'%chicken%'`; second UPDATE seeds 25 known-tried titles from the user's spreadsheet image (yellow rows). New `RecipeCategorizer.Classify(title)` (Core) uses `\begg\b`/`\bchicken\b` word-boundary regex (Egg wins ties) and is wired into `RecipeRepository.Insert/Update` + `RecipesImporter`. Page now shows Tried / Category columns; rows with `IsTried=true` get a green `SystemFillColorSuccessBackgroundBrush` background via `LoadingRow`. Default sort: `is_tried ASC, category, is_favourite DESC, title` — untried rows surface at the top, grouped Chicken / Egg / Other. Dialog gains "I've tried this" checkbox + Category combo (auto-populates from title until manually changed). +11 tests (categorizer cases + word-boundary edge cases). | User request: mark recipes as tried, sort by tried + category |
| 2026-05-02 | **Phase 5a (app-foundation) implementation green**. Shipped: `AppHost` (MEDI composition root with `IMetaStore`+`MetaRepository` shared-singleton via factory delegate so `Vault` and direct meta consumers see the same instance), `AppPaths`, `IdleLockService` (`Func<TimeSpan>`-injectable probe + `Win32IdleProbe.GetLastInputInfo`), `NavigationService`, `LoginPage` (first-run init vs unlock, migrations in `OnNavigatedTo`, `WrongPasswordException` → InfoBar + 750 ms backoff stub), `MainShellPage` (NavigationView with 5 main + Lock/About + Settings, 1 Hz `DispatcherTimer` driving idle-lock). Build green; 50/50 prior tests still pass. App test project ref dropped after WinAppSDK auto-init blocker (I12-a). Smoke launch initially failed with `REGDB_E_CLASSNOTREG`; fixed by `<WindowsAppSDKSelfContained>true</WindowsAppSDKSelfContained>` (I12-b). Three-reviewer adversarial pass per Anvil 🔴 protocol (gpt-5.3-codex, claude-opus-4.6, gpt-5.5 — gemini-3-pro-preview unavailable in this env) surfaced 1 HIGH + 4 MED findings (3 unique after dedup). Fixes applied: (a) `MainShellPage._lockRequested` re-entrancy guard prevents idle-tick + manual-lock click double-navigating to a duplicate `LoginPage`; (b) `MainShellPage.Teardown()` invoked from both `Unloaded` event and `LockAndReturnToLogin`, plus `_wired` guard so a duplicate `Loaded` cannot stack timers/event handlers on the singleton `IdleLockService`; (c) `LoginPage` XAML defaults `PasswordBox`/`ConfirmBox`/`UnlockButton` to `IsEnabled=False` and `OnNavigatedTo` enables them only after `RunAsync()` + `IsInitialisedAsync()` complete, eliminating the slow-disk race where a user could click into a half-initialised DB. Re-built, 50/50 tests still pass, smoke launch alive ≥8 s. Deferred (logged as future work): progressive backoff ladder (1 s/5 s/30 s/5 min) per UI-Mockups → Phase 6; Hello binding; theming (Mica); clipboard auto-clear (Phase 6) | Phase 5a delivery |
| 2026-05-02 | **Serilog logging added to App** (`Hosting/Logging.cs`). Packages: `Serilog 4.2.0`, `Serilog.Sinks.File 6.0.0`, `Serilog.Sinks.Debug 3.0.0`. Rolling-daily file sink at `%LOCALAPPDATA%\ToshanVault\logs\toshanvault-YYYYMMDD.log` (retains 14 days, shared write so multiple instances are safe), plus Debug sink for VS Output. `App.xaml.cs` calls `Logging.Initialise()` before `InitializeComponent()` and wires `UnhandledException` (WinUI), `AppDomain.UnhandledException`, and `TaskScheduler.UnobservedTaskException`. Pages get a logger via `Logging.ForContext<T>()` (used in `GoldOrnamentsPage`); rolling-out to other pages is opportunistic. Library projects deliberately do NOT reference Serilog — keeps Core/Data/Importer test-portable; they raise typed exceptions and the App layer logs at the boundary. | User request — needed observability after a blank "Error" InfoBar made the picker E_FAIL undebuggable |
| 2026-05-03 | **Project moved to `C:\Work\ToshanVault`**. User relocated the repo; all paths updated. `appsettings.json` default DB path set to `C:\Work\ToshanVault\App\VaultDb\vault.db` |
| 2026-05-03 | **Single-file publish** (`tools\publish-single.ps1`). WinUI 3 self-contained exe (~98 MB) via `PublishSingleFile=true` + `IncludeAllContentForSelfExtract=true` + `WindowsAppSDKSelfContained=true`. First launch extracts to `%TEMP%\.net\<app>\<hash>\`. Critical fix: `AppPaths.LoadSettingsDbPath` now probes `Process.GetCurrentProcess().MainModule.FileName`'s directory first (extraction temp `AppContext.BaseDirectory` doesn't contain sidecar files). Publish script preserves `VaultDb\` folder, `*.db/*.sqlite/*.sqlite3` files, and `appsettings.json` across runs |
| 2026-05-03 | **Dashboard shipped** (`DashboardPage.xaml/.cs`). 7 KPI tiles: Weekly Cashflow, Gold Holdings, Loan Payoff, Insurance Renewals (next 5, color-coded), Counts strip, Recent Notes, DB info + Backup button. All loads parallelised via `Task.WhenAll`. Tile taps navigate via `NavigationService.NavigateInShell(tag)` |
| 2026-05-03 | **Missing-DB prompt**. `App.OnLaunched` checks `File.Exists(dbPath)` before `AppHost.Build()`. If missing, shows Win32 `MessageBoxW` (Yes=create, No=exit). Uses P/Invoke because no XAML host exists yet at that point |
| 2026-05-03 | **Data loss incident & recovery**. `publish-single.ps1` originally wiped `App\` including user's `VaultDb\`; script now explicitly preserves data folders. Retirement items re-seeded via `tools\seed-retirement.ps1` (16 rows). Gold ornaments re-seeded via `tools\seed-gold.ps1` (54 items from 30/01/2022 inventory). Recipes imported from `Book1.xlsx` via `tools\parse-recipes-xlsx.js` + `tools\seed-recipes.ps1` (80 recipes, 20 tried) |
| 2026-05-03 | **Recipe categories fixed**. Seed SQL was missing `category` and `is_tried` columns. Applied UPDATE to classify 66 Chicken / 10 Egg / 4 Other; set `is_tried=1` on 20 tried recipes. Fixed `parse-recipes-xlsx.js` to include both columns in future runs |
| 2026-05-03 | **Gold page: AUD → $**. Replaced all 5 "AUD" currency prefixes with "$" in `GoldOrnamentsPage.xaml.cs` and `.xaml` |
| 2026-05-04 | **NotesWindow popup implemented**. Extracted notes editing from all edit dialogs into a standalone `Window` (`NotesWindow.xaml.cs`). Uses 90% screen width/height. RichNotesField placed inside a Grid with Auto+Star rows so the editor fills the full available height. Notes icon button (📝 glyph \uE70B) added to Bank Account, Vault, and Insurance tiles. Notes removed from edit dialog forms. Insurance: `insurance.notes` column added (plaintext, stored in DB not vault_field), with `MigrateNotesToColumnAsync()` to decrypt old vault_field notes on first navigation. General Notes page already used it. Committed: `edc0089` |
| 2026-05-04 | **Multi-owner credentials for Insurance + Vault**. Replicated the Bank Accounts multi-owner credential pattern. Migrations 019 (`insurance_credential`) and 020 (`web_credential`) create junction tables with `UNIQUE(parent_id, owner)` + cascade delete triggers. Back-fill: existing vault_entry_id rows assigned owner='Toshan'. `InsuranceCredentialsService` fully rewritten: `SaveAsync(insuranceId, owner, entryName, fields)` creates/reuses insurance_credential row + vault_entry. `WebCredentialsService` gained `SaveCredentialsAsync(entryId, owner, entryName, fields)` (SaveAsync preserved for item-level fields). New repos: `InsuranceCredentialRepository`, `WebCredentialRepository`. UI: owner-initial avatar buttons (circular, showing first letter of owner) + "+" button for adding new owners via `OwnerPickerDialog`. `InsuranceCredentialsDialog` and `VaultCredentialsDialog` now include owner in title, Q&A (up to 10 pairs), and delete button (secondary). `KnownOwners` shared: ["Toshan","Devangini","Prachi","Saloni"]. 129/129 tests pass. Committed: `c1f0d9a` |
| 2026-05-04 | **User decision: keep duplicated credential pattern** per page (Bank/Insurance/Vault) rather than creating a shared credential component. Rationale: dialogs differ per domain (Bank has CardPin/PhonePin/ClientID; Insurance has Username/Password/Q&A; Vault has Username/Password/Q&A) and for a personal app the simplicity of duplicated patterns outweighs DRY concerns |

---

## 13. Conventions

- **Naming** — PascalCase types/methods, camelCase locals/parameters, `_camel` private fields.
- **Async** — every IO/crypto call is async; suffix `Async`.
- **Records vs classes** — immutable DTOs are `record`s; ViewModels are `partial class` with CommunityToolkit source generators.
- **No magic strings** — DB column names live in `Schema` const class.
- **Comments** — only where the *why* isn't obvious. No `//get name` style noise.
- **Logging** — Serilog static facade configured in `ToshanVault_App.Hosting.Logging`. Inside the App layer use `Logging.ForContext<T>()` for context-scoped loggers, or `Serilog.Log.Information/Warning/Error/Fatal(...)` for top-level. **Library projects (Core/Data/Importer) do NOT reference Serilog** — they throw typed exceptions, the App catches at the UI boundary and logs there. Log file: `%LOCALAPPDATA%\ToshanVault\logs\toshanvault-YYYYMMDD.log` (rolling daily, 14-day retention, shared write).

### 13.1 Multi-Owner Credential Pattern

All three entity types (Bank Account, Insurance, Vault) follow the same pattern for per-owner encrypted credentials:

**Architecture:**
1. **Junction table** — `{entity}_credential(id, {entity}_id, owner, vault_entry_id, created_at, updated_at, UNIQUE({entity}_id, owner))`
2. **Cascade chain** — entity delete → CASCADE to credential rows → AFTER DELETE trigger on credential → deletes vault_entry → CASCADE to vault_field rows
3. **Service** — `Save{Entity}CredentialsAsync(entityId, owner, entryName, fields)` creates/reuses credential row + vault_entry; encrypts fields into vault_field
4. **Repository** — read-side: `GetBy{Entity}Async(entityId)` returns all credential rows; `DeleteAsync(credId)` removes one
5. **UI** — tile shows `ItemsControl` of owner-initial avatar buttons + "+" button; click → load fields from vault_entry → open dialog; "+" → `OwnerPickerDialog` (lists owners not yet used) → open dialog with null vaultEntryId

**KnownOwners:** `["Toshan", "Devangini", "Prachi", "Saloni"]` — shared via `BankCredentialsService.KnownOwners`; referenced by Insurance and Web services.

**Credential dialog pattern:**
- Title: `"{domain} · {entity name} · {owner}"`
- Primary button: "Save (encrypted)"
- Secondary button: "Delete credential" (only if editing existing — `allowDelete: true`)
- Close button: "Cancel"
- `DeleteRequested` flag on dialog; page shows confirmation ContentDialog AFTER the cred dialog closes (WinUI only allows one open ContentDialog per XamlRoot)
- After save/delete: wipe model fields (Username/Password/Qa) to minimise plaintext lifetime

**Label namespaces (prevent collision in vault_field):**
- Bank: `bank_login.username`, `bank_login.password`, `bank_login.client_id`, `bank_login.card_pin`, `bank_login.phone_pin`, `bank_login.q1`–`q10`, `bank_login.a1`–`a10`, `bank_login.notes`
- Insurance: `insurance.username`, `insurance.password`, `insurance.q1`–`q10`, `insurance.a1`–`a10`, `insurance.notes`
- Vault: `web_login.username`, `web_login.password`, `web_login.q1`–`q10`, `web_login.a1`–`a10`, `web_login.number`, `web_login.website`, `web_login.additional_details`

### 13.2 NotesWindow Pattern

Notes are edited in a standalone `Window` (not ContentDialog) for maximum editing area:

```csharp
var (saved, value) = await NotesWindow.ShowAsync("Title", existingRtf);
if (!saved) return;
// persist value
```

- Window size: 90% of screen width/height
- Content: RichNotesField (RichEditBox + toolbar) placed in a Grid with `Auto` (header) + `*` (editor) rows
- Returns RTF string on save, null/unchanged on cancel
- Used by: General Notes, Bank Accounts, Vault items, Insurance policies
- Triggered by: Notes icon button (glyph \uE70B) on each tile

### 13.3 Tile Icon Buttons

All entity tiles use consistent 32×32 icon buttons (no text). Standard set per tile:
- ✏️ Edit (glyph \uE70F) — opens entity edit dialog
- 📝 Notes (glyph \uE70B) — opens NotesWindow popup
- 👤 Owner initials (circular, CornerRadius=16) — opens credential dialog for that owner
- ➕ Add credential (glyph \uE710) — opens OwnerPickerDialog then credential dialog
- 🗑️ Delete (glyph \uE74D) — confirms then deletes entity

### 13.4 Encryption Rules

- **Encrypted (vault_field):** passwords, PINs, security answers, client IDs, usernames
- **Plaintext (DB column):** notes (RTF), website URLs, phone numbers, policy numbers, names
- Only fields that would cause harm if the DB file were stolen are encrypted
- Notes were explicitly moved to plaintext per user decision (only passwords + secret answers need encryption)
- **User-visible errors** — never call `ShowError(ex.Message)` directly on the bare exception; many WinRT/COM exceptions have empty `Message`. Use a page-local `FormatError(ex)` helper (see `GoldOrnamentsPage.xaml.cs`) that walks the `InnerException` chain and appends the log file path. Always pair the InfoBar with a `_log.Error(ex, "...")` so the full stack lands on disk.
- **Secrets** — never logged, never displayed in tooltips, never written outside `vault_field.value_enc`.

---

## 14. How to Use This File

- After every meaningful step (decision, package change, fix, phase complete), update §9 + §11 + §12.
- Link to it from `plan.md` rather than duplicating content.
- This file may be committed to the project root and is safe to share — it contains no
  secrets (the master password fallback value is intentionally referenced but is not a
  secret per user direction).

---

## 15. Resuming the Session (read this first on next run)

**This project is being moved to a different PC.** Copilot CLI sessions are stored
locally per-machine, so the original session ID **will not work** on the new PC. Start
fresh and use this file to bring the new agent fully up to speed.

### A. What to copy from the old PC

Copy the **entire** project folder, preserving structure:

```
C:\Work\ToshanVault\
├── Toshan.xlsx                          ← the source spreadsheet (REQUIRED)
├── UI-Mockups.md                        ← approved screen designs
├── project-instructions.md              ← THIS FILE (single source of truth)
├── plan.md                              ← optional copy of the original plan
└── ToshanVault\                          ← solution + 5 projects + NuGet refs
    ├── ToshanVault.slnx
    ├── src\ToshanVault.App\
    ├── src\ToshanVault.Core\
    ├── src\ToshanVault.Data\
    ├── src\ToshanVault.Importer\
    └── tests\ToshanVault.Tests\
```

You can safely **skip** these (they will be re-generated on first build):
`bin\`, `obj\`, `*.user`, `.vs\`

If `plan.md` is in the session folder
(`C:\Users\<you>\.copilot\session-state\<session-id>\plan.md`), copy it next to
`project-instructions.md` so it travels with the project.

### B. Prerequisites on the new PC

| Tool | Required version | Install command |
|------|------------------|-----------------|
| Windows | 11 (any current build) | — |
| .NET SDK | 10.0.x (or newer) | https://dotnet.microsoft.com/download |
| Windows App SDK runtime | 1.6+ | Installed automatically as a NuGet dependency on first build |
| Visual Studio 2022 (optional) | 17.10+ with "Windows App SDK C# Templates" workload | Only needed for the designer; CLI build works without it |
| GitHub Copilot CLI | latest | https://github.com/github/copilot-cli (or `winget install GitHub.cli` then auth) |
| WinUI 3 dotnet templates | latest | `dotnet new install Microsoft.WindowsAppSDK.WinUI.CSharp.Templates` |

### C. Commands to start work on the new PC

```powershell
# 1. Verify the project copied correctly and builds (red is expected — see I1, I2)
cd "C:\Work\ToshanVault"
dotnet restore ToshanVault.slnx
dotnet build ToshanVault.slnx -c Debug -p:Platform=x64

# 2. Start a brand-new Copilot CLI session in the project root
cd "C:\Work\ToshanVault"
copilot
```

### D. First message to send to the new agent

Paste this verbatim — it tells the fresh agent everything it needs to pick up cleanly:

> I am resuming a project that was started on another PC. The full history,
> architecture, decisions, current build state, known issues and next steps are
> documented in `project-instructions.md` in this folder. Please:
>
> 1. Read `project-instructions.md` end-to-end (especially §3 architecture, §5 packages,
>    §6 security model, §9.1 scaffold progress, §11 known issues, §12 decision log).
> 2. Read `UI-Mockups.md` for the approved UI design.
> 3. Confirm the solution builds: `dotnet build ToshanVault.slnx -c Debug -p:Platform=x64`
> 4. Run tests: `dotnet test tests\ToshanVault.Tests\ToshanVault.Tests.csproj -c Debug -p:Platform=x64 --nologo`
> 5. Tell me which phases from §9 still need work and ask what I'd like to tackle next.

### E. Where to find the original session (for reference only)

The original Copilot CLI session lives at
`C:\Users\tpate\.copilot\session-state\face5148-645d-4b5e-b4e8-1b138e9a16e9\` on the
original PC. You don't need to copy it — `project-instructions.md` is sufficient. If you
ever go back to that PC you can resume with:

```powershell
copilot --resume=face5148-645d-4b5e-b4e8-1b138e9a16e9
```

### F. Expected state when you arrive on the new PC

- 5 projects exist with all NuGet packages restored ✅
- Project references wired correctly ✅
- Build is **green** (`dotnet build ToshanVault.slnx -c Debug -p:Platform=x64`) ✅
- 129 unit tests pass (4 skipped — xlsx sanitizer tests require source file) ✅
- All major features implemented: auth, vault, budget, bank accounts, insurance, recipes, gold, retirement, dashboard
- Multi-owner credentials on all three entity types (Bank, Insurance, Vault) ✅
- NotesWindow popup for all note-bearing entities ✅
- Single-file publish works via `tools\publish-single.ps1` ✅
- SQLite DB at `App\VaultDb\vault.db` with seeded data (54 gold items, 80 recipes, 16 retirement items)
- Publish script preserves VaultDb folder and *.db files
- Migrations 001–020 applied (schema_ver=20)

### G. Quick orientation files

| File | Purpose |
|------|---------|
| `project-instructions.md` | This file — single source of truth |
| `UI-Mockups.md` | Approved ASCII screen designs |
| `ToshanVault.slnx` | Solution to build / open in VS |
| `tools\publish-single.ps1` | Single-file publish script |
| `tools\seed-gold.ps1` | Seeds 54 gold ornament rows |
| `tools\parse-recipes-xlsx.js` | Parses Book1.xlsx → seed-recipes.sql |
| `tools\seed-recipes.ps1` | Runs seed-recipes.sql against vault.db |

### H. Key source files (architecture map)

**Services (Data layer — `src\ToshanVault.Data\Repositories\`):**
| File | Purpose |
|------|---------|
| `BankCredentialsService.cs` | Multi-owner bank credential encrypt/decrypt (reference implementation) |
| `InsuranceCredentialsService.cs` | Multi-owner insurance credential encrypt/decrypt + notes migration |
| `WebCredentialsService.cs` | Multi-owner vault item credential encrypt/decrypt |
| `BankAccountCredentialRepository.cs` | Read-side: `GetByAccountAsync`, `DeleteAsync` |
| `InsuranceCredentialRepository.cs` | Read-side: `GetByInsuranceAsync`, `DeleteAsync` |
| `WebCredentialRepository.cs` | Read-side: `GetByEntryAsync`, `DeleteAsync` |
| `GeneralNotesService.cs` | General notes CRUD |

**Pages (App layer — `src\ToshanVault.App\Pages\`):**
| File | Purpose |
|------|---------|
| `BankAccountsPage.xaml/.cs` | Bank accounts — reference for multi-owner credential UI pattern |
| `BankAccountDialogs.cs` | `CredentialsDialog`, `OwnerPickerDialog` (reused by Insurance/Vault) |
| `InsurancePage.xaml/.cs` | Insurance tiles with credential avatars |
| `InsuranceDialogs.cs` | `InsuranceDialog`, `InsuranceCredentialsDialog` (with Q&A) |
| `VaultPage.xaml/.cs` | Vault with category grouping + credential avatars |
| `VaultDialogs.cs` | `VaultEntryDialog`, `VaultCredentialsDialog` (with Q&A + delete) |
| `NotesWindow.xaml/.cs` | Standalone notes window (full-height RichEditBox) |

**Migrations (Data layer — `src\ToshanVault.Data\Schema\Migrations\`):**
| File | Content |
|------|---------|
| `001_init.sql` | meta table |
| `002_data.sql` | All core tables (budget, vault, recipe, gold, retirement) |
| `003_bank_account.sql` | bank_account + migrate closed_account |
| `006_bank_credential.sql` | bank_account_credential multi-owner |
| `009_attachment.sql` | Polymorphic attachment table |
| `010_insurance.sql` | insurance entity + attachment CHECK rebuild |
| `011_insurance_owner.sql` | insurance.owner column |
| `012_recipe_tried_category.sql` | recipe.is_tried + recipe.category |
| `019_insurance_credential.sql` | insurance_credential multi-owner |
| `020_web_credential.sql` | web_credential multi-owner |

### I. Build & test commands

```powershell
# Build (Debug x64)
dotnet build ToshanVault.slnx -c Debug -p:Platform=x64 --nologo -v:q

# Run tests (after build)
dotnet test tests\ToshanVault.Tests\ToshanVault.Tests.csproj -c Debug -p:Platform=x64 --nologo --no-build

# Publish single-file exe (~98 MB)
pwsh tools\publish-single.ps1
# Output: App\ToshanVault.App.exe + App\appsettings.json + App\VaultDb\vault.db
```


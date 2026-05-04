-- Migration 002: domain tables.
-- All §7 tables except `meta` (created in 001).
-- Encrypted columns are `_enc BLOB` + their `iv BLOB` and `tag BLOB` siblings.

----------------------------------------------------------------
-- Budget (monthly, recurring)
----------------------------------------------------------------
CREATE TABLE IF NOT EXISTS budget_category (
    id   INTEGER PRIMARY KEY AUTOINCREMENT,
    name TEXT NOT NULL,
    type TEXT NOT NULL CHECK (type IN ('Income','Fixed','Variable'))
);

CREATE TABLE IF NOT EXISTS budget_item (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    category_id INTEGER NOT NULL REFERENCES budget_category(id) ON DELETE RESTRICT,
    label       TEXT NOT NULL,
    amount      REAL NOT NULL,
    frequency   TEXT NOT NULL CHECK (frequency IN ('Monthly','Quarterly','Yearly','OneOff')),
    notes       TEXT,
    sort_order  INTEGER NOT NULL DEFAULT 0
);
CREATE INDEX IF NOT EXISTS ix_budget_item_category ON budget_item(category_id, sort_order);

----------------------------------------------------------------
-- Retirement (long-term projections)
----------------------------------------------------------------
CREATE TABLE IF NOT EXISTS retirement_item (
    id                       INTEGER PRIMARY KEY AUTOINCREMENT,
    label                    TEXT NOT NULL,
    kind                     TEXT NOT NULL CHECK (kind IN ('Income','Expense')),
    monthly_amount_jan2025   REAL NOT NULL,
    inflation_pct            REAL NOT NULL DEFAULT 0,
    indexed                  INTEGER NOT NULL DEFAULT 0 CHECK (indexed IN (0,1)),
    start_age                INTEGER,
    end_age                  INTEGER,
    notes                    TEXT
);

----------------------------------------------------------------
-- Gold
----------------------------------------------------------------
CREATE TABLE IF NOT EXISTS gold_item (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    item_name   TEXT NOT NULL,
    purity      TEXT NOT NULL,
    qty         REAL NOT NULL,
    tola        REAL NOT NULL DEFAULT 0,
    grams_calc  REAL NOT NULL DEFAULT 0,
    notes       TEXT
);

CREATE TABLE IF NOT EXISTS gold_price_cache (
    currency               TEXT PRIMARY KEY,
    price_per_gram_24k     REAL NOT NULL,
    fetched_at             TEXT NOT NULL
) WITHOUT ROWID;

----------------------------------------------------------------
-- Vault (encrypted-at-rest fields)
----------------------------------------------------------------
CREATE TABLE IF NOT EXISTS vault_entry (
    id         INTEGER PRIMARY KEY AUTOINCREMENT,
    kind       TEXT NOT NULL,
    name       TEXT NOT NULL,
    category   TEXT,
    tags       TEXT,
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_vault_entry_kind ON vault_entry(kind);
CREATE INDEX IF NOT EXISTS ix_vault_entry_name ON vault_entry(name);

CREATE TABLE IF NOT EXISTS vault_field (
    id         INTEGER PRIMARY KEY AUTOINCREMENT,
    entry_id   INTEGER NOT NULL REFERENCES vault_entry(id) ON DELETE CASCADE,
    label      TEXT NOT NULL,
    value_enc  BLOB NOT NULL,
    iv         BLOB NOT NULL,
    tag        BLOB NOT NULL,
    is_secret  INTEGER NOT NULL DEFAULT 0 CHECK (is_secret IN (0,1))
);
CREATE INDEX IF NOT EXISTS ix_vault_field_entry ON vault_field(entry_id);

-- Closed accounts
----------------------------------------------------------------
CREATE TABLE IF NOT EXISTS closed_account (
    id              INTEGER PRIMARY KEY AUTOINCREMENT,
    bank            TEXT NOT NULL,
    account_number  TEXT NOT NULL,
    closed_date     TEXT NOT NULL,
    reason          TEXT,
    notes           TEXT
);

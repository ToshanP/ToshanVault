-- Migration 003: bank_account replaces closed_account.
--
-- Phase 5c. The Phase 3 stub `closed_account` was a standalone archive (no FK,
-- only soft-deleted via INSERT). Phase 5c unifies active + closed bank accounts
-- in a single `bank_account` table with an `is_closed` flag, so closing an
-- account preserves history and supports reopening. Credentials live in the
-- existing vault_entry/vault_field encrypted store, linked via vault_entry_id.

CREATE TABLE IF NOT EXISTS bank_account (
    id                 INTEGER PRIMARY KEY AUTOINCREMENT,
    bank               TEXT NOT NULL,
    account_name       TEXT NOT NULL,
    bsb                TEXT,
    account_number     TEXT,
    account_type       TEXT NOT NULL CHECK (account_type IN
                         ('Savings','Cheque','TermDeposit','Offset',
                          'CreditCard','Mortgage','Other')),
    holder_name        TEXT,
    interest_rate_pct  REAL,
    notes              TEXT,
    is_closed          INTEGER NOT NULL DEFAULT 0 CHECK (is_closed IN (0,1)),
    closed_date        TEXT,
    close_reason       TEXT,
    vault_entry_id     INTEGER REFERENCES vault_entry(id) ON DELETE SET NULL,
    created_at         TEXT NOT NULL,
    updated_at         TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_bank_account_is_closed     ON bank_account(is_closed);
CREATE INDEX IF NOT EXISTS ix_bank_account_vault_entry   ON bank_account(vault_entry_id);

-- Re-create the legacy table as a no-op if it doesn't exist so the copy below
-- is unconditional. (CREATE IF NOT EXISTS is a no-op when it already exists.)
CREATE TABLE IF NOT EXISTS closed_account (
    id              INTEGER PRIMARY KEY,
    bank            TEXT,
    account_number  TEXT,
    closed_date     TEXT,
    reason          TEXT,
    notes           TEXT
);

-- Forward-migrate any pre-existing closed_account rows so user data is never
-- lost. account_name is required NOT NULL in bank_account; reuse bank as a
-- safe fallback when it's missing. Marked is_closed=1 with original close
-- date (or migration time if absent).
INSERT INTO bank_account (bank, account_name, account_number, account_type,
                          notes, is_closed, closed_date, close_reason,
                          created_at, updated_at)
SELECT
    COALESCE(NULLIF(TRIM(bank), ''), '(unknown)'),
    COALESCE(NULLIF(TRIM(bank), ''), '(unknown)'),
    account_number,
    'Other',
    notes,
    1,
    COALESCE(closed_date, strftime('%Y-%m-%dT%H:%M:%S+00:00', 'now')),
    reason,
    strftime('%Y-%m-%dT%H:%M:%S+00:00', 'now'),
    strftime('%Y-%m-%dT%H:%M:%S+00:00', 'now')
FROM closed_account;

DROP TABLE closed_account;


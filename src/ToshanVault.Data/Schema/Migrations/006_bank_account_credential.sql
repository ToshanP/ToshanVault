-- Migration 006: per-owner bank login credentials.
--
-- Phase 5c follow-up. A single bank account often has multiple online-banking
-- logins (e.g. self + spouse). The original 1:1 bank_account.vault_entry_id
-- link cannot represent that. This migration introduces a join table
-- `bank_account_credential` so any bank_account can have N credentials, each
-- tagged with an owner label and pointing at its own vault_entry that holds
-- the encrypted bank_login.* fields (username, password, security Q&A …).
--
-- The legacy column `bank_account.vault_entry_id` is left in place (SQLite
-- cannot DROP COLUMN reliably across older runtime versions and removing it
-- would invalidate prior backups). Existing single-credential rows are
-- back-filled into the new table with owner='Toshan' so nothing is lost.
-- New code must read/write credentials only via this table.

CREATE TABLE IF NOT EXISTS bank_account_credential (
    id              INTEGER PRIMARY KEY AUTOINCREMENT,
    bank_account_id INTEGER NOT NULL REFERENCES bank_account(id) ON DELETE CASCADE,
    owner           TEXT NOT NULL,
    vault_entry_id  INTEGER NOT NULL REFERENCES vault_entry(id) ON DELETE CASCADE,
    created_at      TEXT NOT NULL,
    updated_at      TEXT NOT NULL,
    UNIQUE(bank_account_id, owner)
);
CREATE INDEX IF NOT EXISTS ix_bank_account_credential_account
    ON bank_account_credential(bank_account_id);
CREATE INDEX IF NOT EXISTS ix_bank_account_credential_entry
    ON bank_account_credential(vault_entry_id);

-- Cascade vault_entry cleanup when a credential row is deleted directly
-- (or via bank_account cascade). vault_field already cascades from
-- vault_entry so deleting the entry removes its encrypted blobs too.
CREATE TRIGGER IF NOT EXISTS trg_bank_credential_after_delete
AFTER DELETE ON bank_account_credential
BEGIN
    DELETE FROM vault_entry WHERE id = OLD.vault_entry_id;
END;

-- Back-fill: any existing bank_account that has a vault_entry_id becomes a
-- credential row owned by 'Toshan'. Validates that the referenced entry still
-- exists to avoid foreign-key violations on partially cleaned databases.
INSERT INTO bank_account_credential (bank_account_id, owner, vault_entry_id, created_at, updated_at)
SELECT
    ba.id,
    'Toshan',
    ba.vault_entry_id,
    strftime('%Y-%m-%dT%H:%M:%S+00:00', 'now'),
    strftime('%Y-%m-%dT%H:%M:%S+00:00', 'now')
FROM bank_account ba
WHERE ba.vault_entry_id IS NOT NULL
  AND EXISTS (SELECT 1 FROM vault_entry ve WHERE ve.id = ba.vault_entry_id);

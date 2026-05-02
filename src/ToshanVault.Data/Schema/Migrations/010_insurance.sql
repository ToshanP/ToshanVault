-- Migration 010: insurance entity + extend file-link table for insurance kind.
--
-- Insurance is a first-class entity (own table) rather than a vault_entry kind
-- because we want renewal-date-driven UI behaviour (badges, sorting) and
-- type-specific fields without leaking insurance concepts into the generic
-- vault model.
--
-- Encrypted-at-rest fields (notes, credentials) live in the existing
-- vault_entry/vault_field machinery via insurance.vault_entry_id, mirroring
-- bank_account_credential. Username/password/notes labels are namespaced
-- under "insurance.*" by InsuranceCredentialsService.

CREATE TABLE IF NOT EXISTS insurance (
    id                INTEGER PRIMARY KEY AUTOINCREMENT,
    insurer_company   TEXT NOT NULL,
    policy_number     TEXT,
    insurance_type    TEXT,                 -- free text: Health/Car/Home/Life/Travel/...
    website           TEXT,
    renewal_date      TEXT,                 -- ISO-8601 yyyy-MM-dd; nullable so historical
                                            -- policies without a known date still import.
    vault_entry_id    INTEGER,              -- FK to vault_entry holding insurance.* fields
    created_at        TEXT NOT NULL,
    updated_at        TEXT NOT NULL,
    FOREIGN KEY(vault_entry_id) REFERENCES vault_entry(id) ON DELETE SET NULL
);

CREATE INDEX IF NOT EXISTS ix_insurance_renewal      ON insurance(renewal_date);
CREATE INDEX IF NOT EXISTS ix_insurance_vault_entry  ON insurance(vault_entry_id);

-- When an insurance row is deleted, also delete its credentials vault_entry
-- (which cascades to vault_field via the existing FK in 002_vault_*.sql).
-- Without this, deleting an insurance leaves an orphan vault_entry of
-- kind 'insurance_login' that the user can never reach again.
CREATE TRIGGER IF NOT EXISTS trg_insurance_after_delete
AFTER DELETE ON insurance
BEGIN
    DELETE FROM vault_entry
    WHERE id = OLD.vault_entry_id
      AND kind = 'insurance_login';
END;

-- ---------------------------------------------------------------------------
-- Extend the file-link table to allow 'insurance' as a third polymorphic kind.
-- SQLite does not support ALTER TABLE for CHECK constraints, so we rebuild
-- the table via the documented 12-step recipe (rename, recreate, copy, drop).
-- Indexes and triggers must also be recreated since they were attached to
-- the old table name.
-- ---------------------------------------------------------------------------

ALTER TABLE attachment RENAME TO attachment_old_010;

DROP INDEX  IF EXISTS ix_attachment_target;
DROP TRIGGER IF EXISTS trg_attachment_after_bank_delete;
DROP TRIGGER IF EXISTS trg_attachment_after_vault_delete;

CREATE TABLE attachment (
    id           INTEGER PRIMARY KEY AUTOINCREMENT,
    target_kind  TEXT NOT NULL CHECK(target_kind IN ('bank_account','vault_entry','insurance')),
    target_id    INTEGER NOT NULL,
    file_name    TEXT NOT NULL,
    mime_type    TEXT,
    size_bytes   INTEGER NOT NULL CHECK(size_bytes >= 0),
    iv           BLOB NOT NULL,
    ciphertext   BLOB NOT NULL,
    tag          BLOB NOT NULL,
    created_at   TEXT NOT NULL
);

INSERT INTO attachment(id, target_kind, target_id, file_name, mime_type, size_bytes, iv, ciphertext, tag, created_at)
SELECT id, target_kind, target_id, file_name, mime_type, size_bytes, iv, ciphertext, tag, created_at
FROM attachment_old_010;

DROP TABLE attachment_old_010;

CREATE INDEX IF NOT EXISTS ix_attachment_target ON attachment(target_kind, target_id);

CREATE TRIGGER IF NOT EXISTS trg_attachment_after_bank_delete
AFTER DELETE ON bank_account
BEGIN
    DELETE FROM attachment WHERE target_kind='bank_account' AND target_id=OLD.id;
END;

CREATE TRIGGER IF NOT EXISTS trg_attachment_after_vault_delete
AFTER DELETE ON vault_entry
BEGIN
    DELETE FROM attachment WHERE target_kind='vault_entry' AND target_id=OLD.id;
END;

CREATE TRIGGER IF NOT EXISTS trg_attachment_after_insurance_delete
AFTER DELETE ON insurance
BEGIN
    DELETE FROM attachment WHERE target_kind='insurance' AND target_id=OLD.id;
END;

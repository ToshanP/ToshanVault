-- Migration 016: extend attachment table CHECK to allow general_note kind, and
-- add cascade trigger so deleting a general_note vault_entry removes its files.
--
-- General Notes reuse the vault_entry table with kind = 'general_note'. The body
-- is one rich-text field stored encrypted in vault_field under label
-- 'general_note.body'. Files are linked through the existing attachment table
-- with target_kind = 'general_note'. To allow that target_kind we have to
-- rebuild the attachment table because SQLite cannot ALTER a CHECK constraint.
-- The pattern (rename → recreate → copy → drop → recreate indexes/triggers)
-- mirrors migration 010 which added the 'insurance' kind.

ALTER TABLE attachment RENAME TO attachment_old_016;

DROP INDEX  IF EXISTS ix_attachment_target;
DROP TRIGGER IF EXISTS trg_attachment_after_bank_delete;
DROP TRIGGER IF EXISTS trg_attachment_after_vault_delete;
DROP TRIGGER IF EXISTS trg_attachment_after_insurance_delete;

CREATE TABLE attachment (
    id           INTEGER PRIMARY KEY AUTOINCREMENT,
    target_kind  TEXT NOT NULL CHECK(target_kind IN ('bank_account','vault_entry','insurance','general_note')),
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
FROM attachment_old_016;

DROP TABLE attachment_old_016;

CREATE INDEX IF NOT EXISTS ix_attachment_target ON attachment(target_kind, target_id);

-- Cascade triggers must be recreated against the new table name.
CREATE TRIGGER IF NOT EXISTS trg_attachment_after_bank_delete
AFTER DELETE ON bank_account
BEGIN
    DELETE FROM attachment WHERE target_kind='bank_account' AND target_id=OLD.id;
END;

CREATE TRIGGER IF NOT EXISTS trg_attachment_after_vault_delete
AFTER DELETE ON vault_entry
BEGIN
    -- Covers both web_login and general_note kinds (and any future kind whose
    -- attachments are stored against the vault_entry id). The general_note
    -- branch below is redundant with this one but kept explicit so future
    -- readers don't think general_note attachments leak.
    DELETE FROM attachment WHERE target_kind IN ('vault_entry','general_note') AND target_id=OLD.id;
END;

CREATE TRIGGER IF NOT EXISTS trg_attachment_after_insurance_delete
AFTER DELETE ON insurance
BEGIN
    DELETE FROM attachment WHERE target_kind='insurance' AND target_id=OLD.id;
END;

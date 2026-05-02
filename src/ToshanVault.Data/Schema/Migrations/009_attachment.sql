-- Migration 009: encrypted attachments for bank accounts + vault entries.
--
-- A polymorphic table that lets either a bank_account row OR a vault_entry row
-- carry an arbitrary number of file attachments (PDFs, screenshots, anything).
-- Files are AES-GCM encrypted in-place using the vault DEK so the DB file is
-- still the single sealed artefact (no plaintext lives anywhere on disk except
-- ephemeral OS-temp copies during open).
--
-- target_kind + target_id is a polymorphic FK — SQLite cannot enforce it
-- declaratively, so two AFTER-DELETE triggers below cascade attachment cleanup
-- when the parent row goes away. Both kinds enforce CHECK on the value to
-- prevent a typo from silently orphaning rows.

CREATE TABLE IF NOT EXISTS attachment (
    id           INTEGER PRIMARY KEY AUTOINCREMENT,
    target_kind  TEXT NOT NULL CHECK(target_kind IN ('bank_account','vault_entry')),
    target_id    INTEGER NOT NULL,
    file_name    TEXT NOT NULL,
    mime_type    TEXT,
    size_bytes   INTEGER NOT NULL CHECK(size_bytes >= 0),
    iv           BLOB NOT NULL,
    ciphertext   BLOB NOT NULL,
    tag          BLOB NOT NULL,
    created_at   TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_attachment_target
    ON attachment(target_kind, target_id);

-- Cascade attachments when their parent row is deleted. SQLite does not allow
-- ON DELETE CASCADE across a polymorphic key, hence the per-parent triggers.
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

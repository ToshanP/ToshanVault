-- Migration 020: per-owner web/vault login credentials.
--
-- Mirrors the bank_account_credential pattern (migration 006). A single
-- vault entry (website/service) may have multiple logins (one per family
-- member). For migrated entries the credential vault_entry_id points to the
-- same vault_entry as the parent (credentials already stored there).

CREATE TABLE IF NOT EXISTS web_credential (
    id              INTEGER PRIMARY KEY AUTOINCREMENT,
    entry_id        INTEGER NOT NULL REFERENCES vault_entry(id) ON DELETE CASCADE,
    owner           TEXT NOT NULL,
    vault_entry_id  INTEGER NOT NULL REFERENCES vault_entry(id),
    created_at      TEXT NOT NULL,
    updated_at      TEXT NOT NULL,
    UNIQUE(entry_id, owner)
);
CREATE INDEX IF NOT EXISTS ix_web_credential_entry
    ON web_credential(entry_id);
CREATE INDEX IF NOT EXISTS ix_web_credential_vault_entry
    ON web_credential(vault_entry_id);

-- Cascade: when a web_credential row is deleted, remove its credential
-- vault_entry ONLY if it differs from the parent entry (don't delete the
-- parent entry when removing the first migrated credential).
CREATE TRIGGER IF NOT EXISTS trg_web_credential_after_delete
AFTER DELETE ON web_credential
WHEN OLD.vault_entry_id != OLD.entry_id
BEGIN
    DELETE FROM vault_entry WHERE id = OLD.vault_entry_id;
END;

-- Back-fill: existing web_login entries that have username/password fields
-- become a credential row owned by 'Toshan'. The credential vault_entry_id
-- equals the parent entry_id (credentials live on the same entry).
INSERT INTO web_credential (entry_id, owner, vault_entry_id, created_at, updated_at)
SELECT
    ve.id,
    'Toshan',
    ve.id,
    strftime('%Y-%m-%dT%H:%M:%S+00:00', 'now'),
    strftime('%Y-%m-%dT%H:%M:%S+00:00', 'now')
FROM vault_entry ve
WHERE ve.kind = 'web_login'
  AND EXISTS (
    SELECT 1 FROM vault_field vf
    WHERE vf.entry_id = ve.id
      AND vf.label IN ('web_login.username', 'web_login.password')
  );

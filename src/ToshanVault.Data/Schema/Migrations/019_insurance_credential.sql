-- Migration 019: per-owner insurance login credentials.
--
-- Mirrors the bank_account_credential pattern (migration 006). An insurance
-- policy may have multiple online-portal logins (one per family member).
-- The legacy insurance.vault_entry_id column is left in place; existing
-- single-credential rows are back-filled with owner='Toshan'.

CREATE TABLE IF NOT EXISTS insurance_credential (
    id              INTEGER PRIMARY KEY AUTOINCREMENT,
    insurance_id    INTEGER NOT NULL REFERENCES insurance(id) ON DELETE CASCADE,
    owner           TEXT NOT NULL,
    vault_entry_id  INTEGER NOT NULL REFERENCES vault_entry(id) ON DELETE CASCADE,
    created_at      TEXT NOT NULL,
    updated_at      TEXT NOT NULL,
    UNIQUE(insurance_id, owner)
);
CREATE INDEX IF NOT EXISTS ix_insurance_credential_insurance
    ON insurance_credential(insurance_id);
CREATE INDEX IF NOT EXISTS ix_insurance_credential_entry
    ON insurance_credential(vault_entry_id);

-- Cascade vault_entry cleanup when a credential row is deleted.
CREATE TRIGGER IF NOT EXISTS trg_insurance_credential_after_delete
AFTER DELETE ON insurance_credential
BEGIN
    DELETE FROM vault_entry WHERE id = OLD.vault_entry_id;
END;

-- Back-fill: existing insurance rows with a vault_entry_id become a
-- credential row owned by 'Toshan'.
INSERT INTO insurance_credential (insurance_id, owner, vault_entry_id, created_at, updated_at)
SELECT
    i.id,
    'Toshan',
    i.vault_entry_id,
    strftime('%Y-%m-%dT%H:%M:%S+00:00', 'now'),
    strftime('%Y-%m-%dT%H:%M:%S+00:00', 'now')
FROM insurance i
WHERE i.vault_entry_id IS NOT NULL
  AND EXISTS (SELECT 1 FROM vault_entry ve WHERE ve.id = i.vault_entry_id);

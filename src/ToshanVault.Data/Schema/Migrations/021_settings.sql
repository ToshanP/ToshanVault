-- Migration 021: app settings key-value store.
-- Non-sensitive user preferences stored as plain text. Currently tracks
-- 'backup_on_exit' (0/1). Lives outside the encrypted vault — settings are
-- readable before unlock so the app can honour them at startup.

CREATE TABLE IF NOT EXISTS settings (
    key   TEXT PRIMARY KEY,
    value TEXT NOT NULL
) WITHOUT ROWID;

INSERT OR IGNORE INTO settings(key, value) VALUES ('backup_on_exit', '0');

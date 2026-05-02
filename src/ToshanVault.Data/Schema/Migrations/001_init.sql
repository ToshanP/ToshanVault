-- Migration 001: meta table.
-- Stores vault crypto material as key/value blobs. Per project-instructions.md §7.

CREATE TABLE IF NOT EXISTS meta (
    key   TEXT PRIMARY KEY,
    value BLOB NOT NULL
) WITHOUT ROWID;

INSERT OR IGNORE INTO meta(key, value) VALUES ('schema_ver', X'01');

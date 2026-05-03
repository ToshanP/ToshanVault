-- 014_sort_order.sql
-- Adds a user-controlled sort_order column to bank_account and vault_entry so
-- the UI can persist drag-and-drop ordering. Backfilled from id so existing
-- rows keep stable insertion order on first run.

ALTER TABLE bank_account ADD COLUMN sort_order INTEGER NOT NULL DEFAULT 0;
ALTER TABLE vault_entry  ADD COLUMN sort_order INTEGER NOT NULL DEFAULT 0;

UPDATE bank_account SET sort_order = id WHERE sort_order = 0;
UPDATE vault_entry  SET sort_order = id WHERE sort_order = 0;

CREATE INDEX IF NOT EXISTS ix_bank_account_sort ON bank_account(is_closed, sort_order);
CREATE INDEX IF NOT EXISTS ix_vault_entry_sort  ON vault_entry(sort_order);

-- Migration 007: enforce 1:1 credential ↔ vault_entry mapping.
--
-- Migration 006 created `bank_account_credential.vault_entry_id` with a plain
-- non-unique index. The AFTER-DELETE trigger on bank_account_credential
-- unconditionally deletes the linked vault_entry — which is correct under the
-- intended invariant of one entry per credential, but would silently destroy
-- another credential's data if the schema ever permitted two credentials to
-- share an entry (e.g. via manual DB edit or future code regression).
--
-- This migration enforces the invariant at the schema level. The unique index
-- is created idempotently so existing 006-only databases pick it up safely.
-- The non-unique index is dropped because the unique index covers the same
-- query patterns plus uniqueness.

CREATE UNIQUE INDEX IF NOT EXISTS uq_bank_account_credential_vault_entry
    ON bank_account_credential(vault_entry_id);

DROP INDEX IF EXISTS ix_bank_account_credential_entry;

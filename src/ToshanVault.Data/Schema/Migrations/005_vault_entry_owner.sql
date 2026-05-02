-- Migration 005: extend vault_entry for web/membership login entries.
--
-- Phase 6 (Vault tab). Adds a single nullable `owner` column to identify which
-- family member each entry belongs to (Toshan / Devu / Prachi / Saloni). All
-- other login fields (number, website, additional_details, username, password,
-- security Q&A) live in vault_field as encrypted blobs under the
-- `web_login.*` label namespace, mirroring the bank_login pattern.

ALTER TABLE vault_entry ADD COLUMN owner TEXT;

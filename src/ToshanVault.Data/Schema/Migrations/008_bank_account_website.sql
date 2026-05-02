-- Migration 008: bank_account.website
--
-- New optional plaintext field on bank_account so the user can stash the
-- bank's online-banking landing URL alongside the rest of the metadata.
-- Stored as TEXT (nullable) to match the rest of the optional columns.
-- No back-fill needed — existing rows simply get NULL.

ALTER TABLE bank_account ADD COLUMN website TEXT;

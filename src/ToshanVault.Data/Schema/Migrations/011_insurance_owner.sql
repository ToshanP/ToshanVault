-- Migration 011: insurance.owner column.
--
-- Mirrors VaultEntry.owner — a free-text label of who in the household the
-- policy belongs to (Toshan / Devu / Prachi / Saloni). Stored as TEXT rather
-- than enum so future household members don't require a schema migration.
-- Nullable so existing rows from migration 010 don't need backfilling.
ALTER TABLE insurance ADD COLUMN owner TEXT;

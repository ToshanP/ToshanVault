-- Migration 004: add IFSC code column to bank_account.
--
-- Phase 5c followup. IFSC (Indian Financial System Code) identifies bank
-- branches in India; needed for accounts held with Indian banks. Sits
-- alongside BSB (Australian) and account_number — purely additive, nullable.

ALTER TABLE bank_account ADD COLUMN ifsc_code TEXT;

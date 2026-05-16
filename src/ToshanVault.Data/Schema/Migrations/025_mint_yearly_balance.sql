-- 025_mint_yearly_balance.sql
-- Stores user-entered actual values for mint investment yearly balance tracking.
-- Rows are keyed by financial year end date (30 June).
CREATE TABLE IF NOT EXISTS mint_yearly_balance (
    year_end        TEXT PRIMARY KEY,
    actual_oz       REAL NOT NULL DEFAULT 0,
    actual_invested REAL NOT NULL DEFAULT 0
);

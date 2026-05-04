-- 022_mint_investment.sql
-- Configurable Perth Mint investment schedule. This is operational tracking
-- (planned buys + completion history), separate from jewellery inventory and
-- retirement projections.
CREATE TABLE IF NOT EXISTS mint_investment_plan (
    id                              INTEGER PRIMARY KEY CHECK (id = 1),
    enabled                         INTEGER NOT NULL DEFAULT 1,
    account_start_date              TEXT NOT NULL DEFAULT (date('now')),
    fortnightly_contribution_aud    REAL NOT NULL DEFAULT 500,
    working_unit_ounces             REAL NOT NULL DEFAULT 1,
    price_per_ounce_aud             REAL NOT NULL DEFAULT 5000,
    reminder_lead_days              INTEGER NOT NULL DEFAULT 14,
    consolidation_target_ounces     REAL NOT NULL DEFAULT 10,
    notes                           TEXT
);

INSERT OR IGNORE INTO mint_investment_plan(id) VALUES (1);

CREATE TABLE IF NOT EXISTS mint_investment_purchase (
    due_date                TEXT PRIMARY KEY,
    completed_date          TEXT NOT NULL,
    ounces                  REAL NOT NULL,
    price_per_ounce_aud     REAL NOT NULL,
    notes                   TEXT
);

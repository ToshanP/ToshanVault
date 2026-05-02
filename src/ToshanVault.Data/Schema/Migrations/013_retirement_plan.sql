-- 013_retirement_plan.sql
-- Single-row table holding the user's mortgage / retirement plan inputs
-- (one loan only, defaults to Redbank Plains $545,000) plus a parallel
-- gold-accumulation savings stream. The page reads and writes the row
-- with id=1; absence of the row is treated as "use defaults".
CREATE TABLE IF NOT EXISTS retirement_plan (
    id                  INTEGER PRIMARY KEY CHECK (id = 1),
    loan_name           TEXT    NOT NULL DEFAULT 'Redbank Plains',
    principal           REAL    NOT NULL DEFAULT 545000,
    annual_rate_pct     REAL    NOT NULL DEFAULT 6.0,
    term_years          INTEGER NOT NULL DEFAULT 30,
    frequency           TEXT    NOT NULL DEFAULT 'Fortnightly'
                                  CHECK (frequency IN ('Weekly','Fortnightly','Monthly')),
    extra_per_period    REAL    NOT NULL DEFAULT 0,
    start_date          TEXT    NOT NULL DEFAULT (date('now')),
    gold_per_period     REAL    NOT NULL DEFAULT 500,
    gold_growth_pct     REAL    NOT NULL DEFAULT 5.0,
    gold_start_date     TEXT    NOT NULL DEFAULT '2028-05-01',
    notes               TEXT
);

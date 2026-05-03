-- Migration 017: extend budget_item.frequency CHECK to allow 'Weekly' and
-- 'Fortnightly'. SQLite cannot ALTER a CHECK constraint so the table is
-- rebuilt (rename → recreate → copy → drop → recreate index). Pattern
-- mirrors migration 010 (insurance kind) and 016 (general_note kind).
--
-- Existing rows are preserved verbatim — their frequency values stay one
-- of the original four ('Monthly','Quarterly','Yearly','OneOff') and
-- continue to satisfy the new wider CHECK.

PRAGMA foreign_keys = OFF;

ALTER TABLE budget_item RENAME TO budget_item_old_017;

DROP INDEX IF EXISTS ix_budget_item_category;

CREATE TABLE budget_item (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    category_id INTEGER NOT NULL REFERENCES budget_category(id) ON DELETE RESTRICT,
    label       TEXT NOT NULL,
    amount      REAL NOT NULL,
    frequency   TEXT NOT NULL CHECK (frequency IN ('Weekly','Fortnightly','Monthly','Quarterly','Yearly','OneOff')),
    notes       TEXT,
    sort_order  INTEGER NOT NULL DEFAULT 0
);

INSERT INTO budget_item(id, category_id, label, amount, frequency, notes, sort_order)
SELECT id, category_id, label, amount, frequency, notes, sort_order
FROM budget_item_old_017;

DROP TABLE budget_item_old_017;

CREATE INDEX IF NOT EXISTS ix_budget_item_category ON budget_item(category_id, sort_order);

PRAGMA foreign_keys = ON;

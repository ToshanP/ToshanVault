-- Migration 015: user-controlled drag-and-drop ordering for Insurance tiles.
-- Mirrors migration 014's treatment of bank_account / vault_entry. Backfill
-- uses ROW_NUMBER() over the prior default sort (renewal date, nulls last)
-- so existing users see the same order they had before migration — drag and
-- drop only takes over once they actually reorder. The `WHERE sort_order = 0`
-- guard makes the UPDATE idempotent if the migration is ever re-run on rows
-- whose sort_order has since been touched by InsertAsync (which always
-- writes MAX(sort_order)+1, never 0).
ALTER TABLE insurance ADD COLUMN sort_order INTEGER NOT NULL DEFAULT 0;
UPDATE insurance
   SET sort_order = ranked.rn
  FROM (
       SELECT id,
              ROW_NUMBER() OVER (
                  ORDER BY (renewal_date IS NULL), renewal_date, insurer_company, id
              ) AS rn
         FROM insurance
       ) AS ranked
 WHERE insurance.id = ranked.id
   AND insurance.sort_order = 0;
CREATE INDEX IF NOT EXISTS ix_insurance_sort ON insurance(sort_order);


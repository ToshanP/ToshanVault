-- 012_recipe_tried_category.sql
-- Adds the "tried" toggle and a coarse category (Chicken / Egg / Other)
-- to the recipe table. Seeds the tried flag from the user's existing
-- spreadsheet markings (yellow-highlighted rows) and auto-classifies
-- existing rows by simple keyword match. Idempotent: ADD COLUMN is
-- guarded by SQLite's "no-op if duplicate" path via a feature check
-- pattern (we use a one-shot ALTER and rely on schema_ver gating to
-- prevent re-run).

ALTER TABLE recipe ADD COLUMN is_tried  INTEGER NOT NULL DEFAULT 0;
ALTER TABLE recipe ADD COLUMN category  TEXT    NOT NULL DEFAULT 'Other';

-- Auto-classify existing rows. SQL LIKE is good enough for the
-- import-time data; the C# RecipeCategorizer (with word boundaries)
-- handles future edits and new inserts more precisely.
UPDATE recipe
SET category = CASE
    WHEN LOWER(title) LIKE '%egg%'     THEN 'Egg'
    WHEN LOWER(title) LIKE '%chicken%' THEN 'Chicken'
    ELSE 'Other'
END;

-- Seed the user's existing "tried" markings from the spreadsheet image
-- supplied 2026-05-02. Match is case-insensitive on TRIM(title) to be
-- resilient to import whitespace variations.
UPDATE recipe SET is_tried = 1
WHERE LOWER(TRIM(title)) IN (
    'bharat kitchen',
    'dhabba style chicken curry',
    'tareewala chicken in pressure cooker',
    'chicken changezi',
    'bihari chicken',
    'kashmiri chicken',
    'punjabi tareewala chicken',
    'kolhapuri chicken',
    'hyderabadi wedding special red chicken',
    'chicken salan',
    'maharastian malvani chicken',
    'chicken chatpata gravy',
    'butter chicken',
    'dhaba style chicken curry',
    'muglai chicken curry',
    'chicken butter masala',
    'punjabi chicken gravy',
    'chicken angara',
    'hyderabadi chilli chicken',
    'hyderabadi chicken biryani',
    'paneer bhurji',
    'punjabi chicken masala',
    'butter chicken old delhi style',
    'hyderabadi masala chicken',
    'tandoori chicken tikka'
);

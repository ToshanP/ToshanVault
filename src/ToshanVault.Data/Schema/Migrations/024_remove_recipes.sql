-- 024_remove_recipes.sql
-- Recipes were removed as a top-level feature; the user keeps recipe data in
-- an external spreadsheet / Notes instead.
DROP TABLE IF EXISTS recipe_tag;
DROP TABLE IF EXISTS recipe;

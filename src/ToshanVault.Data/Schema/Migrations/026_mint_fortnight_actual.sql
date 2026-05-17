CREATE TABLE IF NOT EXISTS mint_fortnight_actual (
    fortnight_date TEXT NOT NULL PRIMARY KEY,
    actual_oz REAL NOT NULL DEFAULT 0,
    actual_contribution REAL NOT NULL DEFAULT 0
);

-- Add a dedicated notes column to insurance (independent from credential notes).
ALTER TABLE insurance ADD COLUMN notes TEXT;

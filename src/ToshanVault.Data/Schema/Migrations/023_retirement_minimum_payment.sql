-- 023_retirement_minimum_payment.sql
-- Store the bank minimum repayment directly so payoff calculations rely on:
-- minimum repayment per period + additional repayment per period.
ALTER TABLE retirement_plan ADD COLUMN minimum_payment_per_period REAL NOT NULL DEFAULT 0;

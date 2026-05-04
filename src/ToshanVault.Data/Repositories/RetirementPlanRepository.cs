using Dapper;
using ToshanVault.Core.Models;
using ToshanVault.Data.Schema;

namespace ToshanVault.Data.Repositories;

/// <summary>
/// Single-row store for the user's mortgage / retirement plan inputs
/// (id is fixed at 1; defaults are seeded by <c>013_retirement_plan.sql</c>
/// only on first read via <see cref="GetAsync"/>).
/// </summary>
public sealed class RetirementPlanRepository
{
    private readonly IDbConnectionFactory _factory;

    public RetirementPlanRepository(IDbConnectionFactory factory)
    {
        DapperSetup.EnsureInitialised();
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    public async Task<RetirementPlan> GetAsync(CancellationToken ct = default)
    {
        await using var conn = _factory.Open();
        var row = await conn.QuerySingleOrDefaultAsync<RetirementPlan>(new CommandDefinition(
            @"SELECT id, loan_name, principal, annual_rate_pct, term_years,
                     frequency, extra_per_period, start_date,
                     minimum_payment_per_period,
                     gold_per_period, gold_growth_pct, gold_start_date, notes
               FROM retirement_plan WHERE id = 1;",
            cancellationToken: ct)).ConfigureAwait(false);
        row ??= new RetirementPlan();
        if (row.MinimumPaymentPerPeriod <= 0)
        {
            row.MinimumPaymentPerPeriod = MortgageCalculator.ScheduledPayment(
                row.Principal, row.AnnualRatePct, row.TermYears, row.Frequency);
        }
        return row;
    }

    public async Task UpsertAsync(RetirementPlan p, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(p);
        await using var conn = _factory.Open();
        await conn.ExecuteAsync(new CommandDefinition(
            @"INSERT INTO retirement_plan(id, loan_name, principal, annual_rate_pct,
                                           term_years, frequency, extra_per_period,
                                           start_date, minimum_payment_per_period,
                                           gold_per_period, gold_growth_pct,
                                           gold_start_date, notes)
               VALUES (1, @LoanName, @Principal, @AnnualRatePct, @TermYears,
                       @Frequency, @ExtraPerPeriod, @StartDate,
                       @MinimumPaymentPerPeriod,
                       @GoldPerPeriod, @GoldGrowthPct, @GoldStartDate, @Notes)
               ON CONFLICT(id) DO UPDATE SET
                   loan_name        = excluded.loan_name,
                   principal        = excluded.principal,
                  annual_rate_pct  = excluded.annual_rate_pct,
                  term_years       = excluded.term_years,
                   frequency        = excluded.frequency,
                   extra_per_period = excluded.extra_per_period,
                   start_date       = excluded.start_date,
                   minimum_payment_per_period = excluded.minimum_payment_per_period,
                   gold_per_period  = excluded.gold_per_period,
                   gold_growth_pct  = excluded.gold_growth_pct,
                   gold_start_date  = excluded.gold_start_date,
                   notes            = excluded.notes;",
            new
            {
                p.LoanName, p.Principal, p.AnnualRatePct, p.TermYears,
                Frequency = p.Frequency.ToString(),
                p.ExtraPerPeriod, p.StartDate,
                p.MinimumPaymentPerPeriod,
                p.GoldPerPeriod, p.GoldGrowthPct, p.GoldStartDate,
                p.Notes,
            },
            cancellationToken: ct)).ConfigureAwait(false);
    }
}

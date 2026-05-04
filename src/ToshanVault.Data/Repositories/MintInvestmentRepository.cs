using Dapper;
using ToshanVault.Core.Models;
using ToshanVault.Data.Schema;

namespace ToshanVault.Data.Repositories;

public sealed class MintInvestmentRepository
{
    private readonly IDbConnectionFactory _factory;

    public MintInvestmentRepository(IDbConnectionFactory factory)
    {
        DapperSetup.EnsureInitialised();
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    public async Task<MintInvestmentPlan> GetPlanAsync(CancellationToken ct = default)
    {
        await using var conn = _factory.Open();
        var row = await conn.QuerySingleOrDefaultAsync<MintInvestmentPlan>(new CommandDefinition(
            @"SELECT id, enabled, account_start_date, fortnightly_contribution_aud,
                     working_unit_ounces, price_per_ounce_aud, reminder_lead_days,
                     consolidation_target_ounces, notes
              FROM mint_investment_plan WHERE id = 1;",
            cancellationToken: ct)).ConfigureAwait(false);
        return row ?? new MintInvestmentPlan();
    }

    public async Task UpsertPlanAsync(MintInvestmentPlan plan, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        await using var conn = _factory.Open();
        await conn.ExecuteAsync(new CommandDefinition(
            @"INSERT INTO mint_investment_plan(
                  id, enabled, account_start_date, fortnightly_contribution_aud,
                  working_unit_ounces, price_per_ounce_aud, reminder_lead_days,
                  consolidation_target_ounces, notes)
              VALUES (
                  1, @Enabled, @AccountStartDate, @FortnightlyContributionAud,
                  @WorkingUnitOunces, @PricePerOunceAud, @ReminderLeadDays,
                  @ConsolidationTargetOunces, @Notes)
              ON CONFLICT(id) DO UPDATE SET
                  enabled = excluded.enabled,
                  account_start_date = excluded.account_start_date,
                  fortnightly_contribution_aud = excluded.fortnightly_contribution_aud,
                  working_unit_ounces = excluded.working_unit_ounces,
                  price_per_ounce_aud = excluded.price_per_ounce_aud,
                  reminder_lead_days = excluded.reminder_lead_days,
                  consolidation_target_ounces = excluded.consolidation_target_ounces,
                  notes = excluded.notes;",
            plan,
            cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<MintInvestmentPurchase>> GetPurchasesAsync(CancellationToken ct = default)
    {
        await using var conn = _factory.Open();
        var rows = await conn.QueryAsync<MintInvestmentPurchase>(new CommandDefinition(
            @"SELECT due_date, completed_date, ounces, price_per_ounce_aud, notes
              FROM mint_investment_purchase
              ORDER BY due_date;",
            cancellationToken: ct)).ConfigureAwait(false);
        return rows.AsList();
    }

    public async Task UpsertCompletedPurchaseAsync(MintInvestmentPurchase purchase, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(purchase);
        if (purchase.CompletedDate is null)
            throw new ArgumentException("Completed purchase must have a completed date.", nameof(purchase));

        await using var conn = _factory.Open();
        await conn.ExecuteAsync(new CommandDefinition(
            @"INSERT INTO mint_investment_purchase(due_date, completed_date, ounces, price_per_ounce_aud, notes)
              VALUES (@DueDate, @CompletedDate, @Ounces, @PricePerOunceAud, @Notes)
              ON CONFLICT(due_date) DO UPDATE SET
                  completed_date = excluded.completed_date,
                  ounces = excluded.ounces,
                  price_per_ounce_aud = excluded.price_per_ounce_aud,
                  notes = excluded.notes;",
            purchase,
            cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task<int> DeletePurchaseAsync(DateOnly dueDate, CancellationToken ct = default)
    {
        await using var conn = _factory.Open();
        return await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM mint_investment_purchase WHERE due_date = @dueDate;",
            new { dueDate },
            cancellationToken: ct)).ConfigureAwait(false);
    }
}

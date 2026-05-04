namespace ToshanVault.Core.Models;

public static class MintInvestmentCalculator
{
    public sealed record Summary(
        double ContributionsToDate,
        double CompletedPurchaseCost,
        double MintAccountCash,
        double PhysicalOunces,
        double PhysicalValue,
        double ConsolidationBars,
        double LiquidOunces);

    public sealed record ScheduleRow(
        DateOnly DueDate,
        DateOnly? CompletedDate,
        double Ounces,
        double PricePerOunceAud,
        double EstimatedCost,
        double CashAfterPurchase,
        bool IsConsolidationCheckpoint);

    public static Summary Summarise(
        MintInvestmentPlan plan,
        IEnumerable<MintInvestmentPurchase> purchases,
        DateOnly asOf)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(purchases);

        var completed = purchases
            .Where(p => p.CompletedDate.HasValue && p.CompletedDate.Value <= asOf)
            .ToList();
        var contributions = ContributionsToDate(plan, asOf);
        var purchaseCost = completed.Sum(Cost);
        var ounces = completed.Sum(p => Math.Max(0, p.Ounces));
        var bars = plan.ConsolidationTargetOunces <= 0
            ? 0
            : Math.Floor(ounces / plan.ConsolidationTargetOunces);
        var liquid = plan.ConsolidationTargetOunces <= 0
            ? ounces
            : ounces - bars * plan.ConsolidationTargetOunces;

        return new Summary(
            contributions,
            purchaseCost,
            contributions - purchaseCost,
            ounces,
            ounces * Math.Max(0, plan.PricePerOunceAud),
            bars,
            liquid);
    }

    public static IReadOnlyList<ScheduleRow> GenerateSchedule(
        MintInvestmentPlan plan,
        IEnumerable<MintInvestmentPurchase> purchases,
        DateOnly asOf,
        int futureRows)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(purchases);
        if (futureRows <= 0) return Array.Empty<ScheduleRow>();

        var completedByDueDate = purchases
            .Where(p => p.CompletedDate.HasValue)
            .GroupBy(p => p.DueDate)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.CompletedDate).First());
        var completedDates = completedByDueDate.Keys.OrderBy(d => d).ToList();
        var completedIndex = 0;
        var rows = new List<ScheduleRow>();
        var cash = 0.0;
        var ounces = 0.0;
        var date = plan.AccountStartDate;
        var unitCost = UnitCost(plan);
        if (unitCost <= 0) return Array.Empty<ScheduleRow>();

        var guard = 0;
        while (rows.Count < futureRows && guard++ < 2000)
        {
            cash += Math.Max(0, plan.FortnightlyContributionAud);
            var completedThisDate = false;
            while (completedIndex < completedDates.Count && completedDates[completedIndex] <= date)
            {
                var completed = completedByDueDate[completedDates[completedIndex++]];
                var completedCost = Cost(completed);
                cash -= completedCost;
                ounces += Math.Max(0, completed.Ounces);
                completedThisDate = true;

                if (completed.DueDate >= asOf)
                {
                    rows.Add(new ScheduleRow(
                        completed.DueDate,
                        completed.CompletedDate,
                        completed.Ounces,
                        completed.PricePerOunceAud,
                        completedCost,
                        cash,
                        IsConsolidationCheckpoint(ounces, plan.ConsolidationTargetOunces)));
                    if (rows.Count >= futureRows) return rows;
                }
            }

            if (completedThisDate)
            {
                date = date.AddDays(14);
                continue;
            }

            if (cash + 0.0001 < unitCost)
            {
                date = date.AddDays(14);
                continue;
            }

            var rowOunces = plan.WorkingUnitOunces;
            var rowPrice = plan.PricePerOunceAud;
            var cost = Math.Max(0, rowOunces) * Math.Max(0, rowPrice);
            cash -= cost;
            ounces += Math.Max(0, rowOunces);

            rows.Add(new ScheduleRow(
                date,
                null,
                rowOunces,
                rowPrice,
                cost,
                cash,
                IsConsolidationCheckpoint(ounces, plan.ConsolidationTargetOunces)));

            date = date.AddDays(14);
        }

        return rows;
    }

    public static double ContributionsToDate(MintInvestmentPlan plan, DateOnly asOf)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (asOf < plan.AccountStartDate) return 0;
        var deposits = ((asOf.DayNumber - plan.AccountStartDate.DayNumber) / 14) + 1;
        return deposits * Math.Max(0, plan.FortnightlyContributionAud);
    }

    public static double UnitCost(MintInvestmentPlan plan)
        => Math.Max(0, plan.WorkingUnitOunces) * Math.Max(0, plan.PricePerOunceAud);

    private static double Cost(MintInvestmentPurchase purchase)
        => Math.Max(0, purchase.Ounces) * Math.Max(0, purchase.PricePerOunceAud);

    private static bool IsConsolidationCheckpoint(double ounces, double target)
    {
        if (target <= 0 || ounces < target) return false;
        return Math.Abs(ounces % target) < 0.0001;
    }
}

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

    public sealed record YearProjection(
        int YearNumber,
        DateOnly YearEnd,
        double ContributedThisYear,
        double TotalContributed,
        double MintAccountCash,
        double PhysicalOunces,
        double PhysicalValue,
        double TotalValue);

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

    public static IReadOnlyList<YearProjection> ProjectYearValues(
        MintInvestmentPlan plan,
        IEnumerable<MintInvestmentPurchase> purchases,
        IReadOnlyList<DateOnly> yearEnds)
        => ProjectYearValues(plan, purchases, yearEnds, DateOnly.FromDateTime(DateTime.Today));

    public static IReadOnlyList<YearProjection> ProjectYearValues(
        MintInvestmentPlan plan,
        IEnumerable<MintInvestmentPurchase> purchases,
        IReadOnlyList<DateOnly> yearEnds,
        DateOnly forecastFrom)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(purchases);
        ArgumentNullException.ThrowIfNull(yearEnds);
        if (yearEnds.Count == 0) return Array.Empty<YearProjection>();

        var completedPurchases = purchases
            .Where(p => p.CompletedDate.HasValue)
            .OrderBy(p => p.CompletedDate!.Value)
            .ThenBy(p => p.DueDate)
            .ToList();
        var completedIndex = 0;
        var rows = new List<YearProjection>(yearEnds.Count);
        var cash = 0.0;
        var totalContributed = 0.0;
        var yearContributed = 0.0;
        var ounces = 0.0;
        var date = plan.AccountStartDate;
        var contribution = Math.Max(0, plan.FortnightlyContributionAud);
        var unitOunces = Math.Max(0, plan.WorkingUnitOunces);
        var unitPrice = Math.Max(0, plan.PricePerOunceAud);
        var unitCost = unitOunces * unitPrice;

        for (var yearIndex = 0; yearIndex < yearEnds.Count; yearIndex++)
        {
            var yearEnd = yearEnds[yearIndex];
            while (date <= yearEnd)
            {
                cash += contribution;
                totalContributed += contribution;
                yearContributed += contribution;

                var completedThisCycle = ProcessCompletedPurchases(completedPurchases, ref completedIndex, date, ref cash, ref ounces);

                if (!completedThisCycle && date >= forecastFrom && unitCost > 0 && cash + 0.0001 >= unitCost)
                {
                    cash -= unitCost;
                    ounces += unitOunces;
                }

                date = date.AddDays(14);
            }
            ProcessCompletedPurchases(completedPurchases, ref completedIndex, yearEnd, ref cash, ref ounces);

            var physicalValue = ounces * unitPrice;
            rows.Add(new YearProjection(
                yearIndex + 1,
                yearEnd,
                yearContributed,
                totalContributed,
                cash,
                ounces,
                physicalValue,
                cash + physicalValue));
            yearContributed = 0;
        }

        return rows;
    }

    private static bool ProcessCompletedPurchases(
        IReadOnlyList<MintInvestmentPurchase> completedPurchases,
        ref int completedIndex,
        DateOnly throughDate,
        ref double cash,
        ref double ounces)
    {
        var processedAny = false;
        while (completedIndex < completedPurchases.Count &&
               completedPurchases[completedIndex].CompletedDate!.Value <= throughDate)
        {
            var completed = completedPurchases[completedIndex++];
            cash -= Cost(completed);
            ounces += Math.Max(0, completed.Ounces);
            processedAny = true;
        }
        return processedAny;
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

    public sealed record FortnightSnapshot(
        DateOnly Date,
        double Contribution,
        double CashBalance,
        double PurchaseOz,
        double RunningOz,
        double RunningValue);

    /// <summary>
    /// Generates per-fortnight snapshots from plan start through the target FY,
    /// using stored actuals where available and projections otherwise.
    /// Returns only the fortnights within [fyStart+1day .. fyEnd].
    /// </summary>
    public static IReadOnlyList<FortnightSnapshot> GenerateFortnightDetails(
        MintInvestmentPlan plan,
        IReadOnlyDictionary<DateOnly, MintFortnightActual> allActuals,
        DateOnly fyStart,
        DateOnly fyEnd)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(allActuals);

        var price = Math.Max(0, plan.PricePerOunceAud);
        var defaultContrib = Math.Max(0, plan.FortnightlyContributionAud);
        var unitOz = Math.Max(0, plan.WorkingUnitOunces);
        var unitCost = unitOz * price;

        var cash = 0.0;
        var oz = 0.0;
        var date = plan.AccountStartDate;
        var results = new List<FortnightSnapshot>();

        while (date <= fyEnd)
        {
            double contrib, purchaseOz;

            if (allActuals.TryGetValue(date, out var actual))
            {
                contrib = actual.ActualContribution;
                purchaseOz = actual.ActualOz;
            }
            else
            {
                contrib = defaultContrib;
                purchaseOz = (unitCost > 0 && cash + contrib + 0.0001 >= unitCost) ? unitOz : 0;
            }

            cash += contrib;
            cash -= purchaseOz * price;
            oz += purchaseOz;

            if (date > fyStart)
            {
                results.Add(new FortnightSnapshot(date, contrib, cash, purchaseOz, oz, oz * price));
            }

            date = date.AddDays(14);
        }

        return results;
    }

    private static bool IsConsolidationCheckpoint(double ounces, double target)
    {
        if (target <= 0 || ounces < target) return false;
        return Math.Abs(ounces % target) < 0.0001;
    }
}

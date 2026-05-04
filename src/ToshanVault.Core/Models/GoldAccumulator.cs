using System;
using System.Collections.Generic;

namespace ToshanVault.Core.Models;

/// <summary>
/// Pure projection of a periodic gold-savings stream. Each period the
/// existing balance grows at the periodic rate (annual / periodsPerYear)
/// and the contribution is added at period end. Lives in Core so it can
/// be unit-tested without WinUI.
/// </summary>
public static class GoldAccumulator
{
    public sealed record YearValue(
        int YearNumber,
        DateOnly YearEnd,
        double Contributed,
        double EndingValue,
        double PhysicalSpent = 0,
        double OuncesHeld = 0);

    public sealed record Result(
        double TotalContributed,
        double FinalValue,
        IReadOnlyList<YearValue> YearValues,
        double TotalPhysicalSpent = 0,
        double TotalOunces = 0);

    public sealed record PhysicalPurchasePlan(
        bool Enabled,
        double BarOunces,
        int IntervalMonths,
        double PricePerOunceAud,
        DateOnly StartDate);

    /// <summary>
    /// Projects gold value period-by-period from <paramref name="goldStart"/>
    /// for <paramref name="totalPeriods"/> periods of the loan schedule
    /// (so year boundaries align with the loan's year boundaries). Periods
    /// before <paramref name="goldStart"/> contribute nothing but are still
    /// counted for the year-end timeline.
    /// </summary>
    public static Result Project(
        double perPeriod,
        double annualGrowthPct,
        RepaymentFrequency frequency,
        DateOnly loanStart,
        DateOnly goldStart,
        int totalPeriods)
        => Project(perPeriod, annualGrowthPct, frequency, loanStart, goldStart, totalPeriods, null);

    public static Result Project(
        double perPeriod,
        double annualGrowthPct,
        RepaymentFrequency frequency,
        DateOnly loanStart,
        DateOnly goldStart,
        int totalPeriods,
        PhysicalPurchasePlan? physicalPlan)
    {
        if (perPeriod < 0) perPeriod = 0;
        if (annualGrowthPct < 0) annualGrowthPct = 0;
        var periodsPerYear = MortgageCalculator.PeriodsPerYear(frequency);
        var r = (annualGrowthPct / 100.0) / periodsPerYear;

        var cashBalance = 0.0;
        var cashContributed = 0.0;
        var physicalSpent = 0.0;
        var ouncesHeld = 0.0;
        var currentPricePerOunce = Math.Max(0, physicalPlan?.PricePerOunceAud ?? 0);
        var nextPurchaseDate = FirstPurchaseOnOrAfter(physicalPlan, loanStart);
        var rows = new List<YearValue>();
        var yearCashContrib = 0.0;
        var yearPhysicalSpent = 0.0;

        for (var i = 1; i <= totalPeriods; i++)
        {
            var periodEnd = AddPeriods(loanStart, i, frequency);
            cashBalance *= (1 + r);
            currentPricePerOunce *= (1 + r);
            if (periodEnd >= goldStart)
            {
                cashBalance += perPeriod;
                cashContributed += perPeriod;
                yearCashContrib += perPeriod;
            }

            while (physicalPlan?.Enabled == true &&
                   nextPurchaseDate is { } due &&
                   due < periodEnd)
            {
                var spent = Math.Max(0, physicalPlan.BarOunces) * currentPricePerOunce;
                ouncesHeld += Math.Max(0, physicalPlan.BarOunces);
                physicalSpent += spent;
                yearPhysicalSpent += spent;
                cashBalance -= spent;
                nextPurchaseDate = due.AddMonths(Math.Max(1, physicalPlan.IntervalMonths));
            }

            if (i % periodsPerYear == 0 || i == totalPeriods)
            {
                var yearNumber = (i + periodsPerYear - 1) / periodsPerYear;
                var physicalValue = ouncesHeld * currentPricePerOunce;
                rows.Add(new YearValue(
                    yearNumber,
                    periodEnd,
                    yearCashContrib,
                    cashBalance + physicalValue,
                    yearPhysicalSpent,
                    ouncesHeld));
                yearCashContrib = 0;
                yearPhysicalSpent = 0;
            }
        }

        return new Result(cashContributed, rows.Count == 0 ? 0 : rows[^1].EndingValue, rows, physicalSpent, ouncesHeld);
    }

    public static DateOnly? GetNextPurchaseDueDate(
        PhysicalPurchasePlan physicalPlan,
        DateOnly today,
        IEnumerable<DateOnly> completedDueDates)
    {
        if (!physicalPlan.Enabled ||
            physicalPlan.BarOunces <= 0 ||
            physicalPlan.IntervalMonths <= 0)
        {
            return null;
        }

        var completed = new HashSet<DateOnly>(completedDueDates);
        var due = physicalPlan.StartDate;
        while (due <= today && completed.Contains(due))
        {
            due = due.AddMonths(physicalPlan.IntervalMonths);
        }

        return due;
    }

    private static DateOnly? FirstPurchaseOnOrAfter(PhysicalPurchasePlan? plan, DateOnly date)
    {
        if (plan is not { Enabled: true } ||
            plan.BarOunces <= 0 ||
            plan.IntervalMonths <= 0 ||
            plan.PricePerOunceAud <= 0)
        {
            return null;
        }

        var due = plan.StartDate;
        while (due < date)
        {
            due = due.AddMonths(plan.IntervalMonths);
        }

        return due;
    }

    private static DateOnly AddPeriods(DateOnly start, int periods, RepaymentFrequency f) => f switch
    {
        RepaymentFrequency.Weekly      => start.AddDays(7 * periods),
        RepaymentFrequency.Fortnightly => start.AddDays(14 * periods),
        RepaymentFrequency.Monthly     => start.AddMonths(periods),
        _ => throw new ArgumentOutOfRangeException(nameof(f)),
    };
}

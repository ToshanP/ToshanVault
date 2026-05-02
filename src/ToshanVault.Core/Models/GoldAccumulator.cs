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
    public sealed record YearValue(int YearNumber, DateOnly YearEnd, double Contributed, double EndingValue);

    public sealed record Result(
        double TotalContributed,
        double FinalValue,
        IReadOnlyList<YearValue> YearValues);

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
    {
        if (perPeriod < 0) perPeriod = 0;
        if (annualGrowthPct < 0) annualGrowthPct = 0;
        var periodsPerYear = MortgageCalculator.PeriodsPerYear(frequency);
        var r = (annualGrowthPct / 100.0) / periodsPerYear;

        var balance = 0.0;
        var contributed = 0.0;
        var rows = new List<YearValue>();
        var yearContrib = 0.0;

        for (var i = 1; i <= totalPeriods; i++)
        {
            var periodEnd = AddPeriods(loanStart, i, frequency);
            balance *= (1 + r);
            if (periodEnd >= goldStart)
            {
                balance += perPeriod;
                contributed += perPeriod;
                yearContrib += perPeriod;
            }

            if (i % periodsPerYear == 0 || i == totalPeriods)
            {
                var yearNumber = (i + periodsPerYear - 1) / periodsPerYear;
                rows.Add(new YearValue(yearNumber, periodEnd, yearContrib, balance));
                yearContrib = 0;
            }
        }

        return new Result(contributed, balance, rows);
    }

    private static DateOnly AddPeriods(DateOnly start, int periods, RepaymentFrequency f) => f switch
    {
        RepaymentFrequency.Weekly      => start.AddDays(7 * periods),
        RepaymentFrequency.Fortnightly => start.AddDays(14 * periods),
        RepaymentFrequency.Monthly     => start.AddMonths(periods),
        _ => throw new ArgumentOutOfRangeException(nameof(f)),
    };
}

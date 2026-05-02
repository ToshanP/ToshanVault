using System;
using System.Collections.Generic;

namespace ToshanVault.Core.Models;

public enum RepaymentFrequency
{
    Weekly,
    Fortnightly,
    Monthly,
}

/// <summary>
/// Pure mortgage amortization math. Lives in the Core assembly so it can be
/// unit-tested without WinUI. Australian-bank convention: the periodic rate
/// is the nominal annual rate divided by the number of periods per year
/// (no compounding adjustment).
/// </summary>
public static class MortgageCalculator
{
    public static int PeriodsPerYear(RepaymentFrequency f) => f switch
    {
        RepaymentFrequency.Weekly      => 52,
        RepaymentFrequency.Fortnightly => 26,
        RepaymentFrequency.Monthly     => 12,
        _ => throw new ArgumentOutOfRangeException(nameof(f)),
    };

    /// <summary>
    /// Scheduled periodic payment for a fully-amortising loan. Returns
    /// principal/n when the rate is zero. Throws if the inputs are invalid.
    /// </summary>
    public static double ScheduledPayment(double principal, double annualRatePct,
                                          int termYears, RepaymentFrequency frequency)
    {
        if (principal <= 0) throw new ArgumentOutOfRangeException(nameof(principal));
        if (termYears <= 0) throw new ArgumentOutOfRangeException(nameof(termYears));
        if (annualRatePct < 0) throw new ArgumentOutOfRangeException(nameof(annualRatePct));

        var n = termYears * PeriodsPerYear(frequency);
        var r = (annualRatePct / 100.0) / PeriodsPerYear(frequency);
        if (r == 0) return principal / n;
        return principal * r / (1 - Math.Pow(1 + r, -n));
    }

    public sealed record AmortizationResult(
        double ScheduledPayment,
        double ActualPayment,
        int    PeriodsToPayoff,
        DateOnly PayoffDate,
        double TotalInterest,
        double TotalPaid,
        IReadOnlyList<YearSummary> YearSummaries);

    public sealed record YearSummary(
        int    YearNumber,
        DateOnly YearEnd,
        double InterestPaid,
        double PrincipalPaid,
        double EndingBalance);

    /// <summary>
    /// Runs the amortization. <paramref name="extraPerPeriod"/> is added to
    /// every payment until the loan is paid off (capped on the final period
    /// so we don't overshoot zero). Safety cap: 100 years of periods.
    /// </summary>
    public static AmortizationResult Amortize(
        double principal,
        double annualRatePct,
        int termYears,
        RepaymentFrequency frequency,
        double extraPerPeriod,
        DateOnly startDate)
    {
        var scheduled = ScheduledPayment(principal, annualRatePct, termYears, frequency);
        if (extraPerPeriod < 0) extraPerPeriod = 0;
        var actual = scheduled + extraPerPeriod;

        var periodsPerYear = PeriodsPerYear(frequency);
        var r = (annualRatePct / 100.0) / periodsPerYear;
        var maxPeriods = 100 * periodsPerYear;

        // Guard: payment must cover at least the first period's interest, else
        // the balance would grow forever. (Only possible with a negative extra,
        // which we already clamped, but be defensive.)
        var firstInterest = principal * r;
        if (actual <= firstInterest && r > 0)
            throw new InvalidOperationException(
                "Payment does not cover interest; loan would never be repaid.");

        var balance = principal;
        var totalInterest = 0.0;
        var totalPaid = 0.0;
        var periods = 0;

        var yearSummaries = new List<YearSummary>();
        var yearInterest = 0.0;
        var yearPrincipal = 0.0;

        while (balance > 0.0001 && periods < maxPeriods)
        {
            periods++;
            var interest = balance * r;
            var principalPart = actual - interest;
            double paidThisPeriod;

            if (principalPart >= balance)
            {
                // Final period: pay only what's owed plus its interest.
                principalPart = balance;
                paidThisPeriod = principalPart + interest;
                balance = 0;
            }
            else
            {
                paidThisPeriod = actual;
                balance -= principalPart;
            }

            totalPaid += paidThisPeriod;
            totalInterest += interest;
            yearInterest += interest;
            yearPrincipal += principalPart;

            if (periods % periodsPerYear == 0 || balance <= 0.0001)
            {
                var yearNumber = (periods + periodsPerYear - 1) / periodsPerYear;
                var yearEnd = AddPeriods(startDate, periods, frequency);
                yearSummaries.Add(new YearSummary(
                    yearNumber, yearEnd, yearInterest, yearPrincipal, Math.Max(0, balance)));
                yearInterest = 0;
                yearPrincipal = 0;
            }
        }

        var payoffDate = AddPeriods(startDate, periods, frequency);
        return new AmortizationResult(
            scheduled, actual, periods, payoffDate, totalInterest, totalPaid, yearSummaries);
    }

    private static DateOnly AddPeriods(DateOnly start, int periods, RepaymentFrequency f) => f switch
    {
        RepaymentFrequency.Weekly      => start.AddDays(7 * periods),
        RepaymentFrequency.Fortnightly => start.AddDays(14 * periods),
        RepaymentFrequency.Monthly     => start.AddMonths(periods),
        _ => throw new ArgumentOutOfRangeException(nameof(f)),
    };
}

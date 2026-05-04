using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Serilog;
using ToshanVault.Core.Models;
using ToshanVault.Data.Repositories;

namespace ToshanVault_App.Services;

public sealed class MintInvestmentReminderService
{
    private static readonly CultureInfo Aud = CultureInfo.GetCultureInfo("en-AU");
    private readonly MintInvestmentRepository _repo;
    private bool _shownThisLaunch;

    public MintInvestmentReminderService(MintInvestmentRepository repo)
    {
        _repo = repo ?? throw new ArgumentNullException(nameof(repo));
    }

    public async Task ShowIfDueAsync(XamlRoot xamlRoot, Action openMintInvestment)
    {
        ArgumentNullException.ThrowIfNull(xamlRoot);
        ArgumentNullException.ThrowIfNull(openMintInvestment);
        try
        {
            if (_shownThisLaunch) return;

            var plan = await _repo.GetPlanAsync().ConfigureAwait(true);
            if (!plan.Enabled) return;

            var purchases = await _repo.GetPurchasesAsync().ConfigureAwait(true);
            var today = DateOnly.FromDateTime(DateTime.Today);
            var next = MintInvestmentCalculator.GenerateSchedule(plan, purchases, today, 24)
                .FirstOrDefault(x => !x.CompletedDate.HasValue);
            if (next is null) return;

            var daysUntilDue = next.DueDate.DayNumber - today.DayNumber;
            if (daysUntilDue > plan.ReminderLeadDays) return;

            _shownThisLaunch = true;
            var summary = MintInvestmentCalculator.Summarise(plan, purchases, today);
            var lastCompleted = purchases
                .Where(x => x.CompletedDate.HasValue)
                .OrderByDescending(x => x.CompletedDate!.Value)
                .FirstOrDefault();
            var lastCompletedText = lastCompleted?.CompletedDate is { } completedDate
                ? $"It has been {MonthsBetween(completedDate, today)} month(s) since your last completed Mint purchase ({completedDate:dd MMM yyyy})."
                : "No Mint Investment purchase has been marked complete yet.";
            var status = daysUntilDue < 0
                ? $"overdue by {-daysUntilDue} day(s)"
                : daysUntilDue == 0
                    ? "due today"
                    : $"due in {daysUntilDue} day(s)";

            var dialog = new ContentDialog
            {
                XamlRoot = xamlRoot,
                Title = "Mint Investment reminder",
                Content =
                    $"Your next scheduled {plan.WorkingUnitOunces:N1} oz purchase is {status}: {next.DueDate:dd MMM yyyy}.\n\n" +
                    $"Estimated cost: {next.EstimatedCost.ToString("C0", Aud)}. Mint account cash after completed buys: {summary.MintAccountCash.ToString("C0", Aud)}.\n\n" +
                    $"Recorded physical gold: {summary.PhysicalOunces:N1} oz. {lastCompletedText}\n\n" +
                    "Open Mint Investment to tick the purchase as completed.",
                PrimaryButtonText = "Open Mint Investment",
                CloseButtonText = "Dismiss",
                DefaultButton = ContentDialogButton.Primary,
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                openMintInvestment();
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Mint Investment reminder failed");
        }
    }

    private static int MonthsBetween(DateOnly from, DateOnly to)
    {
        var months = (to.Year - from.Year) * 12 + to.Month - from.Month;
        if (to.Day < from.Day) months--;
        return Math.Max(0, months);
    }
}

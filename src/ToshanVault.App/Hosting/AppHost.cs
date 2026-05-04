using System;
using Microsoft.Extensions.DependencyInjection;
using ToshanVault.Core.Security;
using ToshanVault.Data.Repositories;
using ToshanVault.Data.Schema;
using ToshanVault_App.Services;

namespace ToshanVault_App.Hosting;

/// <summary>
/// Static composition root. Built once at app launch, before the first window
/// activates. <see cref="Services"/> is the global service provider used by
/// pages and ViewModels (resolved via <see cref="GetService{T}"/>).
/// </summary>
public static class AppHost
{
    private static IServiceProvider? _services;

    public static IServiceProvider Services
        => _services ?? throw new InvalidOperationException("AppHost.Build() must be called before Services is read.");

    public static T GetService<T>() where T : notnull
        => Services.GetRequiredService<T>();

    /// <summary>
    /// Builds the DI container. Idempotent.
    /// </summary>
    public static void Build(string? connectionStringOverride = null)
    {
        if (_services is not null) return;

        AppPaths.EnsureDataDirectory();
        var connStr = connectionStringOverride ?? AppPaths.BuildConnectionString();

        DapperSetup.EnsureInitialised();

        var sc = new ServiceCollection();

        sc.AddSingleton<IDbConnectionFactory>(_ => new SqliteConnectionFactory(connStr));
        sc.AddSingleton<MigrationRunner>();
        sc.AddSingleton<MetaRepository>();
        sc.AddSingleton<IMetaStore>(sp => sp.GetRequiredService<MetaRepository>());
        sc.AddSingleton<Vault>();

        sc.AddSingleton<BudgetCategoryRepository>();
        sc.AddSingleton<BudgetItemRepository>();
        sc.AddSingleton<RetirementItemRepository>();
        sc.AddSingleton<RetirementPlanRepository>();
        sc.AddSingleton<MintInvestmentRepository>();
        sc.AddSingleton<GoldItemRepository>();
        sc.AddSingleton<GoldPriceCacheRepository>();
        sc.AddSingleton<VaultEntryRepository>();
        sc.AddSingleton<VaultFieldRepository>();
        sc.AddSingleton<RecipeRepository>();
        sc.AddSingleton<BankAccountRepository>();
        sc.AddSingleton<BankAccountCredentialRepository>();
        sc.AddSingleton<BankCredentialsService>();
        sc.AddSingleton<WebCredentialsService>();
        sc.AddSingleton<WebCredentialRepository>();
        sc.AddSingleton<GeneralNotesService>();
        sc.AddSingleton<AttachmentService>();
        sc.AddSingleton<InsuranceRepository>();
        sc.AddSingleton<InsuranceCredentialsService>();
        sc.AddSingleton<InsuranceCredentialRepository>();
        sc.AddSingleton<SettingsRepository>();

        sc.AddSingleton<IdleLockService>();
        sc.AddSingleton<NavigationService>();
        sc.AddSingleton<GoldPriceService>();
        sc.AddSingleton<MintInvestmentReminderService>();

        _services = sc.BuildServiceProvider(validateScopes: true);
    }
}

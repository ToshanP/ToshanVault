using Dapper;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ToshanVault.Core.Models;
using ToshanVault.Core.Security;
using ToshanVault.Data.Repositories;
using ToshanVault.Data.Schema;

namespace ToshanVault.Tests.Repositories;

[TestClass]
public class BankAccountCredentialMultiOwnerTests
{
    private const string Pwd = "!Arvind@Nivas83!";

    private static async Task<(TestDbFactory f, Vault v, BankCredentialsService svc, BankAccountCredentialRepository credRepo, BankAccountRepository bankRepo)> SetupAsync()
    {
        var f = await TestDbFactory.CreateMigratedAsync();
        var meta = new MetaRepository(f);
        var v = new Vault(meta);
        await v.InitialiseAsync(Pwd);
        return (f, v, new BankCredentialsService(f, v), new BankAccountCredentialRepository(f), new BankAccountRepository(f));
    }

    [TestMethod]
    public async Task TwoOwners_OnSameAccount_AreStoredIndependently()
    {
        var (f, v, svc, credRepo, bankRepo) = await SetupAsync();
        using var _ = f; using var __ = v;

        var id = await bankRepo.InsertAsync(new BankAccount { Bank = "ANZ", AccountName = "Joint", AccountType = BankAccountType.Savings });

        var toshanEntry = await svc.SaveAsync(id, "Toshan", "ANZ Joint (Toshan)", new[]
        {
            new BankCredentialsService.FieldSpec(BankCredentialsService.UsernameLabel, "toshan_user", false),
            new BankCredentialsService.FieldSpec(BankCredentialsService.PasswordLabel, "T-pwd", true),
        });
        var devEntry = await svc.SaveAsync(id, "Devangini", "ANZ Joint (Devangini)", new[]
        {
            new BankCredentialsService.FieldSpec(BankCredentialsService.UsernameLabel, "dev_user", false),
            new BankCredentialsService.FieldSpec(BankCredentialsService.PasswordLabel, "D-pwd", true),
        });

        toshanEntry.Should().NotBe(devEntry, "each owner has its own vault_entry");

        var creds = await credRepo.GetByAccountAsync(id);
        creds.Should().HaveCount(2);
        creds.Select(c => c.Owner).Should().BeEquivalentTo(new[] { "Devangini", "Toshan" });

        // Cross-checking: each entry stores only its own owner's fields.
        (await svc.LoadAsync(toshanEntry))[BankCredentialsService.UsernameLabel].Should().Be("toshan_user");
        (await svc.LoadAsync(devEntry))[BankCredentialsService.UsernameLabel].Should().Be("dev_user");
    }

    [TestMethod]
    public async Task DeleteCredential_CascadesVaultEntryAndFields_ViaTrigger()
    {
        var (f, v, svc, credRepo, bankRepo) = await SetupAsync();
        using var _ = f; using var __ = v;

        var id = await bankRepo.InsertAsync(new BankAccount { Bank = "ANZ", AccountName = "Joint", AccountType = BankAccountType.Savings });
        var entryId = await svc.SaveAsync(id, "Toshan", "ANZ", new[]
        {
            new BankCredentialsService.FieldSpec(BankCredentialsService.UsernameLabel, "u", false),
            new BankCredentialsService.FieldSpec(BankCredentialsService.PasswordLabel, "p", true),
        });

        var rows = await credRepo.GetByAccountAsync(id);
        var credId = rows.Single().Id;
        await credRepo.DeleteAsync(credId);

        await using var c = f.Open();
        (await c.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM bank_account_credential WHERE id=@i;", new { i = credId })).Should().Be(0);
        (await c.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM vault_entry WHERE id=@i;", new { i = entryId })).Should().Be(0, "trigger cascades vault_entry");
        (await c.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM vault_field WHERE entry_id=@i;", new { i = entryId })).Should().Be(0, "vault_field cascades from vault_entry");
    }

    [TestMethod]
    public async Task DeletingBankAccount_CascadesAllItsCredentialsAndVaultEntries()
    {
        var (f, v, svc, credRepo, bankRepo) = await SetupAsync();
        using var _ = f; using var __ = v;

        var id = await bankRepo.InsertAsync(new BankAccount { Bank = "ANZ", AccountName = "Joint", AccountType = BankAccountType.Savings });
        await svc.SaveAsync(id, "Toshan", "x", new[] { new BankCredentialsService.FieldSpec(BankCredentialsService.UsernameLabel, "u1", false) });
        await svc.SaveAsync(id, "Devangini", "x", new[] { new BankCredentialsService.FieldSpec(BankCredentialsService.UsernameLabel, "u2", false) });

        await bankRepo.DeleteAsync(id);

        await using var c = f.Open();
        (await c.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM bank_account_credential WHERE bank_account_id=@i;", new { i = id })).Should().Be(0);
        (await c.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM vault_entry WHERE kind='bank_login';")).Should().Be(0, "trigger ran for both credentials");
    }

    [TestMethod]
    public async Task UniqueConstraint_PreventsTwoCredentialsForSameOwner()
    {
        var (f, v, svc, _, bankRepo) = await SetupAsync();
        using var _f = f; using var _v = v;

        var id = await bankRepo.InsertAsync(new BankAccount { Bank = "ANZ", AccountName = "Joint", AccountType = BankAccountType.Savings });
        await svc.SaveAsync(id, "Toshan", "x", new[] { new BankCredentialsService.FieldSpec(BankCredentialsService.UsernameLabel, "first", false) });

        // Second SaveAsync with same owner should NOT create a duplicate row — it
        // upserts in place. (UNIQUE(account, owner) makes a duplicate INSERT throw,
        // so this also verifies the upsert path.)
        await svc.SaveAsync(id, "Toshan", "x", new[] { new BankCredentialsService.FieldSpec(BankCredentialsService.UsernameLabel, "second", false) });

        await using var c = f.Open();
        var count = await c.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM bank_account_credential WHERE bank_account_id=@i AND owner='Toshan';", new { i = id });
        count.Should().Be(1);
    }
}

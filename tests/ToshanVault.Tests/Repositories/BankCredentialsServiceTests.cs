using Dapper;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ToshanVault.Core.Models;
using ToshanVault.Core.Security;
using ToshanVault.Data.Repositories;
using ToshanVault.Data.Schema;

namespace ToshanVault.Tests.Repositories;

[TestClass]
public class BankCredentialsServiceTests
{
    private const string Pwd = "!Arvind@Nivas83!";

    private const string Owner = "Toshan";

    private static async Task<(TestDbFactory f, Vault vault, BankCredentialsService svc, BankAccountRepository bankRepo)> SetupAsync()
    {
        var f = await TestDbFactory.CreateMigratedAsync();
        var meta = new MetaRepository(f);
        var vault = new Vault(meta);
        await vault.InitialiseAsync(Pwd);
        return (f, vault, new BankCredentialsService(f, vault), new BankAccountRepository(f));
    }

    private static BankAccount Sample() => new()
    {
        Bank = "ANZ", AccountName = "Joint Everyday",
        AccountType = BankAccountType.Savings,
    };

    [TestMethod]
    public async Task SaveAsync_FirstTime_CreatesEntry_LinksAndPersistsFields()
    {
        var (f, vault, svc, bankRepo) = await SetupAsync();
        using var _ = f; using var __ = vault;

        var id = await bankRepo.InsertAsync(Sample());
        var entryId = await svc.SaveAsync(id, Owner, "ANZ · Joint", new[]
        {
            new BankCredentialsService.FieldSpec(BankCredentialsService.UsernameLabel, "alice", false),
            new BankCredentialsService.FieldSpec(BankCredentialsService.PasswordLabel, "s3cret", true),
        });

        // Migration 006: linkage now lives in bank_account_credential, not on bank_account.
        await using var c = f.Open();
        var link = await c.ExecuteScalarAsync<long?>(
            "SELECT vault_entry_id FROM bank_account_credential WHERE bank_account_id=@id AND owner=@o;",
            new { id, o = Owner });
        link.Should().Be(entryId);

        var loaded = await svc.LoadAsync(entryId);
        loaded[BankCredentialsService.UsernameLabel].Should().Be("alice");
        loaded[BankCredentialsService.PasswordLabel].Should().Be("s3cret");
    }

    [TestMethod]
    public async Task SaveAsync_EmptyValue_DeletesField_NoEmptyEncryptedRows()
    {
        var (f, vault, svc, bankRepo) = await SetupAsync();
        using var _ = f; using var __ = vault;

        var id = await bankRepo.InsertAsync(Sample());
        var entryId = await svc.SaveAsync(id, Owner, "ANZ · Joint", new[]
        {
            new BankCredentialsService.FieldSpec(BankCredentialsService.UsernameLabel, "alice", false),
        });

        // Save again with empty username → field should be deleted.
        await svc.SaveAsync(id, Owner, "ANZ · Joint", new[]
        {
            new BankCredentialsService.FieldSpec(BankCredentialsService.UsernameLabel, "", false),
        });

        var loaded = await svc.LoadAsync(entryId);
        loaded.ContainsKey(BankCredentialsService.UsernameLabel).Should().BeFalse();

        // No empty rows linger in the table.
        await using var conn = f.Open();
        var count = await conn.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM vault_field WHERE entry_id=@e;", new { e = entryId });
        count.Should().Be(0);
    }

    [TestMethod]
    public async Task SaveAsync_SecondCall_ReusesSameEntry_DoesNotCreateOrphans()
    {
        var (f, vault, svc, bankRepo) = await SetupAsync();
        using var _ = f; using var __ = vault;

        var id = await bankRepo.InsertAsync(Sample());
        var first = await svc.SaveAsync(id, Owner, "ANZ · Joint", new[]
        {
            new BankCredentialsService.FieldSpec(BankCredentialsService.UsernameLabel, "alice", false),
        });
        var second = await svc.SaveAsync(id, Owner, "ANZ · Joint", new[]
        {
            new BankCredentialsService.FieldSpec(BankCredentialsService.UsernameLabel, "bob", false),
        });
        first.Should().Be(second);

        await using var conn = f.Open();
        var entryCount = await conn.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM vault_entry;");
        entryCount.Should().Be(1);

        var loaded = await svc.LoadAsync(first);
        loaded[BankCredentialsService.UsernameLabel].Should().Be("bob");
    }

    [TestMethod]
    public async Task SaveAsync_VaultEntryDeletedExternally_RecreatesAndRelinksAtomically()
    {
        var (f, vault, svc, bankRepo) = await SetupAsync();
        using var _ = f; using var __ = vault;

        var id = await bankRepo.InsertAsync(Sample());
        var first = await svc.SaveAsync(id, Owner, "ANZ · Joint", new[]
        {
            new BankCredentialsService.FieldSpec(BankCredentialsService.UsernameLabel, "alice", false),
        });

        // Simulate vault entry being deleted from another tab while dialog is open.
        // The AFTER DELETE trigger on bank_account_credential cascades vault_entry,
        // but here we go the other way to exercise the recreate path. We must
        // disable FK enforcement on this connection too — otherwise the
        // ON DELETE CASCADE on bank_account_credential.vault_entry_id would
        // remove the credential row before we can test orphan-recovery.
        await using (var c = f.Open())
        {
            await c.ExecuteAsync("PRAGMA foreign_keys=OFF;");
            await c.ExecuteAsync("DELETE FROM vault_entry WHERE id=@id;", new { id = first });
        }

        // Save should recreate + relink, not throw FK.
        var second = await svc.SaveAsync(id, Owner, "ANZ · Joint", new[]
        {
            new BankCredentialsService.FieldSpec(BankCredentialsService.PasswordLabel, "newpwd", true),
        });
        second.Should().NotBe(first);

        await using var c2 = f.Open();
        var link = await c2.ExecuteScalarAsync<long?>(
            "SELECT vault_entry_id FROM bank_account_credential WHERE bank_account_id=@id AND owner=@o;",
            new { id, o = Owner });
        link.Should().Be(second);

        var loaded = await svc.LoadAsync(second);
        loaded[BankCredentialsService.PasswordLabel].Should().Be("newpwd");
    }

    [TestMethod]
    public async Task SaveAsync_RoundTripsCardPin_PhonePin_AndNotesFields()
    {
        var (f, vault, svc, bankRepo) = await SetupAsync();
        using var _ = f; using var __ = vault;

        var id = await bankRepo.InsertAsync(Sample());
        var rtfNotes = @"{\rtf1\ansi {\b Bold} note}";
        var entryId = await svc.SaveAsync(id, Owner, "ANZ · Joint", new[]
        {
            new BankCredentialsService.FieldSpec(BankCredentialsService.UsernameLabel, "alice", false),
            new BankCredentialsService.FieldSpec(BankCredentialsService.CardPinLabel,  "1234",  true),
            new BankCredentialsService.FieldSpec(BankCredentialsService.PhonePinLabel, "9876",  true),
            new BankCredentialsService.FieldSpec(BankCredentialsService.NotesLabel,    rtfNotes, false),
        });

        var loaded = await svc.LoadAsync(entryId);
        loaded[BankCredentialsService.CardPinLabel].Should().Be("1234");
        loaded[BankCredentialsService.PhonePinLabel].Should().Be("9876");
        loaded[BankCredentialsService.NotesLabel].Should().Be(rtfNotes);

        await using var c = f.Open();
        var secrets = (await c.QueryAsync<(string label, int isSecret)>(
            "SELECT label, is_secret FROM vault_field WHERE entry_id=@e ORDER BY label;",
            new { e = entryId })).ToList();
        secrets.Should().Contain(s => s.label == BankCredentialsService.CardPinLabel  && s.isSecret == 1);
        secrets.Should().Contain(s => s.label == BankCredentialsService.PhonePinLabel && s.isSecret == 1);
        // Notes is encrypted but not flagged secret — UI doesn't mask it.
        secrets.Should().Contain(s => s.label == BankCredentialsService.NotesLabel    && s.isSecret == 0);
    }

    [TestMethod]
    public async Task LoadAsync_NamespaceFilter_IgnoresUserCreatedFieldsWithSimilarNames()
    {
        var (f, vault, svc, bankRepo) = await SetupAsync();
        using var _ = f; using var __ = vault;

        var id = await bankRepo.InsertAsync(Sample());
        var entryId = await svc.SaveAsync(id, Owner, "ANZ · Joint", new[]
        {
            new BankCredentialsService.FieldSpec(BankCredentialsService.UsernameLabel, "alice", false),
        });

        // User adds a non-namespaced field labelled "username" via the Vault tab.
        var fieldRepo = new VaultFieldRepository(f, vault);
        await fieldRepo.InsertAsync(new VaultField
        { EntryId = entryId, Label = "username", Value = "user-typed-value", IsSecret = false });

        var loaded = await svc.LoadAsync(entryId);
        loaded[BankCredentialsService.UsernameLabel].Should().Be("alice");
        loaded.ContainsKey("username").Should().BeFalse(); // user's field is excluded by prefix filter
    }
}

[TestClass]
public class Migration003ForwardCompatTests
{
    [TestMethod]
    public async Task Migration003_PreservesPreExistingClosedAccountRows()
    {
        // Build a fresh DB that only has closed_account (simulating an installation that
        // ever wrote to the legacy table), then run migration 003 directly and assert
        // the rows are forward-migrated into bank_account with is_closed=1.
        var cs = $"Data Source=file:m003-{Guid.NewGuid():N}?mode=memory&cache=shared";
        await using var keepAlive = new SqliteConnection(cs);
        await keepAlive.OpenAsync();

        await using (var c = new SqliteConnection(cs))
        {
            await c.OpenAsync();
            await c.ExecuteAsync(@"
                CREATE TABLE closed_account (
                    id INTEGER PRIMARY KEY,
                    bank TEXT, account_number TEXT,
                    closed_date TEXT, reason TEXT, notes TEXT);
                INSERT INTO closed_account (bank, account_number, closed_date, reason, notes)
                VALUES ('CBA','••••1111','2024-01-15T00:00:00+00:00','superseded','old card'),
                       (NULL ,'unknown',  NULL,                       NULL,        NULL);
                CREATE TABLE vault_entry (id INTEGER PRIMARY KEY, kind TEXT, name TEXT,
                                          category TEXT, tags TEXT, created_at TEXT, updated_at TEXT);
            ");
        }

        // Read migration 003 from embedded resource and run it.
        var asm = typeof(BankAccountRepository).Assembly;
        var resName = asm.GetManifestResourceNames()
            .First(n => n.EndsWith("003_bank_account.sql", StringComparison.OrdinalIgnoreCase));
        using var stream = asm.GetManifestResourceStream(resName)!;
        using var reader = new StreamReader(stream);
        var sql = await reader.ReadToEndAsync();

        await using (var c = new SqliteConnection(cs))
        {
            await c.OpenAsync();
            await c.ExecuteAsync(sql);
        }

        await using (var c = new SqliteConnection(cs))
        {
            await c.OpenAsync();
            var rows = (await c.QueryAsync<(string Bank, string AccountName, string? AccountNumber,
                                            long IsClosed, string? CloseReason)>(
                @"SELECT bank, account_name, account_number, is_closed, close_reason
                  FROM bank_account ORDER BY id;")).ToList();
            rows.Should().HaveCount(2);
            rows[0].Bank.Should().Be("CBA");
            rows[0].AccountName.Should().Be("CBA"); // bank used as account_name fallback
            rows[0].IsClosed.Should().Be(1);
            rows[0].CloseReason.Should().Be("superseded");
            rows[1].Bank.Should().Be("(unknown)"); // null bank → "(unknown)" sentinel

            // closed_account is gone.
            var stillThere = await c.ExecuteScalarAsync<long>(
                "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='closed_account';");
            stillThere.Should().Be(0);
        }
    }
}

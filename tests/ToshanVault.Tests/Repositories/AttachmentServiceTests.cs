using System.Security.Cryptography;
using Dapper;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ToshanVault.Core.Models;
using ToshanVault.Core.Security;
using ToshanVault.Data.Repositories;
using ToshanVault.Data.Schema;

namespace ToshanVault.Tests.Repositories;

[TestClass]
public class AttachmentServiceTests
{
    private const string Pwd = "!Arvind@Nivas83!";

    private static async Task<(TestDbFactory f, Vault v, AttachmentService svc, BankAccountRepository bankRepo, VaultEntryRepository entryRepo)> SetupAsync()
    {
        var f = await TestDbFactory.CreateMigratedAsync();
        var meta = new MetaRepository(f);
        var v = new Vault(meta);
        await v.InitialiseAsync(Pwd);
        return (f, v, new AttachmentService(f, v), new BankAccountRepository(f), new VaultEntryRepository(f));
    }

    private static byte[] RandomBytes(int n)
    {
        var b = new byte[n]; RandomNumberGenerator.Fill(b); return b;
    }

    [TestMethod]
    public async Task AddAsync_RoundTrip_DecryptsToOriginalBytes()
    {
        var (f, v, svc, bankRepo, _) = await SetupAsync();
        using var _f = f; using var _v = v;

        var id = await bankRepo.InsertAsync(new BankAccount { Bank = "ANZ", AccountName = "X", AccountType = BankAccountType.Savings });
        var original = RandomBytes(2048);
        var copy = (byte[])original.Clone();

        var attId = await svc.AddAsync(Attachment.KindBankAccount, id, "test.pdf", "application/pdf", copy);
        var path = await svc.DecryptToTempAsync(attId);
        try
        {
            var decrypted = await File.ReadAllBytesAsync(path);
            decrypted.Should().Equal(original);
        }
        finally { try { File.Delete(path); } catch { } }
    }

    [TestMethod]
    public async Task AddAsync_ZeroesPlaintextBuffer_AfterEncrypt()
    {
        var (f, v, svc, bankRepo, _) = await SetupAsync();
        using var _f = f; using var _v = v;
        var id = await bankRepo.InsertAsync(new BankAccount { Bank = "B", AccountName = "X", AccountType = BankAccountType.Savings });
        var pt = RandomBytes(64);
        await svc.AddAsync(Attachment.KindBankAccount, id, "a.bin", null, pt);
        // Service must have zeroed the caller-supplied buffer to avoid plaintext lingering.
        pt.All(b => b == 0).Should().BeTrue();
    }

    [TestMethod]
    public async Task AddAsync_OverHardLimit_Throws()
    {
        var (f, v, svc, bankRepo, _) = await SetupAsync();
        using var _f = f; using var _v = v;
        var id = await bankRepo.InsertAsync(new BankAccount { Bank = "B", AccountName = "X", AccountType = BankAccountType.Savings });
        var huge = new byte[AttachmentService.MaxFileBytes + 1];
        Func<Task> act = () => svc.AddAsync(Attachment.KindBankAccount, id, "big.bin", null, huge);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [TestMethod]
    public async Task AddAsync_OverPerTargetCount_Throws()
    {
        var (f, v, svc, bankRepo, _) = await SetupAsync();
        using var _f = f; using var _v = v;
        var id = await bankRepo.InsertAsync(new BankAccount { Bank = "B", AccountName = "X", AccountType = BankAccountType.Savings });
        for (int i = 0; i < AttachmentService.MaxAttachmentsPerTarget; i++)
            await svc.AddAsync(Attachment.KindBankAccount, id, $"f{i}.bin", null, RandomBytes(8));
        Func<Task> act = () => svc.AddAsync(Attachment.KindBankAccount, id, "extra.bin", null, RandomBytes(8));
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [TestMethod]
    public async Task DeletingBankAccount_CascadesAttachmentsViaTrigger()
    {
        var (f, v, svc, bankRepo, _) = await SetupAsync();
        using var _f = f; using var _v = v;
        var id = await bankRepo.InsertAsync(new BankAccount { Bank = "B", AccountName = "X", AccountType = BankAccountType.Savings });
        await svc.AddAsync(Attachment.KindBankAccount, id, "a.bin", null, RandomBytes(8));
        (await svc.CountAsync(Attachment.KindBankAccount, id)).Should().Be(1);

        await using var c = f.Open();
        await c.ExecuteAsync("DELETE FROM bank_account WHERE id=@id;", new { id });

        (await svc.CountAsync(Attachment.KindBankAccount, id)).Should().Be(0);
    }

    [TestMethod]
    public async Task DeletingVaultEntry_CascadesAttachmentsViaTrigger()
    {
        var (f, v, svc, _, entryRepo) = await SetupAsync();
        using var _f = f; using var _v = v;
        var entryId = await entryRepo.InsertAsync(new VaultEntry { Kind = "web", Name = "Netflix" });
        await svc.AddAsync(Attachment.KindVaultEntry, entryId, "a.bin", null, RandomBytes(8));
        (await svc.CountAsync(Attachment.KindVaultEntry, entryId)).Should().Be(1);

        await using var c = f.Open();
        await c.ExecuteAsync("DELETE FROM vault_entry WHERE id=@id;", new { id = entryId });

        (await svc.CountAsync(Attachment.KindVaultEntry, entryId)).Should().Be(0);
    }

    [TestMethod]
    public async Task DecryptToTempAsync_TamperedCiphertext_Throws()
    {
        var (f, v, svc, bankRepo, _) = await SetupAsync();
        using var _f = f; using var _v = v;
        var id = await bankRepo.InsertAsync(new BankAccount { Bank = "B", AccountName = "X", AccountType = BankAccountType.Savings });
        var attId = await svc.AddAsync(Attachment.KindBankAccount, id, "a.bin", null, RandomBytes(64));

        await using (var c = f.Open())
        {
            // Flip a single ciphertext byte — AES-GCM tag verification must reject.
            await c.ExecuteAsync(@"
                UPDATE attachment
                SET ciphertext = (
                    SELECT (substr(ciphertext, 1, 1) || char((unicode(substr(ciphertext,1,1)) + 1) % 256) || substr(ciphertext, 3))
                    FROM attachment WHERE id = @id
                )
                WHERE id = @id;", new { id = attId });
        }
        Func<Task> act = () => svc.DecryptToTempAsync(attId);
        await act.Should().ThrowAsync<Exception>();
    }

    [TestMethod]
    public async Task AddAsync_InvalidKind_Throws()
    {
        var (f, v, svc, _, _) = await SetupAsync();
        using var _f = f; using var _v = v;
        Func<Task> act = () => svc.AddAsync("nope", 1, "a.bin", null, RandomBytes(8));
        await act.Should().ThrowAsync<ArgumentException>();
    }
}

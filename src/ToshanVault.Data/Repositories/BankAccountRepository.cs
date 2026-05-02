using Dapper;
using Microsoft.Data.Sqlite;
using ToshanVault.Core.Models;
using ToshanVault.Data.Schema;

namespace ToshanVault.Data.Repositories;

/// <summary>
/// CRUD for <see cref="BankAccount"/>. Active vs closed is filtered by the
/// <c>is_closed</c> flag — there is no separate archive table. Credentials
/// for the account live in <c>vault_entry</c>/<c>vault_field</c>, linked
/// through <see cref="BankAccount.VaultEntryId"/>; this repo never touches
/// those rows directly.
/// </summary>
public sealed class BankAccountRepository
{
    private readonly IDbConnectionFactory _factory;

    public BankAccountRepository(IDbConnectionFactory factory)
    {
        DapperSetup.EnsureInitialised();
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    public async Task<long> InsertAsync(BankAccount a, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(a);
        if (string.IsNullOrWhiteSpace(a.Bank)) throw new ArgumentException("Bank required.", nameof(a));
        if (string.IsNullOrWhiteSpace(a.AccountName)) throw new ArgumentException("AccountName required.", nameof(a));

        if (a.CreatedAt == default) a.CreatedAt = DateTimeOffset.UtcNow;
        a.UpdatedAt = DateTimeOffset.UtcNow;

        await using var conn = _factory.Open();
        var id = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
            @"INSERT INTO bank_account
                (bank, account_name, bsb, ifsc_code, account_number, account_type,
                 holder_name, interest_rate_pct, notes,
                 is_closed, closed_date, close_reason,
                 vault_entry_id, created_at, updated_at)
              VALUES
                (@Bank, @AccountName, @Bsb, @IfscCode, @AccountNumber, @AccountType,
                 @HolderName, @InterestRatePct, @Notes,
                 @IsClosed, @ClosedDate, @CloseReason,
                 @VaultEntryId, @CreatedAt, @UpdatedAt);
              SELECT last_insert_rowid();",
            new
            {
                a.Bank,
                a.AccountName,
                a.Bsb,
                a.IfscCode,
                a.AccountNumber,
                AccountType = a.AccountType.ToString(),
                a.HolderName,
                a.InterestRatePct,
                a.Notes,
                a.IsClosed,
                a.ClosedDate,
                a.CloseReason,
                a.VaultEntryId,
                a.CreatedAt,
                a.UpdatedAt,
            },
            cancellationToken: ct)).ConfigureAwait(false);
        a.Id = id;
        return id;
    }

    /// <summary>Updates non-status fields. Status transitions go through
    /// <see cref="CloseAsync"/> / <see cref="ReopenAsync"/>.</summary>
    public async Task UpdateAsync(BankAccount a, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(a);
        a.UpdatedAt = DateTimeOffset.UtcNow;
        await using var conn = _factory.Open();
        var rows = await conn.ExecuteAsync(new CommandDefinition(
            @"UPDATE bank_account SET
                bank=@Bank, account_name=@AccountName, bsb=@Bsb, ifsc_code=@IfscCode,
                account_number=@AccountNumber, account_type=@AccountType,
                holder_name=@HolderName, interest_rate_pct=@InterestRatePct,
                notes=@Notes, vault_entry_id=@VaultEntryId, updated_at=@UpdatedAt
              WHERE id=@Id;",
            new
            {
                a.Id, a.Bank, a.AccountName, a.Bsb, a.IfscCode, a.AccountNumber,
                AccountType = a.AccountType.ToString(),
                a.HolderName, a.InterestRatePct, a.Notes,
                a.VaultEntryId, a.UpdatedAt,
            },
            cancellationToken: ct)).ConfigureAwait(false);
        if (rows == 0) throw new InvalidOperationException($"BankAccount {a.Id} not found.");
    }

    /// <summary>Marks an active account as closed. Idempotent if already closed
    /// (the timestamp/reason are NOT overwritten on a second close).</summary>
    public async Task CloseAsync(long id, string? reason, DateTimeOffset? closedAt = null, CancellationToken ct = default)
    {
        var stamp = (closedAt ?? DateTimeOffset.UtcNow);
        await using var conn = _factory.Open();
        await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync(ct).ConfigureAwait(false);

        var current = await conn.QuerySingleOrDefaultAsync<int?>(new CommandDefinition(
            "SELECT is_closed FROM bank_account WHERE id=@id;",
            new { id }, tx, cancellationToken: ct)).ConfigureAwait(false);
        if (current is null) throw new InvalidOperationException($"BankAccount {id} not found.");
        if (current == 1) { await tx.CommitAsync(ct).ConfigureAwait(false); return; }

        await conn.ExecuteAsync(new CommandDefinition(
            @"UPDATE bank_account
                 SET is_closed=1, closed_date=@stamp, close_reason=@reason, updated_at=@stamp
               WHERE id=@id;",
            new { id, stamp, reason }, tx, cancellationToken: ct)).ConfigureAwait(false);

        await tx.CommitAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Re-activates a closed account, clearing closed_date + close_reason.</summary>
    public async Task ReopenAsync(long id, CancellationToken ct = default)
    {
        await using var conn = _factory.Open();
        var rows = await conn.ExecuteAsync(new CommandDefinition(
            @"UPDATE bank_account
                 SET is_closed=0, closed_date=NULL, close_reason=NULL, updated_at=@stamp
               WHERE id=@id AND is_closed=1;",
            new { id, stamp = DateTimeOffset.UtcNow },
            cancellationToken: ct)).ConfigureAwait(false);
        if (rows == 0) throw new InvalidOperationException($"BankAccount {id} not found or not closed.");
    }

    public async Task<int> DeleteAsync(long id, CancellationToken ct = default)
    {
        await using var conn = _factory.Open();
        return await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM bank_account WHERE id=@id;", new { id }, cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task<BankAccount?> GetAsync(long id, CancellationToken ct = default)
    {
        await using var conn = _factory.Open();
        return await conn.QuerySingleOrDefaultAsync<BankAccount>(new CommandDefinition(
            SelectColumns + " WHERE id=@id;", new { id }, cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<BankAccount>> GetActiveAsync(CancellationToken ct = default)
    {
        await using var conn = _factory.Open();
        var rows = await conn.QueryAsync<BankAccount>(new CommandDefinition(
            SelectColumns + " WHERE is_closed=0 ORDER BY bank, account_name;",
            cancellationToken: ct)).ConfigureAwait(false);
        return rows.AsList();
    }

    public async Task<IReadOnlyList<BankAccount>> GetAllAsync(CancellationToken ct = default)
    {
        await using var conn = _factory.Open();
        var rows = await conn.QueryAsync<BankAccount>(new CommandDefinition(
            SelectColumns + " ORDER BY is_closed, bank, account_name;",
            cancellationToken: ct)).ConfigureAwait(false);
        return rows.AsList();
    }

    public async Task<IReadOnlyList<BankAccount>> GetClosedAsync(CancellationToken ct = default)
    {
        await using var conn = _factory.Open();
        var rows = await conn.QueryAsync<BankAccount>(new CommandDefinition(
            SelectColumns + " WHERE is_closed=1 ORDER BY closed_date DESC, bank;",
            cancellationToken: ct)).ConfigureAwait(false);
        return rows.AsList();
    }

    private const string SelectColumns =
        @"SELECT id, bank, account_name, bsb, ifsc_code, account_number, account_type,
                 holder_name, interest_rate_pct, notes,
                 is_closed, closed_date, close_reason,
                 vault_entry_id, created_at, updated_at
            FROM bank_account";
}

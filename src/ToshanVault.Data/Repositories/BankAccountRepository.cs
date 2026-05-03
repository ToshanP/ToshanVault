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
        // Append new accounts at the end of the relevant section by giving them
        // the largest sort_order seen so far + 1. (Open and Closed share one
        // sequence — sort_order is unique per row but the WHERE filter on
        // is_closed slices the lists at read time.)
        if (a.SortOrder == 0)
        {
            var maxOrder = await conn.ExecuteScalarAsync<long?>(new CommandDefinition(
                "SELECT MAX(sort_order) FROM bank_account;",
                cancellationToken: ct)).ConfigureAwait(false) ?? 0;
            a.SortOrder = (int)Math.Min(int.MaxValue, maxOrder + 1);
        }

        var id = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
            @"INSERT INTO bank_account
                (bank, account_name, bsb, ifsc_code, account_number, account_type,
                 holder_name, interest_rate_pct, notes, website,
                 is_closed, closed_date, close_reason,
                 vault_entry_id, sort_order, created_at, updated_at)
              VALUES
                (@Bank, @AccountName, @Bsb, @IfscCode, @AccountNumber, @AccountType,
                 @HolderName, @InterestRatePct, @Notes, @Website,
                 @IsClosed, @ClosedDate, @CloseReason,
                 @VaultEntryId, @SortOrder, @CreatedAt, @UpdatedAt);
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
                a.Website,
                a.IsClosed,
                a.ClosedDate,
                a.CloseReason,
                a.VaultEntryId,
                a.SortOrder,
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
                notes=@Notes, website=@Website, vault_entry_id=@VaultEntryId, updated_at=@UpdatedAt
              WHERE id=@Id;",
            new
            {
                a.Id, a.Bank, a.AccountName, a.Bsb, a.IfscCode, a.AccountNumber,
                AccountType = a.AccountType.ToString(),
                a.HolderName, a.InterestRatePct, a.Notes, a.Website,
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
            SelectColumns + " WHERE is_closed=0 ORDER BY sort_order, id;",
            cancellationToken: ct)).ConfigureAwait(false);
        return rows.AsList();
    }

    public async Task<IReadOnlyList<BankAccount>> GetAllAsync(CancellationToken ct = default)
    {
        await using var conn = _factory.Open();
        var rows = await conn.QueryAsync<BankAccount>(new CommandDefinition(
            SelectColumns + " ORDER BY is_closed, sort_order, id;",
            cancellationToken: ct)).ConfigureAwait(false);
        return rows.AsList();
    }

    public async Task<IReadOnlyList<BankAccount>> GetClosedAsync(CancellationToken ct = default)
    {
        await using var conn = _factory.Open();
        var rows = await conn.QueryAsync<BankAccount>(new CommandDefinition(
            SelectColumns + " WHERE is_closed=1 ORDER BY sort_order, id;",
            cancellationToken: ct)).ConfigureAwait(false);
        return rows.AsList();
    }

    /// <summary>
    /// Persists the user-controlled order of accounts within a slice (Open or
    /// Closed). The caller passes the IDs in the order they should appear; this
    /// method assigns sort_order = index (1-based) in a single transaction so
    /// the ordering is atomic and a partial failure leaves no half-applied state.
    /// IDs not in the input list are NOT touched, allowing the Open and Closed
    /// lists to be reordered independently.
    /// </summary>
    public async Task UpdateSortOrderAsync(IReadOnlyList<long> orderedIds, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(orderedIds);
        if (orderedIds.Count == 0) return;
        await using var conn = _factory.Open();
        await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
        var stamp = DateTimeOffset.UtcNow;
        for (var i = 0; i < orderedIds.Count; i++)
        {
            await conn.ExecuteAsync(new CommandDefinition(
                "UPDATE bank_account SET sort_order=@order, updated_at=@stamp WHERE id=@id;",
                new { order = i + 1, stamp, id = orderedIds[i] },
                tx, cancellationToken: ct)).ConfigureAwait(false);
        }
        await tx.CommitAsync(ct).ConfigureAwait(false);
    }

    private const string SelectColumns =
        @"SELECT id, bank, account_name, bsb, ifsc_code, account_number, account_type,
                 holder_name, interest_rate_pct, notes, website,
                 is_closed, closed_date, close_reason,
                 vault_entry_id, sort_order, created_at, updated_at
            FROM bank_account";
}

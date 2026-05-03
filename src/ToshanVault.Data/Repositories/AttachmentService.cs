using System.Security.Cryptography;
using Dapper;
using ToshanVault.Core.Models;
using ToshanVault.Core.Security;
using ToshanVault.Data.Schema;

namespace ToshanVault.Data.Repositories;

/// <summary>
/// Encrypted file attachments for bank accounts and vault entries.
///
/// All ciphertext is held inline in the SQLite <c>attachment.ciphertext</c>
/// BLOB column. We never write decrypted bytes to disk except as a transient
/// OS-temp copy created by <see cref="DecryptToTempAsync"/> for the OS shell
/// to open with the user's default app — those temp files carry a known
/// prefix so <see cref="SweepOrphanedTempFilesAsync"/> can clean them up on
/// the next app launch in case of a crash mid-view.
///
/// Limits enforced here, not in the schema, so they can evolve without a
/// migration. Per-target cap of 20 attachments and 50 MB per file keep
/// vault.db backups manageable while leaving room for typical statements.
/// </summary>
public sealed class AttachmentService
{
    public const long MaxFileBytes = 50L * 1024 * 1024; // hard cap, 50 MB
    public const long SoftWarnBytes = 10L * 1024 * 1024; // UI hint
    public const int MaxAttachmentsPerTarget = 20;

    /// <summary>Prefix for ToshanVault decrypted-attachment temp files. The
    /// app sweeps any leftover at startup so a crash mid-view doesn't leak
    /// plaintext indefinitely.</summary>
    public const string TempFilePrefix = "ToshanVault-att-";

    private readonly IDbConnectionFactory _factory;
    private readonly Vault _vault;

    public AttachmentService(IDbConnectionFactory factory, Vault vault)
    {
        DapperSetup.EnsureInitialised();
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _vault = vault ?? throw new ArgumentNullException(nameof(vault));
    }

    /// <summary>Encrypts <paramref name="plaintext"/> and inserts a new row.
    /// Plaintext bytes are zeroed after encryption regardless of outcome.</summary>
    public async Task<long> AddAsync(
        string targetKind,
        long targetId,
        string fileName,
        string? mimeType,
        byte[] plaintext,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        ValidateKind(targetKind);
        if (string.IsNullOrWhiteSpace(fileName)) throw new ArgumentException("File name required.", nameof(fileName));
        if (plaintext.LongLength > MaxFileBytes)
            throw new InvalidOperationException($"Attachment exceeds {MaxFileBytes / (1024 * 1024)} MB hard limit.");

        var existingCount = await CountAsync(targetKind, targetId, ct).ConfigureAwait(false);
        if (existingCount >= MaxAttachmentsPerTarget)
            throw new InvalidOperationException($"Cannot add more than {MaxAttachmentsPerTarget} attachments per item.");

        AesGcmCrypto.Sealed sealedBlob;
        long originalSize = plaintext.LongLength;
        try
        {
            // Encrypt outside the transaction (fast-fail if vault locked).
            sealedBlob = _vault.EncryptField(plaintext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }

        await using var conn = _factory.Open();
        var id = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
            @"INSERT INTO attachment
                (target_kind, target_id, file_name, mime_type, size_bytes,
                 iv, ciphertext, tag, created_at)
              VALUES (@TargetKind, @TargetId, @FileName, @MimeType, @SizeBytes,
                      @Iv, @Ciphertext, @Tag, @CreatedAt);
              SELECT last_insert_rowid();",
            new
            {
                TargetKind = targetKind,
                TargetId = targetId,
                FileName = fileName,
                MimeType = mimeType,
                SizeBytes = originalSize,
                sealedBlob.Iv,
                sealedBlob.Ciphertext,
                sealedBlob.Tag,
                CreatedAt = DateTimeOffset.UtcNow,
            },
            cancellationToken: ct)).ConfigureAwait(false);
        return id;
    }

    /// <summary>Lists metadata only (no payload) — cheap for tile/list rendering.</summary>
    public async Task<IReadOnlyList<Attachment>> ListAsync(
        string targetKind, long targetId, CancellationToken ct = default)
    {
        ValidateKind(targetKind);
        await using var conn = _factory.Open();
        var rows = await conn.QueryAsync<Attachment>(new CommandDefinition(
            @"SELECT id AS Id, target_kind AS TargetKind, target_id AS TargetId,
                     file_name AS FileName, mime_type AS MimeType, size_bytes AS SizeBytes,
                     created_at AS CreatedAt
              FROM attachment WHERE target_kind=@k AND target_id=@id
              ORDER BY created_at DESC, id DESC;",
            new { k = targetKind, id = targetId },
            cancellationToken: ct)).ConfigureAwait(false);
        return rows.AsList();
    }

    /// <summary>Cheap count for tile badges. Returns 0 for unknown ids.</summary>
    public async Task<int> CountAsync(string targetKind, long targetId, CancellationToken ct = default)
    {
        ValidateKind(targetKind);
        await using var conn = _factory.Open();
        return (int)await conn.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT COUNT(1) FROM attachment WHERE target_kind=@k AND target_id=@id;",
            new { k = targetKind, id = targetId },
            cancellationToken: ct)).ConfigureAwait(false);
    }

    /// <summary>Decrypts the row to a brand-new OS temp file with a recognisable
    /// prefix and the original file name suffix so the OS shell picks the right
    /// app. Caller is responsible for deleting the file when done; the next-
    /// launch sweep is the safety net if that's missed (e.g. crash).</summary>
    public async Task<string> DecryptToTempAsync(long attachmentId, CancellationToken ct = default)
    {
        await using var conn = _factory.Open();
        var row = await conn.QuerySingleOrDefaultAsync<(byte[] Iv, byte[] Ciphertext, byte[] Tag, string FileName)?>(
            new CommandDefinition(
                "SELECT iv AS Iv, ciphertext AS Ciphertext, tag AS Tag, file_name AS FileName FROM attachment WHERE id=@id;",
                new { id = attachmentId },
                cancellationToken: ct)).ConfigureAwait(false);
        if (row is null) throw new InvalidOperationException($"Attachment {attachmentId} not found.");

        var pt = _vault.DecryptField(row.Value.Iv, row.Value.Ciphertext, row.Value.Tag);
        try
        {
            // Sanitise file name to keep only the suffix. Path.GetFileName guards
            // against any embedded directory separators in stored names.
            var safeName = Path.GetFileName(row.Value.FileName);
            if (string.IsNullOrWhiteSpace(safeName)) safeName = "attachment.bin";
            var tempPath = Path.Combine(Path.GetTempPath(), $"{TempFilePrefix}{Guid.NewGuid():N}-{safeName}");
            await File.WriteAllBytesAsync(tempPath, pt, ct).ConfigureAwait(false);
            return tempPath;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pt);
        }
    }

    public async Task DeleteAsync(long attachmentId, CancellationToken ct = default)
    {
        await using var conn = _factory.Open();
        await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM attachment WHERE id=@id;",
            new { id = attachmentId },
            cancellationToken: ct)).ConfigureAwait(false);
    }

    /// <summary>Best-effort sweep of leftover decrypted temp files from a prior
    /// run that crashed mid-view. Locked / in-use files are skipped silently;
    /// the next launch will retry. Always safe to call.</summary>
    public static int SweepOrphanedTempFiles()
    {
        var swept = 0;
        try
        {
            var temp = Path.GetTempPath();
            foreach (var f in Directory.EnumerateFiles(temp, TempFilePrefix + "*"))
            {
                try { File.Delete(f); swept++; }
                catch { /* in-use: leave for next launch */ }
            }
        }
        catch { /* TEMP unreadable: nothing useful to do */ }
        return swept;
    }

    private static void ValidateKind(string targetKind)
    {
        if (targetKind != Attachment.KindBankAccount
            && targetKind != Attachment.KindVaultEntry
            && targetKind != Attachment.KindInsurance
            && targetKind != Attachment.KindGeneralNote)
            throw new ArgumentException($"Unknown target kind '{targetKind}'.", nameof(targetKind));
    }
}

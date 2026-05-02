namespace ToshanVault.Core.Models;

public enum BudgetCategoryType
{
    Income,
    Fixed,
    Variable,
}

public enum BudgetFrequency
{
    Monthly,
    Quarterly,
    Yearly,
    OneOff,
}

public enum RetirementKind
{
    Income,
    Expense,
}

public sealed class BudgetCategory
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public BudgetCategoryType Type { get; set; }
}

public sealed class BudgetItem
{
    public long Id { get; set; }
    public long CategoryId { get; set; }
    public string Label { get; set; } = string.Empty;
    public double Amount { get; set; }
    public BudgetFrequency Frequency { get; set; }
    public string? Notes { get; set; }
    public int SortOrder { get; set; }
}

public sealed class RetirementItem
{
    public long Id { get; set; }
    public string Label { get; set; } = string.Empty;
    public RetirementKind Kind { get; set; }
    public double MonthlyAmountJan2025 { get; set; }
    public double InflationPct { get; set; }
    public bool Indexed { get; set; }
    public int? StartAge { get; set; }
    public int? EndAge { get; set; }
    public string? Notes { get; set; }
}

public sealed class GoldItem
{
    public long Id { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public string Purity { get; set; } = string.Empty;
    public double Qty { get; set; }
    public double Tola { get; set; }
    public double GramsCalc { get; set; }
    public string? Notes { get; set; }
}

public sealed class GoldPriceCache
{
    public string Currency { get; set; } = string.Empty;
    public double PricePerGram24k { get; set; }
    public DateTimeOffset FetchedAt { get; set; }
}

public enum VaultOwner
{
    Toshan,
    Devu,
    Prachi,
    Saloni,
}

public sealed class VaultEntry
{
    public long Id { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Category { get; set; }
    public string? Tags { get; set; }
    public string? Owner { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// In-memory representation of a vault field with the value already decrypted.
/// Encrypted-at-rest form is handled inside the repository — callers never see ciphertext.
/// </summary>
public sealed class VaultField
{
    public long Id { get; set; }
    public long EntryId { get; set; }
    public string Label { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public bool IsSecret { get; set; }
}

public sealed class Recipe
{
    public long Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Author { get; set; }
    public string? Cuisine { get; set; }
    public int Rating { get; set; }
    public string? YoutubeUrl { get; set; }
    public string? ThumbnailPath { get; set; }
    public string? NotesMd { get; set; }
    public bool IsFavourite { get; set; }
    public bool IsTried { get; set; }
    public string Category { get; set; } = "Other";
    public DateTimeOffset AddedAt { get; set; }
}

public enum BankAccountType
{
    Savings,
    Cheque,
    TermDeposit,
    Offset,
    CreditCard,
    Mortgage,
    Other,
}

/// <summary>
/// A bank account record. Non-secret metadata only — login credentials live in
/// a linked <see cref="VaultEntry"/> (kind = "bank_login") with
/// <see cref="VaultField"/> rows for username, client id, password, and Q/A
/// pairs. Closing an account is a soft-delete via <see cref="IsClosed"/>;
/// the row remains in the same table so history (and reopen) survive.
/// </summary>
public sealed class BankAccount
{
    public long Id { get; set; }
    public string Bank { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public string? Bsb { get; set; }
    public string? IfscCode { get; set; }
    public string? AccountNumber { get; set; }
    public BankAccountType AccountType { get; set; }
    public string? HolderName { get; set; }
    public double? InterestRatePct { get; set; }
    public string? Notes { get; set; }
    public string? Website { get; set; }
    public bool IsClosed { get; set; }
    public DateTimeOffset? ClosedDate { get; set; }
    public string? CloseReason { get; set; }
    /// <summary>Legacy single-credential link. Migration 006 introduced
    /// <c>bank_account_credential</c> for N credentials per account; this
    /// column is no longer written by app code (kept readable for backups
    /// and the Toshan back-fill done in migration 006).</summary>
    public long? VaultEntryId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Per-owner internet-banking credential row. Each account can have
/// many credentials (one per family member). The encrypted bank_login.* fields
/// live in the linked <see cref="VaultEntry"/>.</summary>
public sealed class BankAccountCredential
{
    public long Id { get; set; }
    public long BankAccountId { get; set; }
    /// <summary>Free-text owner label. UI restricts to a fixed dropdown
    /// (Toshan / Devangini / Prachi / Saloni) but the column is plain TEXT
    /// to keep the schema stable if the list changes.</summary>
    public string Owner { get; set; } = string.Empty;
    public long VaultEntryId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Polymorphic attachment row — payload is AES-GCM encrypted with the
/// vault DEK, never stored in plaintext on disk. Linked to either a
/// <see cref="BankAccount"/>, <see cref="VaultEntry"/>, or
/// <see cref="Insurance"/> via (<see cref="TargetKind"/>, <see cref="TargetId"/>).
/// Cascade deletion is handled by AFTER-DELETE triggers in migration 009 + 010.</summary>
public sealed class Attachment
{
    public const string KindBankAccount = "bank_account";
    public const string KindVaultEntry  = "vault_entry";
    public const string KindInsurance   = "insurance";

    public long Id { get; set; }
    public string TargetKind { get; set; } = string.Empty;
    public long TargetId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string? MimeType { get; set; }
    public long SizeBytes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>First-class insurance policy. Encrypted credentials + notes live
/// in a linked <see cref="VaultEntry"/> of kind <c>insurance_login</c>; the
/// columns here are the searchable display fields plus the
/// <see cref="RenewalDate"/> we sort and badge by.</summary>
public sealed class Insurance
{
    public const string CredentialsEntryKind = "insurance_login";

    public long Id { get; set; }
    public string InsurerCompany { get; set; } = string.Empty;
    public string? PolicyNumber { get; set; }
    public string? InsuranceType { get; set; }
    public string? Website { get; set; }
    /// <summary>Free-text owner label (Toshan/Devu/Prachi/Saloni); see
    /// <see cref="VaultOwner"/>. Nullable so legacy rows pre-migration 011
    /// load cleanly.</summary>
    public string? Owner { get; set; }
    /// <summary>Stored as ISO-8601 yyyy-MM-dd text. Nullable — historical
    /// policies imported without a known date should still load.</summary>
    public DateOnly? RenewalDate { get; set; }
    /// <summary>Nullable until the user opens the credentials dialog at least
    /// once; the entry is created on first save and the FK back-fills.</summary>
    public long? VaultEntryId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

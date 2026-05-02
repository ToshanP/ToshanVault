using System.Data;
using System.Globalization;
using System.Reflection;
using Dapper;
using ToshanVault.Core.Models;

namespace ToshanVault.Data.Schema;

/// <summary>
/// One-time Dapper setup for the data layer:
///   * snake_case column → PascalCase property mapping for every model type
///   * Enum &lt;-&gt; TEXT handlers (Dapper otherwise round-trips enums as INTEGER)
///   * DateTimeOffset &lt;-&gt; ISO-8601 TEXT handler
/// Idempotent; safe to call from multiple repository constructors.
/// </summary>
public static class DapperSetup
{
    private static int _initialised;

    public static void EnsureInitialised()
    {
        if (Interlocked.Exchange(ref _initialised, 1) != 0) return;

        SqlMapper.AddTypeHandler(new DateTimeOffsetIsoHandler());
        SqlMapper.AddTypeHandler(new NullableDateTimeOffsetIsoHandler());
        SqlMapper.AddTypeHandler(new DateOnlyIsoHandler());
        SqlMapper.AddTypeHandler(new NullableDateOnlyIsoHandler());

        SqlMapper.AddTypeHandler(new EnumStringHandler<BudgetCategoryType>());
        SqlMapper.AddTypeHandler(new EnumStringHandler<BudgetFrequency>());
        SqlMapper.AddTypeHandler(new EnumStringHandler<RetirementKind>());
        SqlMapper.AddTypeHandler(new EnumStringHandler<BankAccountType>());
        SqlMapper.AddTypeHandler(new EnumStringHandler<RepaymentFrequency>());

        foreach (var t in new[]
        {
            typeof(BudgetCategory), typeof(BudgetItem), typeof(RetirementItem),
            typeof(RetirementPlan),
            typeof(GoldItem), typeof(GoldPriceCache),
            typeof(VaultEntry), typeof(VaultFieldRow),
            typeof(Recipe), typeof(BankAccount),
            typeof(Insurance),
        })
        {
            SqlMapper.SetTypeMap(t, new CustomPropertyTypeMap(t, ResolveSnakeCase));
        }
    }

    private static PropertyInfo ResolveSnakeCase(Type type, string columnName)
    {
        var pascal = SnakeToPascal(columnName);
        var prop = type.GetProperty(pascal, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        return prop ?? throw new InvalidOperationException(
            $"Column '{columnName}' (looking for property '{pascal}') not found on {type.Name}.");
    }

    private static string SnakeToPascal(string snake)
    {
        var parts = snake.Split('_');
        var ti = CultureInfo.InvariantCulture.TextInfo;
        for (var i = 0; i < parts.Length; i++)
            parts[i] = ti.ToTitleCase(parts[i]);
        return string.Concat(parts);
    }

    private sealed class EnumStringHandler<T> : SqlMapper.TypeHandler<T> where T : struct, Enum
    {
        public override T Parse(object value) => (T)Enum.Parse(typeof(T), (string)value, ignoreCase: false);
        public override void SetValue(IDbDataParameter parameter, T value)
        {
            parameter.DbType = DbType.String;
            parameter.Value = value.ToString();
        }
    }

    private sealed class DateTimeOffsetIsoHandler : SqlMapper.TypeHandler<DateTimeOffset>
    {
        public override DateTimeOffset Parse(object value)
            => DateTimeOffset.Parse((string)value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        public override void SetValue(IDbDataParameter parameter, DateTimeOffset value)
        {
            parameter.DbType = DbType.String;
            parameter.Value = value.ToString("O", CultureInfo.InvariantCulture);
        }
    }

    private sealed class NullableDateTimeOffsetIsoHandler : SqlMapper.TypeHandler<DateTimeOffset?>
    {
        public override DateTimeOffset? Parse(object value)
            => value is null ? null
             : DateTimeOffset.Parse((string)value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        public override void SetValue(IDbDataParameter parameter, DateTimeOffset? value)
        {
            parameter.DbType = DbType.String;
            parameter.Value = value.HasValue ? value.Value.ToString("O", CultureInfo.InvariantCulture) : DBNull.Value;
        }
    }

    // SQLite stores DateOnly as "yyyy-MM-dd" TEXT — Dapper has no built-in
    // converter from string to DateOnly, so we wire one explicitly. Used by
    // the insurance table's renewal_date column.
    private sealed class DateOnlyIsoHandler : SqlMapper.TypeHandler<DateOnly>
    {
        public override DateOnly Parse(object value)
            => DateOnly.ParseExact((string)value, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        public override void SetValue(IDbDataParameter parameter, DateOnly value)
        {
            parameter.DbType = DbType.String;
            parameter.Value = value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }
    }

    private sealed class NullableDateOnlyIsoHandler : SqlMapper.TypeHandler<DateOnly?>
    {
        public override DateOnly? Parse(object value)
            => value is null ? null
             : DateOnly.ParseExact((string)value, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        public override void SetValue(IDbDataParameter parameter, DateOnly? value)
        {
            parameter.DbType = DbType.String;
            parameter.Value = value.HasValue ? value.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : DBNull.Value;
        }
    }
}

/// <summary>
/// Persisted (encrypted) form of a vault field. Internal — callers use VaultField.
/// </summary>
internal sealed class VaultFieldRow
{
    public long Id { get; set; }
    public long EntryId { get; set; }
    public string Label { get; set; } = string.Empty;
    public byte[] ValueEnc { get; set; } = Array.Empty<byte>();
    public byte[] Iv { get; set; } = Array.Empty<byte>();
    public byte[] Tag { get; set; } = Array.Empty<byte>();
    public bool IsSecret { get; set; }
}

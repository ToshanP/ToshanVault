using ClosedXML.Excel;
using ToshanVault.Core.Models;
using ToshanVault.Data.Repositories;

namespace ToshanVault.Importer;

/// <summary>
/// Reads the legacy <c>Gold Ornaments</c> worksheet from <c>Toshan.xlsx</c> and
/// inserts rows into the <c>gold_item</c> table. The source layout is:
/// <list type="bullet">
///   <item>Column A: description (item name)</item>
///   <item>Column B: quantity (count)</item>
///   <item>Column C: tola (Indian gold weight unit, 1 tola = 11.6638038 g)</item>
/// </list>
/// Row 1 is empty styling, row 2 is the header band, row 3 is a separator,
/// data starts at row 4. The source has no purity column — we default to
/// <see cref="DefaultPurity"/> (22K, the standard for Indian jewellery). The
/// user can edit per-row purity (or set to "Diamond" / similar) afterwards.
///
/// The importer is idempotent: if an <c>item_name</c> already exists in the
/// database it is skipped, so re-importing the same xlsx does not double the
/// catalogue. Equality is case-insensitive after trimming.
/// </summary>
public sealed class GoldImporter
{
    public const string DefaultSheetName = "Gold Ornaments";
    public const string DefaultPurity = "22K";

    /// <summary>Indian troy measurement: 1 tola = 11.6638038 grams.</summary>
    public const double GramsPerTola = 11.6638038d;

    private readonly GoldItemRepository _repo;

    public GoldImporter(GoldItemRepository repo)
    {
        _repo = repo ?? throw new ArgumentNullException(nameof(repo));
    }

    public sealed record ImportReport(int RowsRead, int Inserted, int Skipped);

    public sealed record GoldRow(string Description, double Qty, double Tola);

    public static double TolaToGrams(double tola) => tola * GramsPerTola;

    /// <summary>
    /// Sanitizes the workbook (drops embedded pictures that ClosedXML cannot
    /// parse) into a temp file, then imports. The original xlsx is never
    /// modified. Throws if the named sheet is missing.
    /// </summary>
    public async Task<ImportReport> ImportAsync(
        string xlsxPath,
        string sheetName = DefaultSheetName,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(xlsxPath);
        if (!File.Exists(xlsxPath))
            throw new FileNotFoundException("Source workbook not found.", xlsxPath);

        var temp = Path.Combine(Path.GetTempPath(),
            $"toshanvault-gold-{Guid.NewGuid():N}.xlsx");
        try
        {
            XlsxSanitizer.Sanitize(xlsxPath, temp);
            using var wb = new XLWorkbook(temp);
            var ws = wb.Worksheets.FirstOrDefault(w =>
                string.Equals(w.Name, sheetName, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException(
                    $"Sheet '{sheetName}' not found. Available: {string.Join(", ", wb.Worksheets.Select(s => s.Name))}");

            var rawRows = ReadRows(ws);
            var existing = (await _repo.GetAllAsync(ct).ConfigureAwait(false))
                .Select(r => (r.ItemName ?? string.Empty).Trim().ToLowerInvariant())
                .ToHashSet();

            int inserted = 0, skipped = 0;
            foreach (var row in rawRows)
            {
                ct.ThrowIfCancellationRequested();
                var key = row.Description.Trim().ToLowerInvariant();
                if (existing.Contains(key)) { skipped++; continue; }

                await _repo.InsertAsync(new GoldItem
                {
                    ItemName  = row.Description,
                    Purity    = DefaultPurity,
                    Qty       = row.Qty,
                    Tola      = row.Tola,
                    GramsCalc = TolaToGrams(row.Tola),
                    Notes     = null,
                }, ct).ConfigureAwait(false);
                existing.Add(key);
                inserted++;
            }

            return new ImportReport(rawRows.Count, inserted, skipped);
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { /* swallow */ }
        }
    }

    /// <summary>
    /// Pure parsing — returned rows are pruned of fully-blank lines and the
    /// header band. Exposed for unit testing without standing up a workbook.
    /// </summary>
    public static IReadOnlyList<GoldRow> ReadRows(IXLWorksheet ws)
    {
        ArgumentNullException.ThrowIfNull(ws);
        var rowsUsed = ws.LastRowUsed()?.RowNumber() ?? 0;
        if (rowsUsed == 0) return Array.Empty<GoldRow>();

        return ParseRows(EnumerateCells(ws, rowsUsed));
    }

    private static IEnumerable<(string a, string b, string c)> EnumerateCells(IXLWorksheet ws, int rows)
    {
        for (int r = 1; r <= rows; r++)
        {
            yield return (
                ws.Cell(r, 1).GetString().Trim(),
                ws.Cell(r, 2).GetString().Trim(),
                ws.Cell(r, 3).GetString().Trim());
        }
    }

    /// <summary>
    /// Pure list-of-rows parser used by both the workbook reader and the unit
    /// tests. Skips blank rows; skips the header row (case-insensitive match
    /// on column B == "Qty"); requires a non-empty description; parses qty/tola
    /// as doubles and silently treats unparseable values as zero (the user can
    /// fix them in the dialog).
    /// </summary>
    public static IReadOnlyList<GoldRow> ParseRows(IEnumerable<(string a, string b, string c)> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var output = new List<GoldRow>();
        foreach (var (a, b, c) in rows)
        {
            // Drop fully-blank rows (separator).
            if (a.Length == 0 && b.Length == 0 && c.Length == 0) continue;
            // Drop header row: column B literally says "Qty".
            if (string.Equals(b, "Qty", StringComparison.OrdinalIgnoreCase)) continue;
            // Drop rows without a description.
            if (a.Length == 0) continue;

            var qty  = ParseDouble(b);
            var tola = ParseDouble(c);

            output.Add(new GoldRow(a, qty, tola));
        }
        return output;
    }

    private static double ParseDouble(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return 0;
        return double.TryParse(s, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0;
    }
}

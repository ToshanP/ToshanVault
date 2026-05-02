using ClosedXML.Excel;
using ToshanVault.Core.Models;
using ToshanVault.Data.Repositories;

namespace ToshanVault.Importer;

/// <summary>
/// Reads the legacy <c>Receipes</c> worksheet from <c>Toshan.xlsx</c> and
/// inserts rows into the <c>recipe</c> table. The source layout is:
/// <list type="bullet">
///   <item>Column A: recipe title (often blank — see <see cref="ForwardFillTitle"/>)</item>
///   <item>Column B: YouTube/external URL</item>
///   <item>Column C: channel/author (optional)</item>
/// </list>
/// Many recipes have several URL rows in succession with column A populated only
/// on the first row. We forward-fill the title so each URL becomes its own
/// recipe row, preserving the user's "show every link as a grid row" decision
/// (see decision log: 2026-05-02 Recipes import).
///
/// The importer is idempotent at the row level: if a (title, youtube_url) pair
/// already exists in the database it is skipped, so re-importing the same xlsx
/// does not double the catalogue.
/// </summary>
public sealed class RecipesImporter
{
    public const string DefaultSheetName = "Receipes";

    private readonly RecipeRepository _repo;

    public RecipesImporter(RecipeRepository repo)
    {
        _repo = repo ?? throw new ArgumentNullException(nameof(repo));
    }

    public sealed record ImportReport(int RowsRead, int Inserted, int Skipped);

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
            $"toshanvault-import-{Guid.NewGuid():N}.xlsx");
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
                .Select(r => (Title: (r.Title ?? "").Trim(),
                              Url:   (r.YoutubeUrl ?? "").Trim()))
                .ToHashSet();

            int inserted = 0, skipped = 0;
            foreach (var row in rawRows)
            {
                ct.ThrowIfCancellationRequested();
                var key = (row.Title.Trim(), row.Url.Trim());
                if (existing.Contains(key)) { skipped++; continue; }

                await _repo.InsertAsync(new Recipe
                {
                    Title       = row.Title,
                    Author      = row.Author,
                    YoutubeUrl  = row.Url,
                    AddedAt     = DateTimeOffset.UtcNow,
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
    /// Pure parsing — returned rows are already forward-filled and pruned of
    /// fully-blank lines. Exposed internal-style for unit tests.
    /// </summary>
    public static IReadOnlyList<RecipeRow> ReadRows(IXLWorksheet ws)
    {
        ArgumentNullException.ThrowIfNull(ws);
        var rowsUsed = ws.LastRowUsed()?.RowNumber() ?? 0;
        if (rowsUsed == 0) return Array.Empty<RecipeRow>();

        var output = new List<RecipeRow>();
        string? lastTitle = null;

        // Skip row 1: it's the header band ("Receipes" sheet has no real header
        // row, but row 1 contains styling-only cells — see XML probe).
        for (int r = 1; r <= rowsUsed; r++)
        {
            var a = ws.Cell(r, 1).GetString().Trim();
            var b = ws.Cell(r, 2).GetString().Trim();
            var c = ws.Cell(r, 3).GetString().Trim();

            // Forward-fill title: a non-empty A starts a new recipe; subsequent
            // blank-A rows inherit it.
            if (a.Length > 0) lastTitle = a;

            // A row is meaningful only if it has a URL. Forward-filled title +
            // empty URL is a spreadsheet separator/spacer — drop it. (A title
            // with no URL anywhere is also dropped — the user can add such
            // rows manually via the Add dialog if needed.)
            if (b.Length == 0) continue;

            var title = a.Length > 0 ? a : (lastTitle ?? string.Empty);
            if (title.Length == 0) continue;

            output.Add(new RecipeRow(
                Title:  title,
                Url:    b,
                Author: c.Length == 0 ? null : c));
        }

        return output;
    }

    public sealed record RecipeRow(string Title, string Url, string? Author);

    /// <summary>
    /// Forward-fills a sequence of (title, url, author) tuples — used by the
    /// unit tests to verify the algorithm without standing up a workbook.
    /// </summary>
    public static IReadOnlyList<RecipeRow> ForwardFillTitle(
        IEnumerable<(string? title, string? url, string? author)> input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var output = new List<RecipeRow>();
        string? lastTitle = null;
        foreach (var (t, u, a) in input)
        {
            var title = string.IsNullOrWhiteSpace(t) ? null : t.Trim();
            var url   = string.IsNullOrWhiteSpace(u) ? null : u.Trim();
            var auth  = string.IsNullOrWhiteSpace(a) ? null : a.Trim();
            if (title is not null) lastTitle = title;
            // Require a URL — that's the only field worth importing for.
            if (url is null) continue;
            var resolved = title ?? lastTitle;
            if (string.IsNullOrEmpty(resolved)) continue;
            output.Add(new RecipeRow(resolved, url, auth));
        }
        return output;
    }
}

using System.IO.Compression;
using System.Xml.Linq;

namespace ToshanVault.Importer;

/// <summary>
/// Strips drawing/picture/chart/embedding parts out of an .xlsx, then patches
/// `[Content_Types].xml`, sheet `.rels`, and sheet XML so the result is a
/// valid OOXML package that ClosedXML 0.104+ can load.
///
/// Why: ClosedXML throws <c>ArgumentException</c> on any picture whose
/// internal `name` attribute contains <c>:\/?*[]</c> — common when the source
/// xlsx was authored by Excel using a sheet name like <c>Toshan & Family</c>
/// (the colon-form date string also trips this). The importer never needs
/// images/charts/embeds, so we drop them.
/// </summary>
public static class XlsxSanitizer
{
    private static readonly XNamespace NsContentTypes =
        "http://schemas.openxmlformats.org/package/2006/content-types";
    private static readonly XNamespace NsRels =
        "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace NsSheet =
        "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace NsR =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    private static readonly string[] StripPrefixes =
    {
        "xl/drawings/", "xl/media/", "xl/charts/",
        "xl/embeddings/", "xl/diagrams/",
    };

    private const string RelTypeDrawing =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/drawing";
    private const string RelTypeVmlDrawing =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/vmlDrawing";
    private const string RelTypeChart =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart";
    private const string RelTypeImage =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image";
    private const string RelTypeOleObject =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/oleObject";

    private static readonly HashSet<string> StripRelTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        RelTypeDrawing, RelTypeVmlDrawing, RelTypeChart, RelTypeImage, RelTypeOleObject,
    };

    /// <summary>
    /// Produces a sanitized copy of <paramref name="sourceXlsxPath"/> at
    /// <paramref name="destinationXlsxPath"/>. Source is not modified.
    /// </summary>
    public static SanitizationReport Sanitize(string sourceXlsxPath, string destinationXlsxPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceXlsxPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationXlsxPath);
        if (!File.Exists(sourceXlsxPath))
            throw new FileNotFoundException("Source xlsx not found.", sourceXlsxPath);

        var dir = Path.GetDirectoryName(destinationXlsxPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.Copy(sourceXlsxPath, destinationXlsxPath, overwrite: true);

        var report = new SanitizationReport();
        using var zip = ZipFile.Open(destinationXlsxPath, ZipArchiveMode.Update);

        // 1. Delete drawing/media/chart/embedding parts (incl. their _rels).
        var toDelete = zip.Entries
            .Where(e => StripPrefixes.Any(p => e.FullName.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            .Select(e => e.FullName)
            .ToList();
        foreach (var name in toDelete)
        {
            zip.GetEntry(name)?.Delete();
            report.PartsRemoved.Add(name);
        }

        // 2. Patch [Content_Types].xml — drop Override entries pointing to deleted parts.
        var ctEntry = zip.GetEntry("[Content_Types].xml");
        if (ctEntry is not null)
        {
            var doc = ReadXml(ctEntry);
            var overrides = doc.Root?.Elements(NsContentTypes + "Override").ToList() ?? new();
            int removed = 0;
            foreach (var ov in overrides)
            {
                var partName = (string?)ov.Attribute("PartName") ?? string.Empty;
                if (StripPrefixes.Any(p => partName.TrimStart('/').StartsWith(p, StringComparison.OrdinalIgnoreCase)))
                {
                    ov.Remove();
                    removed++;
                }
            }
            if (removed > 0)
            {
                ReplaceEntry(zip, "[Content_Types].xml", doc);
                report.ContentTypesOverridesRemoved = removed;
            }
        }

        // 3. Patch sheet .rels — drop Relationship entries pointing to deleted part types.
        var sheetRelEntries = zip.Entries
            .Where(e => e.FullName.StartsWith("xl/worksheets/_rels/", StringComparison.OrdinalIgnoreCase))
            .Select(e => e.FullName)
            .ToList();
        foreach (var relPath in sheetRelEntries)
        {
            var entry = zip.GetEntry(relPath);
            if (entry is null) continue;
            var doc = ReadXml(entry);
            var rels = doc.Root?.Elements(NsRels + "Relationship").ToList() ?? new();
            int removed = 0;
            foreach (var r in rels)
            {
                var t = (string?)r.Attribute("Type");
                if (t is not null && StripRelTypes.Contains(t))
                {
                    r.Remove();
                    removed++;
                }
            }
            if (removed > 0)
            {
                ReplaceEntry(zip, relPath, doc);
                report.SheetRelsPatched++;
            }
        }

        // 4. Patch sheet XML — drop <drawing> and <legacyDrawing> elements.
        var sheetEntries = zip.Entries
            .Where(e => e.FullName.StartsWith("xl/worksheets/sheet", StringComparison.OrdinalIgnoreCase)
                        && e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            .Select(e => e.FullName)
            .ToList();
        foreach (var sheetPath in sheetEntries)
        {
            var entry = zip.GetEntry(sheetPath);
            if (entry is null) continue;
            var doc = ReadXml(entry);
            var refs = doc.Descendants()
                .Where(e => e.Name == NsSheet + "drawing"
                         || e.Name == NsSheet + "legacyDrawing"
                         || e.Name == NsSheet + "legacyDrawingHF"
                         || e.Name == NsSheet + "picture")
                .ToList();
            if (refs.Count > 0)
            {
                foreach (var r in refs) r.Remove();
                ReplaceEntry(zip, sheetPath, doc);
                report.SheetDrawingRefsRemoved += refs.Count;
            }
        }

        return report;
    }

    private static XDocument ReadXml(ZipArchiveEntry entry)
    {
        using var s = entry.Open();
        return XDocument.Load(s);
    }

    private static void ReplaceEntry(ZipArchive zip, string path, XDocument doc)
    {
        zip.GetEntry(path)?.Delete();
        var fresh = zip.CreateEntry(path, CompressionLevel.Optimal);
        using var s = fresh.Open();
        doc.Save(s);
    }
}

public sealed class SanitizationReport
{
    public List<string> PartsRemoved { get; } = new();
    public int ContentTypesOverridesRemoved { get; set; }
    public int SheetRelsPatched { get; set; }
    public int SheetDrawingRefsRemoved { get; set; }
}

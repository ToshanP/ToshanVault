using System.IO.Compression;
using ClosedXML.Excel;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ToshanVault.Importer;

namespace ToshanVault.Tests.Importer;

[TestClass]
public class XlsxSanitizerTests
{
    private static readonly string RealXlsx = @"C:\Toshan\Retirement Plan\Toshan.xlsx";

    private string _scratch = string.Empty;

    [TestInitialize]
    public void Init()
    {
        _scratch = Path.Combine(Path.GetTempPath(), "tvSanitizer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_scratch);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_scratch))
            Directory.Delete(_scratch, recursive: true);
    }

    [TestMethod]
    public void Sanitize_RealToshanXlsx_ProducesClosedXmlReadableFile()
    {
        if (!File.Exists(RealXlsx))
            Assert.Inconclusive($"{RealXlsx} not present in this checkout — skipping real-file sanity test.");

        var dst = Path.Combine(_scratch, "Toshan-clean.xlsx");
        var report = XlsxSanitizer.Sanitize(RealXlsx, dst);

        report.PartsRemoved.Should().NotBeEmpty("the source file is known to embed an image that ClosedXML rejects");
        File.Exists(dst).Should().BeTrue();

        Action open = () =>
        {
            using var wb = new XLWorkbook(dst);
            wb.Worksheets.Count().Should().BeGreaterThan(0);
        };
        open.Should().NotThrow();
    }

    [TestMethod]
    public void Sanitize_DoesNotMutateSource()
    {
        if (!File.Exists(RealXlsx))
            Assert.Inconclusive($"{RealXlsx} not present in this checkout — skipping.");

        var dst = Path.Combine(_scratch, "Toshan-clean.xlsx");
        var beforeSize = new FileInfo(RealXlsx).Length;
        var beforeMtime = File.GetLastWriteTimeUtc(RealXlsx);

        XlsxSanitizer.Sanitize(RealXlsx, dst);

        new FileInfo(RealXlsx).Length.Should().Be(beforeSize);
        File.GetLastWriteTimeUtc(RealXlsx).Should().Be(beforeMtime);
    }

    [TestMethod]
    public void Sanitize_FreshXlsxWithNoDrawings_IsNoOp()
    {
        var src = Path.Combine(_scratch, "fresh.xlsx");
        using (var wb = new XLWorkbook())
        {
            var ws = wb.Worksheets.Add("Hello");
            ws.Cell(1, 1).Value = "world";
            wb.SaveAs(src);
        }
        var dst = Path.Combine(_scratch, "fresh-clean.xlsx");

        var report = XlsxSanitizer.Sanitize(src, dst);

        report.PartsRemoved.Should().BeEmpty();
        report.SheetDrawingRefsRemoved.Should().Be(0);
        report.ContentTypesOverridesRemoved.Should().Be(0);

        using var roundtrip = new XLWorkbook(dst);
        roundtrip.Worksheet(1).Cell(1, 1).GetString().Should().Be("world");
    }

    [TestMethod]
    public void Sanitize_IsIdempotent()
    {
        if (!File.Exists(RealXlsx))
            Assert.Inconclusive($"{RealXlsx} not present in this checkout — skipping.");

        var pass1 = Path.Combine(_scratch, "pass1.xlsx");
        var pass2 = Path.Combine(_scratch, "pass2.xlsx");
        XlsxSanitizer.Sanitize(RealXlsx, pass1);
        var second = XlsxSanitizer.Sanitize(pass1, pass2);

        second.PartsRemoved.Should().BeEmpty("first pass already stripped all drawing/media parts");
        second.SheetDrawingRefsRemoved.Should().Be(0);
        second.ContentTypesOverridesRemoved.Should().Be(0);
    }

    [TestMethod]
    public void Sanitize_MissingSource_Throws()
    {
        var act = () => XlsxSanitizer.Sanitize(Path.Combine(_scratch, "nope.xlsx"),
                                               Path.Combine(_scratch, "out.xlsx"));
        act.Should().Throw<FileNotFoundException>();
    }

    [TestMethod]
    public void Sanitize_CreatesDestinationDirectory()
    {
        var src = Path.Combine(_scratch, "fresh.xlsx");
        using (var wb = new XLWorkbook()) { wb.Worksheets.Add("S"); wb.SaveAs(src); }
        var nested = Path.Combine(_scratch, "a", "b", "c", "out.xlsx");

        XlsxSanitizer.Sanitize(src, nested);

        File.Exists(nested).Should().BeTrue();
    }

    [TestMethod]
    public void Sanitize_RealFile_RemovesExpectedPartFamilies()
    {
        if (!File.Exists(RealXlsx))
            Assert.Inconclusive($"{RealXlsx} not present in this checkout — skipping.");

        var dst = Path.Combine(_scratch, "Toshan-clean.xlsx");
        XlsxSanitizer.Sanitize(RealXlsx, dst);

        using var zip = ZipFile.OpenRead(dst);
        var lingering = zip.Entries
            .Where(e => e.FullName.StartsWith("xl/drawings/", StringComparison.OrdinalIgnoreCase)
                     || e.FullName.StartsWith("xl/media/", StringComparison.OrdinalIgnoreCase)
                     || e.FullName.StartsWith("xl/charts/", StringComparison.OrdinalIgnoreCase)
                     || e.FullName.StartsWith("xl/embeddings/", StringComparison.OrdinalIgnoreCase))
            .Select(e => e.FullName)
            .ToList();
        lingering.Should().BeEmpty();
    }
}

namespace ToshanVault.Core.Models;

/// <summary>
/// Pure valuation math for gold ornaments — extracted from
/// <c>GoldPriceService</c> so it can live in a non-WinUI assembly and be
/// unit-tested without needing the full App composition root.
/// </summary>
public static class GoldValueCalculator
{
    /// <summary>1 troy ounce = 31.1034768 grams.</summary>
    public const double GramsPerTroyOunce = 31.1034768d;

    /// <summary>
    /// Converts a karat label to the gold-content fraction used for valuation.
    /// "24K" → 1.0, "22K" → 0.9167, "18K" → 0.75, "14K" → 0.5833, "10K" → 0.4167.
    /// Anything non-numeric (incl. "Diamond", null, empty) → 0.
    /// </summary>
    public static double PurityFraction(string? purity)
    {
        if (string.IsNullOrWhiteSpace(purity)) return 0;
        var p = purity.Trim().ToUpperInvariant();
        if (p.EndsWith('K')) p = p[..^1];
        if (!int.TryParse(p, out var karat)) return 0;
        if (karat <= 0 || karat > 24) return 0;
        return karat / 24.0;
    }

    /// <summary>
    /// Estimates the AUD value of <paramref name="grams"/> of gold at the
    /// given <paramref name="purity"/>. Returns 0 for non-gold purities or
    /// missing price.
    /// </summary>
    public static double EstimateValue(double grams, string? purity, double pricePerGram24k)
    {
        if (grams <= 0 || pricePerGram24k <= 0) return 0;
        return grams * PurityFraction(purity) * pricePerGram24k;
    }
}

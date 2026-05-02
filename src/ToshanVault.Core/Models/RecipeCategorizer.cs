using System.Text.RegularExpressions;

namespace ToshanVault.Core.Models;

/// <summary>
/// Coarse category used to group recipes in the grid. The user requested
/// three buckets (Chicken / Egg / Other) so they can sort by tried-status
/// and then by category.
/// </summary>
public static class RecipeCategorizer
{
    public const string Chicken = "Chicken";
    public const string Egg     = "Egg";
    public const string Other   = "Other";

    private static readonly Regex EggRegex     = new(@"\begg(s|less)?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ChickenRegex = new(@"\bchicken\b",      RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Word-boundary match avoids false positives like "eggplant" → Egg
    /// or "chickpea" → Chicken. Egg takes precedence over Chicken (a
    /// "chicken egg curry" leans Egg in the user's mental model).
    /// </summary>
    public static string Classify(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return Other;
        if (EggRegex.IsMatch(title))     return Egg;
        if (ChickenRegex.IsMatch(title)) return Chicken;
        return Other;
    }
}

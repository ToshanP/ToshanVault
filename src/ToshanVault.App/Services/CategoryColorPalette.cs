using System;
using Windows.UI;
using Microsoft.UI.Xaml.Media;

namespace ToshanVault_App.Services;

/// <summary>
/// Maps a Vault entry's <c>Category</c> string to a stable banner colour so
/// every group renders in a consistent shade across launches and machines.
///
/// <para>Stability matters: <see cref="string.GetHashCode()"/> is randomised
/// per process in .NET, so we use a fixed FNV-1a 32-bit hash instead. The
/// palette is 8 muted colours that all give acceptable WCAG AA contrast
/// against pure white text.</para>
///
/// <para>The empty/null category (rendered as "Uncategorised") is pinned to
/// a neutral grey rather than hashed, so it visually reads as a default
/// bucket rather than an actual category.</para>
/// </summary>
internal static class CategoryColorPalette
{
    private static readonly Color[] Palette =
    {
        Color.FromArgb(255, 0x3D, 0x5A, 0x80), // slate blue
        Color.FromArgb(255, 0x6A, 0x8E, 0x55), // sage green
        Color.FromArgb(255, 0xC9, 0x7D, 0x60), // terracotta
        Color.FromArgb(255, 0x7A, 0x59, 0x80), // plum
        Color.FromArgb(255, 0x4A, 0x8B, 0x8B), // teal
        Color.FromArgb(255, 0xB8, 0x74, 0x4A), // burnt orange
        Color.FromArgb(255, 0x4D, 0x4B, 0x8C), // indigo
        Color.FromArgb(255, 0x4F, 0x70, 0x48), // forest
    };

    private static readonly Color UncategorisedColor =
        Color.FromArgb(255, 0x5A, 0x5A, 0x5A);

    /// <summary>
    /// Returns the banner background brush for a given category name.
    /// Null/empty/whitespace category returns the neutral grey; everything
    /// else is hashed (case-insensitive) into the palette so renaming case
    /// (Banking vs BANKING) doesn't change the colour.
    /// </summary>
    public static SolidColorBrush BrushFor(string? category)
    {
        if (string.IsNullOrWhiteSpace(category))
            return new SolidColorBrush(UncategorisedColor);

        var normalized = category.Trim().ToLowerInvariant();
        var idx = (int)(StableHash(normalized) % (uint)Palette.Length);
        return new SolidColorBrush(Palette[idx]);
    }

    private static uint StableHash(string s)
    {
        // FNV-1a 32-bit. Constants per the canonical algorithm. Operates on
        // UTF-16 code units which is fine - we just need stable bytes in,
        // stable bytes out across processes.
        const uint offset = 2166136261u;
        const uint prime  = 16777619u;
        uint h = offset;
        for (var i = 0; i < s.Length; i++)
        {
            h ^= s[i];
            h *= prime;
        }
        return h;
    }
}

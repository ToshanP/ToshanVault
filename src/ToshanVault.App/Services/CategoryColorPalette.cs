using System;
using Windows.UI;
using Microsoft.UI.Xaml.Media;

namespace ToshanVault_App.Services;

/// <summary>
/// Maps a Vault group's display position to a distinct banner colour. Assigns
/// by alphabetical index rather than by hash so every visible category gets a
/// unique colour as long as there are fewer categories than palette slots
/// (currently 16). Hash-based assignment can't guarantee uniqueness — with N
/// categories and M slots, collisions are inevitable when N > M and likely
/// even when N ≤ M.
///
/// <para>The empty/null category (rendered as "Uncategorised") is pinned to a
/// neutral grey so it visually reads as a default bucket rather than a
/// real category.</para>
///
/// <para>Trade-off: adding a new category that sorts before an existing one
/// will shift the existing one's colour by one slot. This is a one-time
/// visual reflow on the next page load, not a per-launch surprise.</para>
/// </summary>
internal static class CategoryColorPalette
{
    // 16 muted colours, all giving acceptable WCAG AA contrast against pure
    // white text. Hand-picked to be visually distinct in pairs (no two
    // adjacent slots are confusable at a glance) so even partial overlaps in
    // the visible list look intentional.
    private static readonly Color[] Palette =
    {
        Color.FromArgb(255, 0x3D, 0x5A, 0x80), // slate blue
        Color.FromArgb(255, 0xC9, 0x7D, 0x60), // terracotta
        Color.FromArgb(255, 0x6A, 0x8E, 0x55), // sage green
        Color.FromArgb(255, 0x7A, 0x59, 0x80), // plum
        Color.FromArgb(255, 0xB8, 0x74, 0x4A), // burnt orange
        Color.FromArgb(255, 0x4A, 0x8B, 0x8B), // teal
        Color.FromArgb(255, 0x4D, 0x4B, 0x8C), // indigo
        Color.FromArgb(255, 0xA8, 0x55, 0x6B), // dusty rose
        Color.FromArgb(255, 0x4F, 0x70, 0x48), // forest
        Color.FromArgb(255, 0x9E, 0x6B, 0x3F), // amber bronze
        Color.FromArgb(255, 0x55, 0x6B, 0x9E), // periwinkle
        Color.FromArgb(255, 0x8C, 0x4F, 0x4F), // brick
        Color.FromArgb(255, 0x47, 0x82, 0x6B), // pine
        Color.FromArgb(255, 0x6E, 0x4A, 0x8E), // royal purple
        Color.FromArgb(255, 0xC0, 0x8A, 0x4E), // ochre
        Color.FromArgb(255, 0x52, 0x6F, 0x82), // steel blue
    };

    private static readonly Color UncategorisedColor =
        Color.FromArgb(255, 0x5A, 0x5A, 0x5A);

    public static int PaletteSize => Palette.Length;

    /// <summary>
    /// Returns the banner background brush for the group at <paramref name="index"/>
    /// in the page's display order. <paramref name="isUncategorised"/> overrides
    /// the index-based pick with the neutral grey so the catch-all bucket reads
    /// distinctly from the user-named categories.
    /// </summary>
    public static SolidColorBrush BrushForIndex(int index, bool isUncategorised)
    {
        if (isUncategorised) return new SolidColorBrush(UncategorisedColor);
        if (index < 0) index = 0;
        return new SolidColorBrush(Palette[index % Palette.Length]);
    }
}

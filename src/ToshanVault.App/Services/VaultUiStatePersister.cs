using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Serilog;
using ToshanVault_App.Hosting;

namespace ToshanVault_App.Services;

/// <summary>
/// Persists per-page, non-sensitive Vault UI state (currently: which group
/// banners are collapsed) to a tiny JSON file alongside <c>vault.db</c>.
///
/// <para>Lives outside the encrypted SQLite database on purpose - this is
/// purely cosmetic state that doesn't warrant the cost of an open vault to
/// read or the schema cost of another table. Read/write are silent-fail:
/// losing UI state is never worth crashing the app.</para>
/// </summary>
internal sealed class VaultUiStatePersister
{
    private const string FileName = "vault-ui-state.json";

    private sealed class State
    {
        // Stored lower-case so renaming case doesn't lose collapsed state.
        // List rather than HashSet because System.Text.Json can't round-trip
        // a HashSet without a converter and we don't need O(1) lookup at
        // load time - we hydrate into an in-memory HashSet immediately.
        public List<string> CollapsedGroups { get; set; } = new();
    }

    private string PathOnDisk => Path.Combine(AppPaths.DataDirectory, FileName);

    public HashSet<string> LoadCollapsedGroups()
    {
        try
        {
            if (!File.Exists(PathOnDisk)) return new(StringComparer.OrdinalIgnoreCase);
            using var stream = File.OpenRead(PathOnDisk);
            var state = JsonSerializer.Deserialize<State>(stream);
            return new HashSet<string>(state?.CollapsedGroups ?? new(), StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load Vault UI state from {Path}", PathOnDisk);
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public void SaveCollapsedGroups(IEnumerable<string> collapsed)
    {
        try
        {
            AppPaths.EnsureDataDirectory();
            var state = new State
            {
                CollapsedGroups = collapsed
                    .Where(c => !string.IsNullOrWhiteSpace(c))
                    .Select(c => c.Trim().ToLowerInvariant())
                    .Distinct(StringComparer.Ordinal)
                    .ToList(),
            };
            var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
            // WriteAllText is atomic enough for this purpose - the file is
            // single-writer (only the main UI thread touches it) and a
            // mid-write crash just resets to "all expanded" on next launch.
            File.WriteAllText(PathOnDisk, json);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to save Vault UI state to {Path}", PathOnDisk);
        }
    }
}

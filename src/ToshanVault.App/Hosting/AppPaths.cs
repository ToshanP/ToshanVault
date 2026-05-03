using System;
using System.IO;
using System.Text.Json;

namespace ToshanVault_App.Hosting;

/// <summary>
/// Resolves on-disk paths used by the app.
/// <para>Lookup order for the database file (first match wins):</para>
/// <list type="number">
/// <item>The <c>TOSHANVAULT_DATA_DIR</c> environment variable, when set, is
///       treated as the data <em>directory</em> and <c>vault.db</c> is appended.
///       Used by tests and ad-hoc overrides.</item>
/// <item><c>Storage.DatabaseFilePath</c> in <c>appsettings.json</c> next to the
///       exe (full absolute path including filename). Null or empty falls
///       through.</item>
/// <item>The default <c>%LOCALAPPDATA%\ToshanVault\vault.db</c>.</item>
/// </list>
/// </summary>
public static class AppPaths
{
    public const string EnvOverride = "TOSHANVAULT_DATA_DIR";

    private static readonly Lazy<string?> SettingsDbPath = new(LoadSettingsDbPath);

    /// <summary>
    /// Folder that holds <c>vault.db</c> and any future side files (backups, logs).
    /// Defaults to <c>%LOCALAPPDATA%\ToshanVault</c>.
    /// </summary>
    public static string DataDirectory => Path.GetDirectoryName(DatabasePath)
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ToshanVault");

    public static string DatabasePath
    {
        get
        {
            var env = Environment.GetEnvironmentVariable(EnvOverride);
            if (!string.IsNullOrWhiteSpace(env))
                return Path.Combine(env, "vault.db");

            var fromSettings = SettingsDbPath.Value;
            if (!string.IsNullOrWhiteSpace(fromSettings))
                return fromSettings!;

            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(local, "ToshanVault", "vault.db");
        }
    }

    public static void EnsureDataDirectory() => Directory.CreateDirectory(DataDirectory);

    public static string BuildConnectionString(string? overridePath = null)
    {
        var path = overridePath ?? DatabasePath;
        return $"Data Source={path};Mode=ReadWriteCreate;Cache=Private;Pooling=False";
    }

    /// <summary>
    /// Reads <c>appsettings.json</c> next to the running exe and returns
    /// <c>Storage.DatabaseFilePath</c> if non-empty, otherwise null. Any
    /// failure (missing file, bad JSON, IO error) returns null silently —
    /// the caller falls back to <c>%LOCALAPPDATA%</c> so the app still
    /// starts on a fresh machine without the file.
    /// </summary>
    private static string? LoadSettingsDbPath()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
            if (!File.Exists(path)) return null;
            using var stream = File.OpenRead(path);
            using var doc = JsonDocument.Parse(stream, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            if (!doc.RootElement.TryGetProperty("Storage", out var storage)) return null;
            if (!storage.TryGetProperty("DatabaseFilePath", out var prop)) return null;
            if (prop.ValueKind != JsonValueKind.String) return null;
            var value = prop.GetString();
            if (string.IsNullOrWhiteSpace(value)) return null;
            // Reject relative / non-rooted paths — they break EnsureDataDirectory()
            // because Path.GetDirectoryName("vault.db") returns "" and
            // Directory.CreateDirectory("") throws. Fall back to the default
            // when the user supplies anything that isn't a fully-qualified path.
            if (!Path.IsPathFullyQualified(value)) return null;
            return value;
        }
        catch
        {
            return null;
        }
    }
}

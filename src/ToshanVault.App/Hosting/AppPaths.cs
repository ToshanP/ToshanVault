using System;
using System.Collections.Generic;
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
            // In a single-file self-extracting publish, AppContext.BaseDirectory
            // points to the temp extraction folder (e.g. %TEMP%\.net\<app>\<hash>),
            // NOT the directory containing the exe the user actually launched.
            // Probe the real exe location first via the process main module so a
            // user-edited appsettings.json placed next to the exe is honoured;
            // fall back to AppContext.BaseDirectory for non-single-file builds
            // and unit tests where MainModule may not resolve.
            var probes = new List<string>(2);
            try
            {
                var exe = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrWhiteSpace(exe))
                {
                    var dir = Path.GetDirectoryName(exe);
                    if (!string.IsNullOrWhiteSpace(dir))
                        probes.Add(Path.Combine(dir, "appsettings.json"));
                }
            }
            catch { /* MainModule can throw under some hosts; ignore */ }
            probes.Add(Path.Combine(AppContext.BaseDirectory, "appsettings.json"));

            foreach (var path in probes)
            {
                if (!File.Exists(path)) continue;
                using var stream = File.OpenRead(path);
                using var doc = JsonDocument.Parse(stream, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
                if (!doc.RootElement.TryGetProperty("Storage", out var storage)) continue;
                if (!storage.TryGetProperty("DatabaseFilePath", out var prop)) continue;
                if (prop.ValueKind != JsonValueKind.String) continue;
                var value = prop.GetString();
                if (string.IsNullOrWhiteSpace(value)) continue;
                if (!Path.IsPathFullyQualified(value)) continue;
                return value;
            }
            return null;
        }
        catch
        {
            return null;
        }
    }
}

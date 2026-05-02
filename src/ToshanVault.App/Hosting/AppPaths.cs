using System;
using System.IO;

namespace ToshanVault_App.Hosting;

/// <summary>
/// Resolves on-disk paths used by the app. Override the data root via the
/// <c>TOSHANVAULT_DATA_DIR</c> environment variable (used by tests).
/// </summary>
public static class AppPaths
{
    public const string EnvOverride = "TOSHANVAULT_DATA_DIR";

    /// <summary>
    /// Folder that holds <c>vault.db</c> and any future side files (backups, logs).
    /// Defaults to <c>%LOCALAPPDATA%\ToshanVault</c>.
    /// </summary>
    public static string DataDirectory
    {
        get
        {
            var env = Environment.GetEnvironmentVariable(EnvOverride);
            if (!string.IsNullOrWhiteSpace(env)) return env;

            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(local, "ToshanVault");
        }
    }

    public static string DatabasePath => Path.Combine(DataDirectory, "vault.db");

    public static void EnsureDataDirectory() => Directory.CreateDirectory(DataDirectory);

    public static string BuildConnectionString(string? overridePath = null)
    {
        var path = overridePath ?? DatabasePath;
        return $"Data Source={path};Mode=ReadWriteCreate;Cache=Private;Pooling=False";
    }
}

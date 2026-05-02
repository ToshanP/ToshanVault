using System;
using System.IO;
using Serilog;
using Serilog.Events;

namespace ToshanVault_App.Hosting;

/// <summary>
/// Centralised Serilog bootstrap. Configures a rolling-file sink under
/// <c>%LOCALAPPDATA%\ToshanVault\logs\toshanvault-YYYYMMDD.log</c> plus a
/// debug sink so messages appear in the VS Output window during development.
///
/// Use the static <see cref="Serilog.Log"/> facade or call
/// <see cref="ForContext{T}"/> from any class. Idempotent — calling
/// <see cref="Initialise"/> twice is safe.
/// </summary>
public static class Logging
{
    private static bool _initialised;
    private static string? _logFilePath;

    /// <summary>Active log directory (created on first call).</summary>
    public static string LogDirectory { get; private set; } = string.Empty;

    /// <summary>Most recent log file path (computed; rolling daily).</summary>
    public static string LogFilePath => _logFilePath ?? string.Empty;

    public static void Initialise()
    {
        if (_initialised) return;

        LogDirectory = Path.Combine(AppPaths.DataDirectory, "logs");
        Directory.CreateDirectory(LogDirectory);
        _logFilePath = Path.Combine(LogDirectory, "toshanvault-.log");

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("App", "ToshanVault")
            .WriteTo.File(
                path: _logFilePath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                shared: true,
                outputTemplate:
                    "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}")
            .WriteTo.Debug(
                outputTemplate:
                    "[{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        _initialised = true;

        Log.Information("Serilog initialised. Log file: {LogFile}", _logFilePath);
    }

    public static Serilog.ILogger ForContext<T>() => Log.ForContext<T>();

    public static void Shutdown()
    {
        if (!_initialised) return;
        Log.CloseAndFlush();
        _initialised = false;
    }
}

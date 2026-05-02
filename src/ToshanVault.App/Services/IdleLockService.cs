using System;
using System.Runtime.InteropServices;

namespace ToshanVault_App.Services;

/// <summary>
/// Watches OS idle time and raises <see cref="IdleThresholdReached"/> when
/// the user has been idle for at least <see cref="IdleThreshold"/>.
///
/// The Win32 probe is injectable via the constructor so unit tests can drive
/// the threshold logic without needing a real desktop session.
/// </summary>
public sealed class IdleLockService
{
    public static readonly TimeSpan DefaultIdleThreshold = TimeSpan.FromMinutes(10);

    private readonly Func<TimeSpan> _idleProbe;
    private bool _alreadyFired;

    public IdleLockService() : this(Win32IdleProbe.GetIdleTime) { }

    /// <summary>Test-only constructor.</summary>
    public IdleLockService(Func<TimeSpan> idleProbe)
    {
        _idleProbe = idleProbe ?? throw new ArgumentNullException(nameof(idleProbe));
        IdleThreshold = DefaultIdleThreshold;
    }

    public TimeSpan IdleThreshold { get; set; }

    public event EventHandler? IdleThresholdReached;

    /// <summary>
    /// Reads current idle time and, if it has reached the configured
    /// threshold, raises the event exactly once per <see cref="Reset"/>.
    /// Designed to be called from a UI dispatcher timer at ~1 Hz.
    /// </summary>
    public void Tick()
    {
        if (_alreadyFired) return;
        if (_idleProbe() < IdleThreshold) return;

        _alreadyFired = true;
        IdleThresholdReached?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Re-arms the service after the vault is unlocked again.
    /// </summary>
    public void Reset() => _alreadyFired = false;
}

internal static class Win32IdleProbe
{
    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    public static TimeSpan GetIdleTime()
    {
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (!GetLastInputInfo(ref info)) return TimeSpan.Zero;

        var nowTicks = (uint)Environment.TickCount;
        var idleMs = unchecked(nowTicks - info.dwTime);
        return TimeSpan.FromMilliseconds(idleMs);
    }
}

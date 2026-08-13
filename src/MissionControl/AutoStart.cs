using System;
using Microsoft.Win32;

namespace MissionControl;

/// <summary>
/// "Start with Windows" via the per-user Run key. HKCU needs no elevation, which matters
/// because the app installs per-user and its users may not have local admin rights.
/// </summary>
public static class AutoStart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "MissionControl";

    public static string ExecutablePath => Environment.ProcessPath ?? string.Empty;

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string s && s.Length > 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Writes or removes the Run entry. Returns false if the registry rejected the change.</summary>
    public static bool SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            if (key == null) return false;

            if (enabled)
            {
                string path = ExecutablePath;
                if (string.IsNullOrEmpty(path)) return false;
                key.SetValue(ValueName, $"\"{path}\"", RegistryValueKind.String);
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Reconciles the saved preference with the Run key at startup. The registry wins: it is
    /// what Windows will actually act on, and anything else that changed it — the installer's
    /// "start when I sign in" checkbox, Task Manager, another tool — deserves to be respected
    /// rather than silently reverted. The settings file just mirrors it. A stale path (the app
    /// was reinstalled elsewhere) is rewritten in place.
    /// </summary>
    /// <returns>True if the saved preference had to be corrected, so the caller can persist it.</returns>
    public static bool Reconcile(Settings settings)
    {
        bool inRegistry = IsEnabled();

        if (settings.StartWithWindows != inRegistry)
        {
            settings.StartWithWindows = inRegistry;
            return true;
        }

        if (inRegistry && !PathMatches())
            SetEnabled(true);
        return false;
    }

    private static bool PathMatches()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            if (key?.GetValue(ValueName) is not string current) return false;
            return string.Equals(current.Trim('"'), ExecutablePath, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }
}

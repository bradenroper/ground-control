using System;
using System.Collections.Generic;
using System.Windows.Input;
using GroundControl.Native;

namespace GroundControl;

/// <summary>
/// A modifier + key combination, in the form <c>RegisterHotKey</c> wants (Win32 modifier
/// flags + virtual-key code), with a round-trippable text form like "Ctrl+Alt+M" for the
/// settings file and the UI.
/// </summary>
public readonly struct HotKeySpec : IEquatable<HotKeySpec>
{
    public uint Modifiers { get; }
    public uint VirtualKey { get; }

    public HotKeySpec(uint modifiers, uint virtualKey)
    {
        Modifiers = modifiers;
        VirtualKey = virtualKey;
    }

    public bool IsValid => Modifiers != 0 && VirtualKey != 0;

    /// <summary>Builds a spec from a WPF key press, ignoring presses that are modifiers alone.</summary>
    public static HotKeySpec? FromKeyPress(Key key, ModifierKeys modifiers)
    {
        // Alt arrives as Key.System with the real key in SystemKey; the caller resolves that.
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
                or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin
                or Key.System or Key.None or Key.ImeProcessed)
            return null;

        uint mods = 0;
        if (modifiers.HasFlag(ModifierKeys.Control)) mods |= NativeMethods.MOD_CONTROL;
        if (modifiers.HasFlag(ModifierKeys.Alt)) mods |= NativeMethods.MOD_ALT;
        if (modifiers.HasFlag(ModifierKeys.Shift)) mods |= NativeMethods.MOD_SHIFT;
        if (modifiers.HasFlag(ModifierKeys.Windows)) mods |= NativeMethods.MOD_WIN;
        if (mods == 0) return null;    // a bare key would swallow that key system-wide

        uint vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        if (vk == 0) return null;

        return new HotKeySpec(mods, vk);
    }

    // ---------------------------------------------------------------- text form
    public static HotKeySpec Parse(string text) =>
        TryParse(text, out var spec) ? spec : throw new FormatException($"Not a hotkey: '{text}'");

    public static bool TryParse(string? text, out HotKeySpec spec)
    {
        spec = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        uint mods = 0;
        uint vk = 0;

        foreach (string raw in text.Split('+', StringSplitOptions.RemoveEmptyEntries))
        {
            string part = raw.Trim();
            switch (part.ToLowerInvariant())
            {
                case "ctrl":
                case "control": mods |= NativeMethods.MOD_CONTROL; continue;
                case "alt": mods |= NativeMethods.MOD_ALT; continue;
                case "shift": mods |= NativeMethods.MOD_SHIFT; continue;
                case "win":
                case "windows": mods |= NativeMethods.MOD_WIN; continue;
            }

            if (vk != 0) return false;                 // more than one non-modifier key
            if (!TryParseKey(part, out vk)) return false;
        }

        if (mods == 0 || vk == 0) return false;
        spec = new HotKeySpec(mods, vk);
        return true;
    }

    private static bool TryParseKey(string part, out uint vk)
    {
        vk = 0;

        // Digits are Key.D0..Key.D9; accept the bare digit as written in the settings file.
        if (part.Length == 1 && part[0] >= '0' && part[0] <= '9')
            part = "D" + part;

        if (!Enum.TryParse<Key>(part, ignoreCase: true, out var key)) return false;
        int code = KeyInterop.VirtualKeyFromKey(key);
        if (code == 0) return false;

        vk = (uint)code;
        return true;
    }

    public override string ToString()
    {
        var parts = new List<string>(4);
        if ((Modifiers & NativeMethods.MOD_CONTROL) != 0) parts.Add("Ctrl");
        if ((Modifiers & NativeMethods.MOD_ALT) != 0) parts.Add("Alt");
        if ((Modifiers & NativeMethods.MOD_SHIFT) != 0) parts.Add("Shift");
        if ((Modifiers & NativeMethods.MOD_WIN) != 0) parts.Add("Win");
        parts.Add(KeyName());
        return string.Join("+", parts);
    }

    private string KeyName()
    {
        var key = KeyInterop.KeyFromVirtualKey((int)VirtualKey);
        string name = key.ToString();
        // "D5" reads better as "5"; everything else keeps its Key enum name (F1, Space, Tab...).
        if (name.Length == 2 && name[0] == 'D' && char.IsDigit(name[1])) return name[1].ToString();
        return name;
    }

    public bool Equals(HotKeySpec other) => Modifiers == other.Modifiers && VirtualKey == other.VirtualKey;
    public override bool Equals(object? obj) => obj is HotKeySpec other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Modifiers, VirtualKey);
    public static bool operator ==(HotKeySpec a, HotKeySpec b) => a.Equals(b);
    public static bool operator !=(HotKeySpec a, HotKeySpec b) => !a.Equals(b);
}

using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Interop;
using MissionControl.Native;

namespace MissionControl;

/// <summary>
/// Registers process-wide global hotkeys. Uses a hidden message window so hotkeys
/// fire regardless of which application currently has focus.
/// </summary>
public sealed class HotKeyManager : IDisposable
{
    private readonly Window _window;
    private readonly IntPtr _handle;
    private readonly HwndSource _source;
    private readonly Dictionary<int, Action> _actions = new();
    private int _nextId = 1;

    public IntPtr Handle => _handle;

    public HotKeyManager()
    {
        _window = new Window
        {
            Width = 0,
            Height = 0,
            Left = -10000,
            Top = -10000,
            WindowStyle = WindowStyle.None,
            ShowInTaskbar = false,
            ShowActivated = false,
            Visibility = Visibility.Hidden
        };

        // Create the HWND without ever showing the window.
        _handle = new WindowInteropHelper(_window).EnsureHandle();
        _source = HwndSource.FromHwnd(_handle)!;
        _source.AddHook(WndProc);
    }

    public bool Register(uint modifiers, uint virtualKey, Action action)
    {
        int id = _nextId++;
        if (!NativeMethods.RegisterHotKey(_handle, id, modifiers | NativeMethods.MOD_NOREPEAT, virtualKey))
            return false;
        _actions[id] = action;
        return true;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY && _actions.TryGetValue(wParam.ToInt32(), out var action))
        {
            action();
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        foreach (int id in _actions.Keys)
            NativeMethods.UnregisterHotKey(_handle, id);
        _actions.Clear();
        _source.RemoveHook(WndProc);
        _window.Close();
    }
}

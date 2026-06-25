using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using System.Windows.Threading;
using MissionControl.Layout;
using MissionControl.Native;

namespace MissionControl;

/// <summary>
/// Owns one overlay window per monitor and a single global selection. Handles navigation
/// (spatial, across monitors), confirm/cancel, the coordinated reverse animation, and
/// focusing the chosen window once every monitor's outro has finished.
/// </summary>
public sealed class OverlayController
{
    private readonly IntPtr _excludeHwnd;
    private readonly List<OverlayWindow> _overlays = new();
    private readonly List<OverlayItem> _all = new();

    private int _selected = -1;
    private bool _closing;
    private int _outroPending;
    private IntPtr _focusTarget;

    public event Action? Closed;

    public OverlayController(IntPtr excludeHwnd) => _excludeHwnd = excludeHwnd;

    /// <summary>Builds and shows the overlays. Returns false (and does nothing) if there are no windows.</summary>
    public bool Show()
    {
        uint myPid = (uint)Environment.ProcessId;

        var windows = WindowEnumerator.GetAltTabWindows(_excludeHwnd)
            .Where(w =>
            {
                NativeMethods.GetWindowThreadProcessId(w.Handle, out uint pid);
                return pid != myPid;
            })
            .ToList();
        if (windows.Count == 0) return false;

        // Assign each window to the monitor it mostly lives on.
        var byMonitor = new Dictionary<IntPtr, List<WindowInfo>>();
        foreach (var w in windows)
        {
            IntPtr hm = NativeMethods.MonitorFromWindow(w.Handle, NativeMethods.MONITOR_DEFAULTTONEAREST);
            if (!byMonitor.TryGetValue(hm, out var list))
                byMonitor[hm] = list = new List<WindowInfo>();
            list.Add(w);
        }

        IntPtr foreground = NativeMethods.GetForegroundWindow();

        foreach (var monitor in MonitorEnumerator.GetMonitors())
        {
            byMonitor.TryGetValue(monitor.Handle, out var monitorWindows);
            var overlay = new OverlayWindow(this, monitor, monitorWindows ?? new List<WindowInfo>());
            overlay.Prepare();                  // compute layout (no HWND needed yet)
            _overlays.Add(overlay);
            _all.AddRange(overlay.Items);
        }
        if (_all.Count == 0) return false;

        // Start on whatever window was focused, else the first one.
        _selected = _all.FindIndex(it => it.Window.Handle == foreground);
        if (_selected < 0) _selected = 0;

        foreach (var overlay in _overlays)
            overlay.ShowOverlay();

        ApplySelection(snap: true);

        // Give keyboard focus to the monitor that owns the initial selection.
        var owner = _all[_selected].Owner;
        owner.Activate();
        owner.Focus();
        return true;
    }

    // ---------------------------------------------------------------- input from overlays
    public void HandleKey(Key key)
    {
        switch (key)
        {
            case Key.Escape: Cancel(); break;
            case Key.Enter: Confirm(); break;
            case Key.Left: Navigate(-1, 0); break;
            case Key.Right: Navigate(1, 0); break;
            case Key.Up: Navigate(0, -1); break;
            case Key.Down: Navigate(0, 1); break;
            case Key.Tab: NavigateNext(); break;
        }
    }

    public void ConfirmItem(OverlayItem item)
    {
        int idx = _all.IndexOf(item);
        if (idx >= 0) _selected = idx;
        Confirm();
    }

    public void CancelFromClick() => Cancel();

    /// <summary>Re-press of the global hotkey while open = focus the highlighted window.</summary>
    public void ConfirmSelection() => Confirm();

    public void NotifyDeactivated()
    {
        if (_closing) return;
        // Defer: focus may simply be moving between our own overlays. Only bail if it left them all.
        Dispatcher.CurrentDispatcher.BeginInvoke(() =>
        {
            if (_closing) return;
            if (!_overlays.Any(o => o.IsActive)) Cancel();
        }, DispatcherPriority.Background);
    }

    // ---------------------------------------------------------------- navigation
    private void Navigate(int dx, int dy)
    {
        if (_selected < 0) return;
        var cur = _all[_selected].GlobalTarget;
        double cx = cur.CenterX, cy = cur.CenterY;

        int best = -1;
        double bestScore = double.MaxValue;
        for (int k = 0; k < _all.Count; k++)
        {
            if (k == _selected) continue;
            var g = _all[k].GlobalTarget;
            double vx = g.CenterX - cx, vy = g.CenterY - cy;

            // Distance along the travel axis (must be positive) and perpendicular offset.
            double along = dx != 0 ? vx * dx : vy * dy;
            double perp = dx != 0 ? Math.Abs(vy) : Math.Abs(vx);
            if (along <= 1) continue;

            double score = along + 2 * perp;   // prefer near & well-aligned
            if (score < bestScore) { bestScore = score; best = k; }
        }

        if (best >= 0)
        {
            _selected = best;
            ApplySelection(snap: false);
        }
    }

    private void NavigateNext()
    {
        if (_all.Count == 0) return;
        _selected = (_selected + 1) % _all.Count;
        ApplySelection(snap: false);
    }

    private void ApplySelection(bool snap)
    {
        var sel = _selected >= 0 ? _all[_selected] : null;
        foreach (var overlay in _overlays)
            overlay.SetSelected(sel, snap);
    }

    // ---------------------------------------------------------------- exit
    private void Confirm()
    {
        if (_closing) return;
        _focusTarget = _selected >= 0 ? _all[_selected].Window.Handle : IntPtr.Zero;
        BeginClose();
    }

    private void Cancel()
    {
        if (_closing) return;
        _focusTarget = IntPtr.Zero;
        BeginClose();
    }

    private void BeginClose()
    {
        _closing = true;

        // Lift the window we're about to focus to the top of its overlay's thumbnail stack, so it
        // rises above the others as everything morphs back — matching the real focus that follows.
        if (_focusTarget != IntPtr.Zero)
        {
            var item = _all.FirstOrDefault(it => it.Window.Handle == _focusTarget);
            item?.Owner.RaiseToTop(item);
        }

        _outroPending = _overlays.Count;
        if (_outroPending == 0) { FinalizeClose(); return; }
        foreach (var overlay in _overlays)
            overlay.BeginOutro();          // each calls OnOutroComplete when done
    }

    public void OnOutroComplete()
    {
        if (--_outroPending <= 0) FinalizeClose();
    }

    private void FinalizeClose()
    {
        // Focus the chosen window *before* tearing down the overlays. The overlays are topmost and
        // still covering the desktop, so raising the real window behind them is invisible — but it
        // means the real z-order already matches the thumbnails by the time they disappear. Doing it
        // the other way round flashes the old desktop arrangement for a frame as the preview vanishes.
        if (_focusTarget != IntPtr.Zero) FocusWindow(_focusTarget);

        foreach (var overlay in _overlays) overlay.CloseOverlay();
        _overlays.Clear();

        foreach (var item in _all) item.Thumb?.Dispose();
        _all.Clear();

        Closed?.Invoke();
    }

    private static void FocusWindow(IntPtr hwnd)
    {
        if (NativeMethods.IsIconic(hwnd))
            NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);

        IntPtr fg = NativeMethods.GetForegroundWindow();
        uint fgThread = NativeMethods.GetWindowThreadProcessId(fg, out _);
        uint targetThread = NativeMethods.GetWindowThreadProcessId(hwnd, out _);
        uint thisThread = NativeMethods.GetCurrentThreadId();

        NativeMethods.AttachThreadInput(thisThread, targetThread, true);
        NativeMethods.AttachThreadInput(thisThread, fgThread, true);

        NativeMethods.BringWindowToTop(hwnd);
        NativeMethods.SetForegroundWindow(hwnd);

        NativeMethods.AttachThreadInput(thisThread, fgThread, false);
        NativeMethods.AttachThreadInput(thisThread, targetThread, false);
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using MissionControl.Layout;
using MissionControl.Native;

namespace MissionControl;

public partial class OverlayWindow : Window
{
    private sealed class Item
    {
        public required WindowInfo Window;
        public required DwmThumbnail Thumb;
        public RectD Target;   // final thumbnail rect (physical px)
        public RectD Current;  // animated rect (physical px)
        public int Row, Col;
    }

    private const double Gap = 28;     // px between cells, and the outer margin
    private const double Inset = 12;   // px the thumbnail is inset within its cell
    private const double Tau = 0.055;  // smoothing time constant (s); smaller = snappier

    private readonly IntPtr _excludeHwnd;
    private readonly List<Item> _items = new();
    private readonly Stopwatch _clock = new();

    private IntPtr _hwnd;
    private double _dpi = 1.0;
    private int _selected = -1;
    private int _cols = 1;
    private double _lastT;

    private RectD _highlightCurrent;
    private RectD _highlightTarget;
    private bool _highlightInit;

    private bool _ready;
    private bool _closing;

    public OverlayWindow(IntPtr excludeHwnd)
    {
        _excludeHwnd = excludeHwnd;
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    public void ShowAndActivate()
    {
        Show();
        Activate();
        Focus();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;

        // Cover the whole primary screen, sized in physical pixels so the DWM thumbnail
        // coordinate space (also physical) lines up exactly with the window client area.
        int w = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXSCREEN);
        int h = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYSCREEN);
        NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_TOPMOST, 0, 0, w, h, NativeMethods.SWP_SHOWWINDOW);

        _dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        BuildItems();
        if (_items.Count == 0)
        {
            Close();
            return;
        }

        ComputeLayout();
        _selected = 0;
        SetHighlightTarget(initialize: true);

        _clock.Start();
        _lastT = 0;
        CompositionTarget.Rendering += OnRender;

        Activate();
        Focus();
        _ready = true;
    }

    // ---------------------------------------------------------------- setup
    private void BuildItems()
    {
        uint myPid = (uint)Environment.ProcessId;

        foreach (var win in WindowEnumerator.GetAltTabWindows(_hwnd, _excludeHwnd))
        {
            // Skip any window belonging to this process (the overlay, the hotkey window).
            NativeMethods.GetWindowThreadProcessId(win.Handle, out uint pid);
            if (pid == myPid) continue;

            var thumb = DwmThumbnail.Register(_hwnd, win.Handle);
            if (thumb == null) continue;

            _items.Add(new Item { Window = win, Thumb = thumb });
        }
    }

    private void ComputeLayout()
    {
        double regionW = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXSCREEN);
        double regionH = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYSCREEN);

        double[] aspects = _items.Select(AspectOf).ToArray();
        var (cells, cols) = GridLayout.Compute(regionW, regionH, aspects, Gap);
        _cols = cols;

        for (int i = 0; i < _items.Count; i++)
        {
            var it = _items[i];
            it.Row = i / cols;
            it.Col = i % cols;
            it.Target = GridLayout.FitAspect(cells[i], aspects[i], Inset);

            // Begin the morph from the window's real on-screen footprint.
            var r = it.Window.Rect;
            it.Current = new RectD(r.Left, r.Top, Math.Max(1, r.Width), Math.Max(1, r.Height));
        }
    }

    private static double AspectOf(Item it)
    {
        var s = it.Thumb.SourceSize;
        double a = (s.cx > 0 && s.cy > 0) ? (double)s.cx / s.cy : it.Window.Aspect;
        return a <= 0 ? 1.0 : a;
    }

    // ---------------------------------------------------------------- animation loop
    private void OnRender(object? sender, EventArgs e)
    {
        double t = _clock.Elapsed.TotalSeconds;
        double dt = t - _lastT;
        _lastT = t;
        if (dt <= 0) return;

        // Framerate-independent exponential smoothing toward each target rect.
        double k = 1.0 - Math.Exp(-dt / Tau);

        foreach (var it in _items)
        {
            it.Current = Lerp(it.Current, it.Target, k);
            var r = it.Current;
            it.Thumb.SetDestination(
                (int)Math.Round(r.X), (int)Math.Round(r.Y),
                (int)Math.Round(r.X + r.W), (int)Math.Round(r.Y + r.H));
        }

        if (_highlightInit)
        {
            _highlightCurrent = Lerp(_highlightCurrent, _highlightTarget, k);
            UpdateHighlightVisual();
        }
    }

    private static RectD Lerp(RectD a, RectD b, double k) => new(
        a.X + (b.X - a.X) * k,
        a.Y + (b.Y - a.Y) * k,
        a.W + (b.W - a.W) * k,
        a.H + (b.H - a.H) * k);

    // ---------------------------------------------------------------- selection chrome
    private void SetHighlightTarget(bool initialize)
    {
        if (_selected < 0 || _selected >= _items.Count) return;

        const double pad = 8;
        var tr = _items[_selected].Target;
        _highlightTarget = new RectD(tr.X - pad, tr.Y - pad, tr.W + pad * 2, tr.H + pad * 2);

        if (initialize)
        {
            // Start the ring around the item's current (incoming) footprint so it flies in too.
            var c = _items[_selected].Current;
            _highlightCurrent = new RectD(c.X - pad, c.Y - pad, c.W + pad * 2, c.H + pad * 2);
            _highlightInit = true;
        }

        TitleText.Text = _items[_selected].Window.Title;
    }

    private void UpdateHighlightVisual()
    {
        var r = _highlightCurrent;
        double s = _dpi;

        Canvas.SetLeft(Highlight, r.X / s);
        Canvas.SetTop(Highlight, r.Y / s);
        Highlight.Width = r.W / s;
        Highlight.Height = r.H / s;
        Highlight.Visibility = Visibility.Visible;

        // Center the title pill just beneath the highlighted thumbnail.
        TitleBox.Visibility = Visibility.Visible;
        TitleBox.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double pillW = TitleBox.DesiredSize.Width;
        double centerX = (r.X + r.W / 2) / s;
        Canvas.SetLeft(TitleBox, centerX - pillW / 2);
        Canvas.SetTop(TitleBox, (r.Y + r.H) / s + 6);
    }

    // ---------------------------------------------------------------- input
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape: e.Handled = true; Cancel(); break;
            case Key.Enter: e.Handled = true; ConfirmSelection(); break;
            case Key.Left: e.Handled = true; Move(-1, 0); break;
            case Key.Right: e.Handled = true; Move(1, 0); break;
            case Key.Up: e.Handled = true; Move(0, -1); break;
            case Key.Down: e.Handled = true; Move(0, 1); break;
            case Key.Tab: e.Handled = true; Move(1, 0); break;
        }
        base.OnPreviewKeyDown(e);
    }

    private void Move(int dx, int dy)
    {
        if (_items.Count == 0) return;
        int n = _items.Count;
        int cur = _selected < 0 ? 0 : _selected;

        if (dx != 0)
            _selected = (cur + dx + n) % n;            // horizontal wraps around
        else if (dy != 0)
        {
            int next = cur + dy * _cols;               // vertical moves a full row
            if (next >= 0 && next < n) _selected = next;
        }

        SetHighlightTarget(initialize: false);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);

        var p = e.GetPosition(this);
        double px = p.X * _dpi, py = p.Y * _dpi;
        for (int i = 0; i < _items.Count; i++)
        {
            var r = _items[i].Target;
            if (px >= r.X && px <= r.X + r.W && py >= r.Y && py <= r.Y + r.H)
            {
                _selected = i;
                ConfirmSelection();
                return;
            }
        }
    }

    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);
        // Clicking away (or any focus loss after we're up) dismisses the overlay.
        if (_ready && !_closing) Cancel();
    }

    // ---------------------------------------------------------------- exit paths
    public void ConfirmSelection()
    {
        if (_closing) return;
        _closing = true;

        IntPtr target = _selected >= 0 && _selected < _items.Count
            ? _items[_selected].Window.Handle
            : IntPtr.Zero;

        Close();
        if (target != IntPtr.Zero) FocusWindow(target);
    }

    private void Cancel()
    {
        if (_closing) return;
        _closing = true;
        Close();
    }

    private static void FocusWindow(IntPtr hwnd)
    {
        if (NativeMethods.IsIconic(hwnd))
            NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);

        // Windows blocks background foreground-stealing; attaching our input thread to
        // both the outgoing foreground thread and the target's thread lifts that block.
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

    private void OnClosed(object? sender, EventArgs e)
    {
        CompositionTarget.Rendering -= OnRender;
        foreach (var it in _items) it.Thumb.Dispose();
        _items.Clear();
    }
}

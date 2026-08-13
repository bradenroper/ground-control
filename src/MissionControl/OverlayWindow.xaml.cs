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

/// <summary>
/// One overlay per monitor: a dark, full-monitor, topmost window that lays out the windows
/// on that monitor as live DWM thumbnails and animates them. It is a view driven by
/// <see cref="OverlayController"/>; the controller owns selection and exit decisions.
/// </summary>
public partial class OverlayWindow : Window
{
    private enum Phase { Intro, Idle, Outro }

    private const double LayoutMargin = 48;     // px inset of the layout from the monitor edges
    private const double Spacing = 26;          // px gap pushed between windows
    private const double Inset = 12;            // px the thumbnail sits inside its slot
    private const double Tau = 0.055;           // s — highlight smoothing for nav
    private const double HighlightPad = 8;      // px the ring extends past the thumbnail

    private readonly OverlayController _controller;
    private readonly MonitorInfo _monitor;
    private readonly List<WindowInfo> _windows;
    private readonly double _introDuration;     // s — morph in from real positions (user setting)
    private readonly double _outroDuration;     // s — morph back out on confirm/cancel (user setting)
    private readonly List<OverlayItem> _items = new();
    private readonly Stopwatch _clock = new();

    private IntPtr _hwnd;
    private double _dpi = 1.0;
    private Phase _phase = Phase.Intro;
    private double _lastT;

    private int _selectedLocal = -1;
    private RectD _highlightCurrent;
    private RectD _highlightTarget;
    private bool _highlightActive;

    private bool _outroReported;

    public IReadOnlyList<OverlayItem> Items => _items;

    public OverlayWindow(OverlayController controller, MonitorInfo monitor, List<WindowInfo> windows, Settings settings)
    {
        _controller = controller;
        _monitor = monitor;
        _windows = windows;
        _introDuration = settings.IntroDuration;
        _outroDuration = settings.OutroDuration;

        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    // ---------------------------------------------------------------- setup (no HWND required)
    /// <summary>Computes the layout for this monitor's windows. Safe to call before the window is shown.</summary>
    public void Prepare()
    {
        _dpi = _monitor.DpiScale;

        int ox = _monitor.Bounds.Left, oy = _monitor.Bounds.Top;
        double mw = _monitor.Bounds.Width, mh = _monitor.Bounds.Height;

        // Real footprints, expressed monitor-local (so the morph starts exactly under each window).
        var reals = _windows
            .Select(w => new RectD(w.Rect.Left - ox, w.Rect.Top - oy, Math.Max(1, w.Rect.Width), Math.Max(1, w.Rect.Height)))
            .ToArray();

        var targets = NaturalLayout.Compute(mw, mh, reals, LayoutMargin, Spacing);

        for (int i = 0; i < _windows.Count; i++)
        {
            // Inset the thumbnail slightly within its target so the highlight ring has room.
            var slot = Deflate(targets[i], Inset);
            _items.Add(new OverlayItem
            {
                Window = _windows[i],
                Owner = this,
                Start = reals[i],
                Target = slot,
                Current = reals[i],
                GlobalTarget = new RectD(targets[i].X + ox, targets[i].Y + oy, targets[i].W, targets[i].H)
            });
        }
    }

    public void ShowOverlay() => Show();

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;

        var b = _monitor.Bounds;
        NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_TOPMOST, b.Left, b.Top, b.Width, b.Height,
            NativeMethods.SWP_SHOWWINDOW);

        // DWM composites thumbnails in registration order — the *last* registered draws on top.
        // _items is in z-order top-to-bottom (EnumWindows order), so register back-to-front to
        // reproduce the real desktop depth: the frontmost window ends up on top of the stack.
        for (int i = _items.Count - 1; i >= 0; i--)
            _items[i].Thumb = DwmThumbnail.Register(_hwnd, _items[i].Window.Handle);
    }

    /// <summary>
    /// Re-registers an item's thumbnail so it draws on top of all the others (DWM stacks by
    /// registration order, and there is no reorder API). Used to lift the window we're about to
    /// focus above the rest during the outro, so it visually rises before real focus transfers.
    /// </summary>
    public void RaiseToTop(OverlayItem item)
    {
        if (_hwnd == IntPtr.Zero || !_items.Contains(item)) return;
        item.Thumb?.Dispose();
        item.Thumb = DwmThumbnail.Register(_hwnd, item.Window.Handle);
        PushDestination(item);   // re-show immediately at its current rect (avoid a blank frame)
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        _phase = Phase.Intro;
        _clock.Restart();
        _lastT = 0;
        CompositionTarget.Rendering += OnRender;
    }

    // ---------------------------------------------------------------- controller-driven state
    public void SetSelected(OverlayItem? selected, bool snap)
    {
        int local = (selected != null && selected.Owner == this) ? _items.IndexOf(selected) : -1;
        bool hadSelection = _selectedLocal >= 0;
        _selectedLocal = local;

        if (local < 0)
        {
            _highlightActive = false;
            Highlight.Visibility = Visibility.Collapsed;
            TitleBox.Visibility = Visibility.Collapsed;
            return;
        }

        _highlightTarget = Inflate(_items[local].Target, HighlightPad);
        if (snap || !hadSelection)
        {
            // Snap the ring onto the window (its incoming footprint during the intro).
            var basis = _phase == Phase.Intro ? _items[local].Current : _items[local].Target;
            _highlightCurrent = Inflate(basis, HighlightPad);
        }
        _highlightActive = true;
        TitleText.Text = _items[local].Window.Title;
    }

    public void BeginOutro()
    {
        // Hide the chrome and reverse each thumbnail from its slot back to the real window.
        _highlightActive = false;
        Highlight.Visibility = Visibility.Collapsed;
        TitleBox.Visibility = Visibility.Collapsed;

        _phase = Phase.Outro;
        _outroReported = false;
        _clock.Restart();
        _lastT = 0;

        if (_items.Count == 0)
            ReportOutroComplete();   // nothing to animate
    }

    public void CloseOverlay() => Close();

    // ---------------------------------------------------------------- animation loop
    private void OnRender(object? sender, EventArgs e)
    {
        double t = _clock.Elapsed.TotalSeconds;
        double dt = t - _lastT;
        _lastT = t;
        if (dt <= 0) return;

        switch (_phase)
        {
            case Phase.Intro:
            {
                double p = Progress(t, _introDuration);
                double eased = EaseInOutCubic(p);
                foreach (var it in _items)
                {
                    it.Current = Lerp(it.Start, it.Target, eased);
                    PushDestination(it);
                }
                RideHighlightAlong();
                if (p >= 1.0) _phase = Phase.Idle;
                break;
            }

            case Phase.Idle:
            {
                // Thumbnails are settled (DWM keeps them live); only the highlight animates.
                if (_highlightActive)
                {
                    double k = 1.0 - Math.Exp(-dt / Tau);
                    _highlightCurrent = Lerp(_highlightCurrent, _highlightTarget, k);
                    UpdateHighlightVisual();
                }
                break;
            }

            case Phase.Outro:
            {
                double p = Progress(t, _outroDuration);
                double eased = EaseInOutCubic(p);
                foreach (var it in _items)
                {
                    it.Current = Lerp(it.Target, it.Start, eased);
                    PushDestination(it);
                }
                if (p >= 1.0) ReportOutroComplete();
                break;
            }
        }
    }

    private void RideHighlightAlong()
    {
        if (_selectedLocal < 0 || !_highlightActive) return;
        _highlightCurrent = Inflate(_items[_selectedLocal].Current, HighlightPad);
        UpdateHighlightVisual();
    }

    private void ReportOutroComplete()
    {
        if (_outroReported) return;
        _outroReported = true;
        CompositionTarget.Rendering -= OnRender;
        _controller.OnOutroComplete();
    }

    private void PushDestination(OverlayItem it)
    {
        if (it.Thumb == null) return;
        var r = it.Current;
        it.Thumb.SetDestination(
            (int)Math.Round(r.X), (int)Math.Round(r.Y),
            (int)Math.Round(r.X + r.W), (int)Math.Round(r.Y + r.H));
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

        TitleBox.Visibility = Visibility.Visible;
        TitleBox.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double pillW = TitleBox.DesiredSize.Width;
        double centerX = (r.X + r.W / 2) / s;
        Canvas.SetLeft(TitleBox, centerX - pillW / 2);
        Canvas.SetTop(TitleBox, (r.Y + r.H) / s + 6);
    }

    // ---------------------------------------------------------------- input -> controller
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
            case Key.Enter:
            case Key.Left:
            case Key.Right:
            case Key.Up:
            case Key.Down:
            case Key.Tab:
                e.Handled = true;
                _controller.HandleKey(e.Key);
                break;
        }
        base.OnPreviewKeyDown(e);
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
                _controller.ConfirmItem(_items[i]);
                return;
            }
        }
        _controller.CancelFromClick();   // clicked empty space
    }

    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);
        _controller.NotifyDeactivated();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        CompositionTarget.Rendering -= OnRender;
        foreach (var it in _items) it.Thumb?.Dispose();
    }

    // ---------------------------------------------------------------- helpers
    /// <summary>Linear 0..1 progress. A zero-length duration lands on 1 immediately (instant morph).</summary>
    private static double Progress(double t, double duration) =>
        duration <= 0 ? 1.0 : Math.Min(1.0, t / duration);

    private static double EaseInOutCubic(double p) =>
        p < 0.5 ? 4 * p * p * p : 1 - Math.Pow(-2 * p + 2, 3) / 2;

    private static RectD Inflate(RectD r, double pad) =>
        new(r.X - pad, r.Y - pad, r.W + pad * 2, r.H + pad * 2);

    private static RectD Deflate(RectD r, double pad) =>
        new(r.X + pad, r.Y + pad, Math.Max(1, r.W - pad * 2), Math.Max(1, r.H - pad * 2));

    private static RectD Lerp(RectD a, RectD b, double k) => new(
        a.X + (b.X - a.X) * k,
        a.Y + (b.Y - a.Y) * k,
        a.W + (b.W - a.W) * k,
        a.H + (b.H - a.H) * k);
}

using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace MissionControl;

/// <summary>
/// The settings UI, reachable from the tray icon. Every change applies (and saves) immediately —
/// there is no OK/Cancel — so the hotkey can be tested the moment it is rebound.
/// </summary>
public partial class SettingsWindow : Window
{
    private static readonly Brush IdleBorder = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x48));
    private static readonly Brush CaptureBorder = new SolidColorBrush(Color.FromRgb(0x3B, 0x9E, 0xFF));

    private readonly Settings _settings;
    private readonly DispatcherTimer _saveDebounce;
    private bool _loading;
    private bool _capturing;

    /// <summary>
    /// Asked to bind a newly captured combination. Returns false if Windows refused it (another
    /// app owns it), in which case the change is rolled back and the reason shown.
    /// </summary>
    public Func<HotKeySpec, bool>? TryApplyHotkey { get; set; }

    public event Action? QuitRequested;

    public SettingsWindow(Settings settings)
    {
        _settings = settings;
        InitializeComponent();

        IdleBorder.Freeze();
        CaptureBorder.Freeze();

        // Dragging a slider raises hundreds of changes; the value applies at once but the
        // file is only rewritten once the drag settles.
        _saveDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _saveDebounce.Tick += (_, _) => { _saveDebounce.Stop(); _settings.Save(); };
        Closed += (_, _) => { if (_saveDebounce.IsEnabled) { _saveDebounce.Stop(); _settings.Save(); } };

        LoadFromSettings();
        PathText.Text = Settings.FilePath;

        EnabledCheck.Checked += OnEnabledToggled;
        EnabledCheck.Unchecked += OnEnabledToggled;
        StartupCheck.Checked += OnStartupToggled;
        StartupCheck.Unchecked += OnStartupToggled;
    }

    /// <summary>Pulls the current values into the controls without echoing back into the settings.</summary>
    public void LoadFromSettings()
    {
        _loading = true;
        EnabledCheck.IsChecked = _settings.Enabled;
        StartupCheck.IsChecked = _settings.StartWithWindows;
        HotkeyText.Text = _settings.Hotkey;
        IntroSlider.Value = _settings.IntroDuration;
        OutroSlider.Value = _settings.OutroDuration;
        IntroValue.Text = FormatSeconds(_settings.IntroDuration);
        OutroValue.Text = FormatSeconds(_settings.OutroDuration);
        _loading = false;
    }

    // ---------------------------------------------------------------- general
    private void OnEnabledToggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _settings.Enabled = EnabledCheck.IsChecked == true;
        _settings.Save();
    }

    private void OnStartupToggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        bool wanted = StartupCheck.IsChecked == true;

        if (!AutoStart.SetEnabled(wanted))
        {
            SetStatus("Couldn't update the startup entry — the registry rejected the change.", warn: true);
            _loading = true;
            StartupCheck.IsChecked = AutoStart.IsEnabled();
            _loading = false;
            return;
        }

        _settings.StartWithWindows = wanted;
        _settings.Save();
    }

    // ---------------------------------------------------------------- hotkey capture
    private void OnRebindClick(object sender, RoutedEventArgs e) => BeginCapture();

    private void OnHotkeyBoxClick(object sender, MouseButtonEventArgs e) => BeginCapture();

    private void BeginCapture()
    {
        _capturing = true;
        HotkeyText.Text = "Press a key combination…";
        HotkeyBorder.BorderBrush = CaptureBorder;
        RebindButton.Content = "Cancel";
        SetStatus("Include at least one of Ctrl, Alt, Shift or Win. Esc cancels.", warn: false);
        Keyboard.ClearFocus();
        Focus();
    }

    private void EndCapture()
    {
        _capturing = false;
        HotkeyText.Text = _settings.Hotkey;
        HotkeyBorder.BorderBrush = IdleBorder;
        RebindButton.Content = "Change…";
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (!_capturing)
        {
            base.OnPreviewKeyDown(e);
            return;
        }

        e.Handled = true;

        // Alt combinations arrive as Key.System with the real key in SystemKey.
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None)
        {
            EndCapture();
            SetStatus("Hotkey unchanged.", warn: false);
            return;
        }

        var spec = HotKeySpec.FromKeyPress(key, Keyboard.Modifiers);
        if (spec == null) return;    // modifier-only press: keep waiting for the real key

        ApplyHotkey(spec.Value);
    }

    private void ApplyHotkey(HotKeySpec spec)
    {
        // Bind first: if Windows refuses, nothing was saved and the old hotkey still works.
        if (TryApplyHotkey != null && !TryApplyHotkey(spec))
        {
            EndCapture();
            SetStatus($"{spec} is already registered by another app — try a different combination.", warn: true);
            return;
        }

        _settings.Hotkey = spec.ToString();
        _settings.Save();
        EndCapture();
        SetStatus($"Hotkey set to {_settings.Hotkey}.", warn: false);
    }

    // ---------------------------------------------------------------- animation
    private void OnIntroChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        IntroValue.Text = FormatSeconds(e.NewValue);
        if (_loading) return;
        _settings.IntroDuration = e.NewValue;
        SaveSoon();
    }

    private void OnOutroChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        OutroValue.Text = FormatSeconds(e.NewValue);
        if (_loading) return;
        _settings.OutroDuration = e.NewValue;
        SaveSoon();
    }

    private void SaveSoon()
    {
        _saveDebounce.Stop();
        _saveDebounce.Start();
    }

    // ---------------------------------------------------------------- footer
    private void OnRestoreDefaults(object sender, RoutedEventArgs e)
    {
        var previous = _settings.HotKeySpec;
        _settings.RestoreDefaults();

        var restored = _settings.HotKeySpec;
        if (restored != previous && TryApplyHotkey != null && !TryApplyHotkey(restored))
            SetStatus($"{restored} is in use by another app — pick a different hotkey.", warn: true);
        else
            SetStatus("Defaults restored.", warn: false);

        _settings.Save();
        LoadFromSettings();
    }

    private void OnQuit(object sender, RoutedEventArgs e) => QuitRequested?.Invoke();

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    // ---------------------------------------------------------------- helpers
    private void SetStatus(string text, bool warn)
    {
        HotkeyStatus.Text = text;
        HotkeyStatus.Foreground = warn
            ? new SolidColorBrush(Color.FromRgb(0xFF, 0x8A, 0x6B))
            : new SolidColorBrush(Color.FromRgb(0x8A, 0x8D, 0x9B));
    }

    private static string FormatSeconds(double seconds) => $"{seconds:0.00} s";
}

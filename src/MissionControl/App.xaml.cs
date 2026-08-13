using System;
using System.Linq;
using System.Threading;
using System.Windows;
using MissionControl.Native;

namespace MissionControl;

public partial class App : Application
{
    // Virtual-key code for the fixed emergency-quit hotkey (Ctrl+Alt+Shift+Q).
    private const uint VK_Q = 0x51;

    /// <summary>
    /// Also named in the installer (<c>AppMutex</c>) so setup can detect a running copy and
    /// ask the user to close it instead of failing to replace the executable.
    /// </summary>
    private const string InstanceMutexName = "MissionControlSingleInstance";

    /// <summary>Command-line switch (and Start Menu shortcut) that opens the settings window.</summary>
    private const string SettingsArgument = "--settings";

    private Mutex? _instanceMutex;
    private InstanceSignal? _signal;
    private Settings _settings = null!;
    private HotKeyManager? _hotkeys;
    private TrayIcon? _tray;
    private OverlayController? _controller;
    private SettingsWindow? _settingsWindow;

    private int _activateHotkeyId = HotKeyManager.InvalidId;
    private HotKeySpec _boundHotkey;      // what is actually registered right now

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        bool wantsSettings = e.Args.Any(a => string.Equals(a, SettingsArgument, StringComparison.OrdinalIgnoreCase));

        if (!ClaimSingleInstance())
        {
            // Another copy already owns the hotkey and the tray icon. Hand it the request, if any,
            // then get out of its way.
            if (wantsSettings) InstanceSignal.Send();
            Shutdown();
            return;
        }

        // The app lives in the background with no window until the hotkey is pressed.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        _settings = Settings.Load();

        // Save once at the end of startup if the file is missing (so it exists to hand-edit) or
        // if the Run key disagreed with it. Deferred until the tray exists to hear the event.
        bool needsSave = AutoStart.Reconcile(_settings) | _settings.IsFirstRun;
        _settings.Changed += OnSettingsChanged;

        _hotkeys = new HotKeyManager();

        // Ctrl+Alt+Shift+Q : quit. Fixed, and deliberately kept even when the app is "off",
        // so there is always a keyboard way out.
        _hotkeys.Register(NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT | NativeMethods.MOD_SHIFT, VK_Q, Shutdown);

        // "MissionControl.exe --settings" from a second instance lands here.
        _signal = new InstanceSignal(Dispatcher, ShowSettings);

        _tray = new TrayIcon(_settings);
        _tray.Activate += OnActivate;
        _tray.SettingsRequested += ShowSettings;
        _tray.QuitRequested += Shutdown;

        if (!ApplyHotkey())
            _tray.ShowMessage("Hotkey unavailable",
                $"{_settings.Hotkey} is already in use by another app. Open Settings from the tray icon to pick another.",
                warning: true);
        else if (_settings.IsFirstRun)
            _tray.ShowMessage("Mission Control is running",
                $"Press {_settings.Hotkey} to show your windows. Settings live in the tray icon.");

        if (needsSave)
            _settings.Save();

        if (wantsSettings)
            ShowSettings();
    }

    private bool ClaimSingleInstance()
    {
        _instanceMutex = new Mutex(initiallyOwned: true, InstanceMutexName, out bool createdNew);
        if (createdNew) return true;

        _instanceMutex.Dispose();
        _instanceMutex = null;
        return false;
    }

    // ---------------------------------------------------------------- hotkey binding
    /// <summary>(Re)binds the activation hotkey to match the settings. False if Windows refused it.</summary>
    private bool ApplyHotkey()
    {
        _hotkeys!.Unregister(_activateHotkeyId);
        _activateHotkeyId = HotKeyManager.InvalidId;
        _boundHotkey = default;

        if (!_settings.Enabled) return true;      // "off" means we hold no hotkey at all

        var spec = _settings.HotKeySpec;
        _activateHotkeyId = _hotkeys.Register(spec, OnActivate);
        if (_activateHotkeyId == HotKeyManager.InvalidId) return false;

        _boundHotkey = spec;
        return true;
    }

    /// <summary>
    /// Tries a combination on behalf of the settings window, keeping the old binding if the new
    /// one is unavailable — so a rejected rebind never leaves the app with no hotkey.
    /// </summary>
    private bool TryBindHotkey(HotKeySpec spec)
    {
        if (!_settings.Enabled) return true;      // nothing to hold while disabled; accept the choice

        var previous = _boundHotkey;
        _hotkeys!.Unregister(_activateHotkeyId);
        _activateHotkeyId = HotKeyManager.InvalidId;
        _boundHotkey = default;

        int id = _hotkeys.Register(spec, OnActivate);
        if (id != HotKeyManager.InvalidId)
        {
            _activateHotkeyId = id;
            _boundHotkey = spec;
            return true;
        }

        if (previous.IsValid)
        {
            _activateHotkeyId = _hotkeys.Register(previous, OnActivate);
            if (_activateHotkeyId != HotKeyManager.InvalidId) _boundHotkey = previous;
        }
        return false;
    }

    private void OnSettingsChanged()
    {
        _tray?.Sync();
        _settingsWindow?.LoadFromSettings();

        // Only touch the registration when the effective binding actually changed.
        var wanted = _settings.Enabled ? _settings.HotKeySpec : default;
        if (wanted != _boundHotkey && !ApplyHotkey())
            _tray?.ShowMessage("Hotkey unavailable", $"{_settings.Hotkey} is already in use by another app.", warning: true);
    }

    // ---------------------------------------------------------------- overlay
    private void OnActivate()
    {
        if (_controller != null)
        {
            // Second press of the hotkey while open = focus the highlighted window.
            _controller.ConfirmSelection();
            return;
        }

        var controller = new OverlayController(_hotkeys!.Handle, _settings);
        controller.Closed += () => _controller = null;
        if (controller.Show())
            _controller = controller;
    }

    // ---------------------------------------------------------------- settings window
    private void ShowSettings()
    {
        if (_settingsWindow == null)
        {
            _settingsWindow = new SettingsWindow(_settings) { TryApplyHotkey = TryBindHotkey };
            _settingsWindow.QuitRequested += Shutdown;
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            _settingsWindow.Show();
        }
        else if (_settingsWindow.WindowState == WindowState.Minimized)
        {
            _settingsWindow.WindowState = WindowState.Normal;
        }

        // The app has no foreground window of its own, so nudge the window to the front.
        _settingsWindow.Topmost = true;
        _settingsWindow.Activate();
        _settingsWindow.Topmost = false;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _hotkeys?.Dispose();
        _signal?.Dispose();
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }
}

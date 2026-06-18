using System.Windows;
using MissionControl.Native;

namespace MissionControl;

public partial class App : Application
{
    // Virtual-key codes for the hotkeys.
    private const uint VK_M = 0x4D;
    private const uint VK_Q = 0x51;

    private HotKeyManager? _hotkeys;
    private OverlayController? _controller;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // The app lives in the background with no window until the hotkey is pressed.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        _hotkeys = new HotKeyManager();

        // Ctrl+Alt+M : open the overlay, or confirm the current selection if already open.
        if (!_hotkeys.Register(NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT, VK_M, OnActivate))
        {
            MessageBox.Show(
                "Could not register the hotkey Ctrl+Alt+M — it may already be in use by another app.",
                "Mission Control", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        // Ctrl+Alt+Shift+Q : quit the app (it otherwise has no visible UI).
        _hotkeys.Register(NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT | NativeMethods.MOD_SHIFT, VK_Q, Shutdown);
    }

    private void OnActivate()
    {
        if (_controller != null)
        {
            // Second press of the hotkey while open = focus the highlighted window.
            _controller.ConfirmSelection();
            return;
        }

        var controller = new OverlayController(_hotkeys!.Handle);
        controller.Closed += () => _controller = null;
        if (controller.Show())
            _controller = controller;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hotkeys?.Dispose();
        base.OnExit(e);
    }
}

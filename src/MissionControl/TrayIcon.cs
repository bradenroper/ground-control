using System;
using System.Drawing;
using System.Windows;
using Forms = System.Windows.Forms;

namespace MissionControl;

/// <summary>
/// The notification-area icon and its menu — the app's only persistent UI. Uses WinForms'
/// <c>NotifyIcon</c>, which lives in the Windows Desktop runtime (no package needed); WPF's
/// dispatcher pumps the messages it relies on.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private readonly Settings _settings;
    private readonly Forms.NotifyIcon _icon;
    private readonly Forms.ToolStripMenuItem _enabledItem;
    private readonly Forms.ToolStripMenuItem _startupItem;
    private Icon? _iconResource;

    /// <summary>The menu's "Show Mission Control" item — the same action as the hotkey.</summary>
    public event Action? Activate;
    public event Action? SettingsRequested;
    public event Action? QuitRequested;

    public TrayIcon(Settings settings)
    {
        _settings = settings;

        _enabledItem = new Forms.ToolStripMenuItem("Enabled", null, (_, _) =>
        {
            _settings.Enabled = !_settings.Enabled;
            _settings.Save();
        })
        { CheckOnClick = false };

        _startupItem = new Forms.ToolStripMenuItem("Start with Windows", null, (_, _) =>
        {
            bool wanted = !_settings.StartWithWindows;
            // Only record the preference if the registry actually accepted it, so the tick
            // never claims something that will not happen at sign-in.
            if (!AutoStart.SetEnabled(wanted))
            {
                ShowMessage("Couldn't change startup", "The registry rejected the change to your startup entry.");
                return;
            }
            _settings.StartWithWindows = wanted;
            _settings.Save();
        })
        { CheckOnClick = false };

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(new Forms.ToolStripMenuItem("Show Mission Control", null, (_, _) => Activate?.Invoke())
        {
            Font = new Font(Forms.Control.DefaultFont, System.Drawing.FontStyle.Bold)
        });
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(_enabledItem);
        menu.Items.Add(_startupItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(new Forms.ToolStripMenuItem("Settings…", null, (_, _) => SettingsRequested?.Invoke()));
        menu.Items.Add(new Forms.ToolStripMenuItem("Quit", null, (_, _) => QuitRequested?.Invoke()));
        menu.Opening += (_, _) => Sync();

        _icon = new Forms.NotifyIcon
        {
            Icon = LoadIcon(),
            ContextMenuStrip = menu,
            Visible = true
        };
        _icon.DoubleClick += (_, _) => SettingsRequested?.Invoke();

        Sync();
    }

    /// <summary>Refreshes the checkmarks and tooltip from the current settings.</summary>
    public void Sync()
    {
        _enabledItem.Checked = _settings.Enabled;
        _startupItem.Checked = _settings.StartWithWindows;

        string state = _settings.Enabled ? _settings.Hotkey : "disabled";
        // NotifyIcon truncates the tooltip past 63 characters.
        _icon.Text = Truncate($"Mission Control ({state})", 63);
    }

    public void ShowMessage(string title, string body)
    {
        _icon.BalloonTipTitle = title;
        _icon.BalloonTipText = body;
        _icon.BalloonTipIcon = Forms.ToolTipIcon.Warning;
        _icon.ShowBalloonTip(5000);
    }

    private Icon LoadIcon()
    {
        try
        {
            var stream = Application.GetResourceStream(
                new Uri("pack://application:,,,/Resources/app.ico"))?.Stream;
            if (stream != null)
            {
                using (stream)
                    // Pick the size the notification area actually wants, so it stays crisp on high DPI.
                    _iconResource = new Icon(stream, Forms.SystemInformation.SmallIconSize);
                return _iconResource;
            }
        }
        catch (Exception)
        {
            // Fall through to the stock icon rather than failing to start.
        }
        return SystemIcons.Application;
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _iconResource?.Dispose();
    }
}

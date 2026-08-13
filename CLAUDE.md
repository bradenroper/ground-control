# Ground Control (Windows)

A Windows clone of macOS Mission Control / Exposé. A global hotkey shrinks every open
window into a tidy, aspect-preserving grid; arrow keys move a highlight between them;
pressing the hotkey again (or Enter) focuses the highlighted window.

## Build & run

```sh
dotnet build src/GroundControl/GroundControl.csproj
dotnet run   --project src/GroundControl/GroundControl.csproj
```

Requires the .NET SDK (tested on 9.0.306) with the WindowsDesktop runtime. **No NuGet
packages** — everything is `user32.dll` / `dwmapi.dll` P/Invoke plus WPF and the one
WinForms type (`NotifyIcon`) that ship with the Windows Desktop runtime.

The app has **no window of its own** beyond the settings dialog; it lives in the
notification area and listens for the global hotkey.

`GroundControl.exe --settings` opens the settings window. If a copy is already running,
the new process hands the request over (named event, `InstanceSignal.cs`) and exits, so
there is only ever one instance.

## Packaging the installer

```sh
powershell -ExecutionPolicy Bypass -File build/build-installer.ps1            # dist/GroundControl-Setup-1.0.0.exe
powershell -ExecutionPolicy Bypass -File build/build-installer.ps1 -Version 1.1.0
powershell -ExecutionPolicy Bypass -File build/make-icon.ps1                  # regenerate Resources/app.ico
```

Needs Inno Setup 6 on the *build* machine (`winget install JRSoftware.InnoSetup`); nothing
from it ships inside the app. The script publishes **self-contained single-file win-x64**, so
the target machine needs no .NET runtime — that is what makes the ~60 MB installer worth it:
installing the runtime would need admin rights, and this app is aimed at users who may not
have them.

Everything about the install is per-user and elevation-free (`PrivilegesRequired=lowest`):
`%LOCALAPPDATA%\Programs\Ground Control`, Start Menu shortcuts, an HKCU uninstall entry, and
the HKCU `Run` key for auto-start. An admin can still pass `/ALLUSERS`. `AppId` in
`installer/GroundControl.iss` must never change — it is how Windows recognises an upgrade.

The installer is **unsigned**, so SmartScreen shows an "unknown publisher" prompt on first
run; an Authenticode certificate is the only real fix.

## Hotkeys

| Keys | Action |
|------|--------|
| `Ctrl+Up` (configurable) | Open the overlay; press again to focus the highlighted window |
| Arrow keys / `Tab` | Move the highlight between windows |
| `Enter` | Focus the highlighted window |
| Mouse click | Focus the clicked window |
| `Esc` / click empty space | Dismiss without switching |
| `Ctrl+Alt+Shift+Q` | Quit the app (fixed; deliberately kept even when the app is disabled) |

## Settings

Tray icon → **Settings…** (or `--settings`). Everything applies and saves the moment it
changes — there is no OK/Cancel — so a rebound hotkey can be tried immediately.

| Setting | Effect |
|---------|--------|
| Enabled | Off releases the hotkey entirely, so another app can use the combination |
| Hotkey | Any modifier + key; captured by pressing the combination |
| Open / Close duration | The intro and outro morph lengths, 0–1.5 s (0 = instant) |
| Start with Windows | HKCU `Run` entry |

Stored in `%APPDATA%\GroundControl\settings.json`, which is meant to be hand-editable —
malformed or out-of-range values fall back to defaults rather than blocking startup. The file
is only read at startup, so hand-edits need a restart; changes made in the UI apply live.

## How it works (the important design decisions)

- **Live DWM thumbnails, not real window moves.** We never resize the actual windows
  (that causes reflow jank and hits per-app minimum sizes). Instead each window is shown
  as a `DwmRegisterThumbnail` live preview — the same GPU-composited mechanism Alt+Tab
  uses. See `Native/DwmThumbnail.cs`.
- **One overlay window per monitor.** `OverlayController` enumerates monitors, assigns each
  window to the monitor it lives on (`MonitorFromWindow`), and creates one full-monitor
  overlay each. Every monitor lays out only its own windows, so a window morphs from its real
  position *on its own screen* — nothing jumps to the primary monitor. The controller holds a
  single global selection shared across all overlays.
- **Natural (declumping) layout, not a grid.** `Layout/NaturalLayout.cs` starts each window at
  its real position, pushes overlapping windows apart (minimum-translation resolution), then
  uniformly scales the arrangement to fit. Windows keep their relative position and relative
  size, so travel is minimized and big windows stay bigger — the KDE *Present Windows* approach.
- **The overlay is opaque.** DWM thumbnails do not render onto a layered/`AllowsTransparency`
  window, so the backdrop is a solid dark window, not a see-through dim. (A blurred desktop
  screenshot backdrop is a possible future upgrade.)
- **Coordinate space is physical pixels.** Each overlay is sized to its monitor in physical
  pixels via `SetWindowPos` (`OnSourceInitialized`), matching the DWM thumbnail coordinate
  space exactly. Per-monitor DPI comes from `GetDpiForMonitor`; WPF chrome (ring, title pill)
  is converted back to DIPs via that scale (`_dpi`).
- **Three animation phases** in `OverlayWindow.OnRender` (driven by `CompositionTarget.Rendering`):
  - **Intro** — an `easeInOutCubic` morph from real position → slot, over the user's *Open*
    duration (default 0.2s), captured per-overlay at construction so a mid-flight settings
    change can't retime a running animation.
  - **Idle** — thumbnails are static (DWM keeps them live); only the highlight animates, via
    framerate-independent exponential smoothing (`Tau`) for snappy navigation.
  - **Outro** — on confirm/cancel, an eased morph back to real positions over the *Close*
    duration. The controller waits for *all* monitors' outros (`OnOutroComplete`) before
    focusing the chosen window.
- **The tray icon is WinForms' `NotifyIcon`.** WPF has no equivalent, and `NotifyIcon` comes
  with the Windows Desktop runtime — so `UseWindowsForms` costs nothing at runtime. WinForms'
  implicit usings collide with WPF's (`Application`, `MessageBox`, `Point`), so the csproj
  removes them and `TrayIcon.cs` qualifies its WinForms references.
- **Auto-start is the HKCU `Run` key, never HKLM or a scheduled task** — those need admin.
  The registry is the source of truth and `settings.json` mirrors it (`AutoStart.Reconcile`):
  the installer's checkbox, Task Manager or any other tool can change it, and a stale settings
  file must not silently revert that on the next launch.
- **Navigation is spatial, not index-based.** Arrow keys pick the best-scoring window in the
  pressed direction using global (virtual-desktop) target centers, so it crosses monitors and
  handles the non-grid organic layout naturally.
- **Selection chrome lives in the gap.** DWM draws thumbnails *on top* of WPF content, so the
  highlight ring is drawn slightly larger than the thumbnail (in the `Inset` gap) and the title
  pill sits below it.

## File map

| File | Responsibility |
|------|----------------|
| `App.xaml.cs` | Startup, single-instance guard, hotkey (re)binding, tray + settings lifecycle |
| `HotKeyManager.cs` | Hidden message window + `RegisterHotKey`, with per-registration unbinding |
| `Settings.cs` | The JSON preferences file, its defaults, clamping and change event |
| `HotKeySpec.cs` | Modifier+key combination: Win32 flags ⇄ "Ctrl+Alt+M" text ⇄ WPF key press |
| `TrayIcon.cs` | Notification-area icon and menu (WinForms `NotifyIcon`) |
| `SettingsWindow.xaml(.cs)` | Settings UI; applies and saves on every change |
| `AutoStart.cs` | "Start with Windows" via the per-user `Run` key |
| `InstanceSignal.cs` | Named event that forwards `--settings` to the running instance |
| `OverlayController.cs` | Per-monitor overlays, global selection, spatial nav, confirm/cancel, focus |
| `OverlayWindow.xaml(.cs)` | One monitor's view: layout → animate (intro/idle/outro) → input forwarding |
| `OverlayItem.cs` | One window's thumbnail + animation rects |
| `Layout/NaturalLayout.cs` | Declumping "natural" layout (KDE Present Windows style) |
| `Layout/RectD.cs` | Double-precision rectangle |
| `Native/MonitorEnumerator.cs` | Enumerates monitors + per-monitor DPI |
| `Native/WindowEnumerator.cs` | Alt+Tab-style filtered list of real top-level windows |
| `Native/DwmThumbnail.cs` | RAII wrapper over a DWM thumbnail registration |
| `Native/NativeMethods.cs` | All P/Invoke signatures and constants |
| `Resources/app.ico` | App + tray icon, generated by `build/make-icon.ps1` |
| `installer/GroundControl.iss` | Inno Setup script (per-user, no elevation) |
| `build/build-installer.ps1` | Publish self-contained + compile the setup exe |
| `build/make-icon.ps1` | Draws and encodes the multi-size `.ico` |

## Known limitations / next steps

- **Minimized windows are skipped** — DWM can't produce a live thumbnail of a window that
  isn't being composited. Could restore-then-thumbnail, or fall back to a static icon.
- **Backdrop is flat dark.** Capture + blur the desktop for the real macOS look.
- **Windows 11 hides new tray icons** in the overflow flyout, and there is no supported API to
  promote one. Users drag it onto the taskbar themselves.
- **The installer is unsigned**, so first-run SmartScreen warns about an unknown publisher.
- **No update mechanism.** Reinstalling over the top works (same `AppId`), but nothing checks
  for new versions.
- **x64 only.** `build-installer.ps1 -Runtime win-arm64` publishes, but the `.iss` declares
  `ArchitecturesAllowed=x64compatible`, so an ARM64 build needs its own compile.
- **Cross-monitor highlight doesn't slide** — the ring snaps when selection jumps to another
  monitor (each overlay owns its own ring). Within a monitor it slides smoothly.
- Focus uses the `AttachThreadInput` trick in `OverlayController.FocusWindow` to bypass
  foreground-stealing restrictions; watch this if activation ever flakes.

# Mission Control (Windows) — prototype

A Windows clone of macOS Mission Control / Exposé. A global hotkey shrinks every open
window into a tidy, aspect-preserving grid; arrow keys move a highlight between them;
pressing the hotkey again (or Enter) focuses the highlighted window.

## Build & run

```sh
dotnet build src/MissionControl/MissionControl.csproj
dotnet run   --project src/MissionControl/MissionControl.csproj
```

Requires the .NET SDK (tested on 9.0.306) with the WindowsDesktop runtime. **No NuGet
packages** — everything is `user32.dll` / `dwmapi.dll` P/Invoke that ships with Windows.

The app has **no visible window** until invoked. It sits in the background and listens
for global hotkeys.

## Hotkeys

| Keys | Action |
|------|--------|
| `Ctrl+Alt+M` | Open the overlay; press again to focus the highlighted window |
| Arrow keys / `Tab` | Move the highlight between windows |
| `Enter` | Focus the highlighted window |
| Mouse click | Focus the clicked window |
| `Esc` / click empty space | Dismiss without switching |
| `Ctrl+Alt+Shift+Q` | Quit the app |

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
  - **Intro** — a fixed `IntroDuration` (0.75s) `easeInOutCubic` morph from real position → slot.
  - **Idle** — thumbnails are static (DWM keeps them live); only the highlight animates, via
    framerate-independent exponential smoothing (`Tau`) for snappy navigation.
  - **Outro** — on confirm/cancel, a `OutroDuration` (0.55s) eased morph back to real positions.
    The controller waits for *all* monitors' outros (`OnOutroComplete`) before focusing the
    chosen window.
- **Navigation is spatial, not index-based.** Arrow keys pick the best-scoring window in the
  pressed direction using global (virtual-desktop) target centers, so it crosses monitors and
  handles the non-grid organic layout naturally.
- **Selection chrome lives in the gap.** DWM draws thumbnails *on top* of WPF content, so the
  highlight ring is drawn slightly larger than the thumbnail (in the `Inset` gap) and the title
  pill sits below it.

## File map

| File | Responsibility |
|------|----------------|
| `App.xaml.cs` | Startup, registers global hotkeys, owns the controller lifecycle |
| `HotKeyManager.cs` | Hidden message window + `RegisterHotKey` |
| `OverlayController.cs` | Per-monitor overlays, global selection, spatial nav, confirm/cancel, focus |
| `OverlayWindow.xaml(.cs)` | One monitor's view: layout → animate (intro/idle/outro) → input forwarding |
| `OverlayItem.cs` | One window's thumbnail + animation rects |
| `Layout/NaturalLayout.cs` | Declumping "natural" layout (KDE Present Windows style) |
| `Layout/RectD.cs` | Double-precision rectangle |
| `Native/MonitorEnumerator.cs` | Enumerates monitors + per-monitor DPI |
| `Native/WindowEnumerator.cs` | Alt+Tab-style filtered list of real top-level windows |
| `Native/DwmThumbnail.cs` | RAII wrapper over a DWM thumbnail registration |
| `Native/NativeMethods.cs` | All P/Invoke signatures and constants |

## Known limitations / next steps

- **Minimized windows are skipped** — DWM can't produce a live thumbnail of a window that
  isn't being composited. Could restore-then-thumbnail, or fall back to a static icon.
- **Backdrop is flat dark.** Capture + blur the desktop for the real macOS look.
- **No tray icon.** Quit is hotkey-only (`Ctrl+Alt+Shift+Q`).
- **Cross-monitor highlight doesn't slide** — the ring snaps when selection jumps to another
  monitor (each overlay owns its own ring). Within a monitor it slides smoothly.
- Focus uses the `AttachThreadInput` trick in `OverlayController.FocusWindow` to bypass
  foreground-stealing restrictions; watch this if activation ever flakes.

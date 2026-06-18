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
- **The overlay is opaque.** DWM thumbnails do not render onto a layered/`AllowsTransparency`
  window, so the backdrop is a solid dark window, not a see-through dim. (A blurred desktop
  screenshot backdrop is a possible future upgrade.)
- **Coordinate space is physical pixels.** The overlay is sized to the primary screen in
  physical pixels via `SetWindowPos` (`OnSourceInitialized`) so it matches the DWM thumbnail
  coordinate space exactly. WPF chrome (highlight ring, title pill) is converted back to DIPs
  using the window DPI scale (`_dpi`).
- **Animation = exponential smoothing.** `CompositionTarget.Rendering` eases each thumbnail's
  `Current` rect toward its `Target` rect every frame (`OnRender`). This is framerate-independent
  and naturally re-targets when the selection moves — no storyboards. Tune feel with `Tau`.
- **Selection chrome lives in the gap.** DWM draws thumbnails *on top* of WPF content, so the
  highlight ring is drawn slightly larger than the thumbnail (in the `Inset` gap) and the title
  pill sits below it.

## File map

| File | Responsibility |
|------|----------------|
| `App.xaml.cs` | Startup, registers global hotkeys, owns the overlay lifecycle |
| `HotKeyManager.cs` | Hidden message window + `RegisterHotKey` |
| `OverlayWindow.xaml(.cs)` | The fullscreen grid: enumerate → layout → animate → navigate → focus |
| `Layout/GridLayout.cs` | Aspect-preserving grid packing (picks column count by max thumbnail area) |
| `Native/WindowEnumerator.cs` | Alt+Tab-style filtered list of real top-level windows |
| `Native/DwmThumbnail.cs` | RAII wrapper over a DWM thumbnail registration |
| `Native/NativeMethods.cs` | All P/Invoke signatures and constants |

## Known limitations (v1) / next steps

- **Primary monitor only.** Multi-monitor layout is the obvious next step (enumerate per
  monitor, lay out windows on the screen they live on, or one combined surface).
- **Minimized windows are skipped** — DWM can't produce a live thumbnail of a window that
  isn't being composited. Could restore-then-thumbnail, or fall back to a static icon.
- **Backdrop is flat dark.** Capture + blur the desktop for the real macOS look.
- **No tray icon.** Quit is hotkey-only (`Ctrl+Alt+Shift+Q`).
- **Layout is a plain grid.** macOS morphs windows from their real positions into a "natural"
  layout — KDE's open-source *Present Windows* effect is the reference for that algorithm.
- Focus uses the `AttachThreadInput` trick in `FocusWindow` to bypass foreground-stealing
  restrictions; watch this if activation ever flakes.

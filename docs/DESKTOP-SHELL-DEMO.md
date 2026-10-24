# WDMM Desktop — Shell Demo

## Layout

```
+--------------------------------------------------+
| [icon] WDMM            [caption buttons]         |  <- custom title bar (drag region)
+--------+-----------------------------------------+
| [<]    |                                         |  <- collapse button (explicit)
|        |                                         |
| Folders|                                         |
| Music  |         Content Frame                   |
| Playlist|        (ShellPage placeholder)         |
| Manga  |                                         |
| Network|                                         |
| Tasks  |                                         |
| About  |                                         |
|        |                                         |
| v0.x.x |                                         |  <- FFI status strip
| [gear] Settings                                  |  <- Settings button (pane bottom)
+--------+-----------------------------------------+
```

**Collapsed sidebar** (52 px): icons only; expand button replaces collapse button.

**Expanded sidebar** (220 px): icon + label.

## Chrome

- **Mica (BaseAlt)** fills the entire window — no `Background` is set on the root `Grid`, `NavigationView`, or any container that would occlude the backdrop.
- **Immersive title bar**: `ExtendsContentIntoTitleBar = true` + `SetTitleBar(AppTitleBar)`. A compact icon + "WDMM" text is the drag strip; OS caption buttons overlay the right end automatically.

## Destinations

| Destination     | Status        |
|-----------------|---------------|
| Folders         | Placeholder   |
| Music Library   | Placeholder   |
| Playlists       | Placeholder   |
| Manga           | Placeholder   |
| Network Library | Placeholder   |
| Tasks           | Placeholder   |
| About           | Placeholder   |

All destinations navigate the `ContentFrame` to `ShellPage` with the destination name as a parameter.

## Settings

- **Not** a sidebar tab.
- The **Settings button** lives at the bottom of the sidebar pane.
- Clicking it opens a `ContentDialog` modal (`SettingsDialog`) with:
  - FFI/core version badge
  - Bind Mode combo (Local / Global / Custom)
  - Port text box
  - API Token (read-only)
- The dialog content panel is backed by an `AcrylicBrush` (tinted, `TintOpacity 0.65`).

## FFI Smoke

`wdmm_version()` is called in the `MainWindow` constructor. The result (or error message) appears in the sidebar status strip and is passed to the Settings dialog. The `CopyRustDll` MSBuild target is unchanged.

## How to Run

### Prerequisites

- Windows 11 22H2+ (build 22621+)
- Visual Studio 2022 17.8+ with **Windows App SDK** workload
- .NET 8 SDK

### Build and run (Visual Studio)

```
Open WdmmDesktop.sln
Select Debug | x64
F5 (or Ctrl+F5 for run without debugger)
```

### Build and run (CLI, Windows)

```powershell
cd WdmmDesktop
dotnet build -c Debug
dotnet run --project WdmmDesktop.csproj
```

`wdmm_ffi.dll` must be copied to the output directory for the FFI smoke test to pass. Without it the app still launches; the status strip shows the load error.

## Files Changed / Added

| Path | Change |
|------|--------|
| `WdmmDesktop/App.xaml` | Added `NavItemStyle` (RadioButton as nav pill) |
| `WdmmDesktop/MainWindow.xaml` | Full rewrite — Mica, immersive title bar, sidebar, content frame |
| `WdmmDesktop/MainWindow.xaml.cs` | Full rewrite — title bar setup, FFI smoke, sidebar toggle, navigation |
| `WdmmDesktop/Pages/ShellPage.xaml` | New — placeholder page (icon + title, no background) |
| `WdmmDesktop/Pages/ShellPage.xaml.cs` | New |
| `WdmmDesktop/Dialogs/SettingsDialog.xaml` | New — ContentDialog with AcrylicBrush |
| `WdmmDesktop/Dialogs/SettingsDialog.xaml.cs` | New |
| `docs/DESKTOP-SHELL-DEMO.md` | New — this document |

## TODOs

- Wire Bind Mode / Port / Token Save button to actual `wdmm_openapi_*` FFI calls.
- Add `CopyRustDll` MSBuild target to auto-deploy `wdmm_ffi.dll` to output on Windows.
- Replace placeholder pages with real UI (file browser, track list, playlist editor, etc.).
- Animate sidebar expand/collapse with a `DoubleAnimation` on `SidebarColumn.Width`.
- Persist sidebar state (expanded/collapsed) and last selected destination across launches.

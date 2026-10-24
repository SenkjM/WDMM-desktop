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

Mirrors Android HomeShell order. Each destination navigates `ContentFrame` to `ShellPage`,
which shows the destination-specific Segoe Fluent icon and title.

| Destination | Icon (Segoe UI MDL2) | Status      |
|-------------|----------------------|-------------|
| Folders     | `E8B7` Folder        | Placeholder |
| Music       | `E8D6` MusicNote     | Placeholder |
| Playlists   | `E762` SortLines     | Placeholder |
| Manga       | `E82D` ContactCard   | Placeholder |
| Network     | `E968` NetworkTower  | Placeholder |
| Tasks       | `E896` TaskList      | Placeholder |
| About       | `E946` Info          | Placeholder |

## Settings

- **Not** a sidebar tab.
- The **Settings button** lives at the bottom of the sidebar pane.
- Clicking it opens a `ContentDialog` modal (`SettingsDialog`) with:
  - FFI/core version badge
  - Bind Mode combo (Local / Global / Custom)
  - Port text box — pre-populated from `wdmm_openapi_port()` if the server is already running
  - API Token (read-only) — pre-populated from `wdmm_token_get()`
- The dialog content panel is backed by an `AcrylicBrush` (tinted, `TintOpacity 0.65`).
- **Save** calls `wdmm_openapi_start(port)`; on success the token and bound port fields
  refresh from live FFI. Errors are shown inline without closing the dialog.
- All FFI calls are wrapped in try/catch — `wdmm_ffi.dll` absent on Linux is a no-op.

## FFI Smoke

`wdmm_version()` is called in the `MainWindow` constructor. The result (or error message)
appears in the sidebar status strip and is passed to the Settings dialog.
The `CopyRustDll` MSBuild target is unchanged.

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

`wdmm_ffi.dll` must be copied to the output directory for the FFI smoke test to pass.
Without it the app still launches; the status strip shows the load error, and Settings Save
is a silent no-op.

## Files Changed / Added

| Path | Change |
|------|--------|
| `WdmmDesktop/App.xaml` | Added `NavItemStyle` (RadioButton as nav pill) |
| `WdmmDesktop/MainWindow.xaml` | Mica, immersive title bar, sidebar, content frame; labels Music/Network (was Music Library/Network Library) |
| `WdmmDesktop/MainWindow.xaml.cs` | Title bar setup, FFI smoke, sidebar toggle, navigation; updated label refs |
| `WdmmDesktop/Pages/ShellPage.xaml` | Placeholder page (icon + title, no background) |
| `WdmmDesktop/Pages/ShellPage.xaml.cs` | Per-destination icon glyph in `OnNavigatedTo` |
| `WdmmDesktop/Dialogs/SettingsDialog.xaml` | ContentDialog with AcrylicBrush; SaveStatusText strip; token starts empty |
| `WdmmDesktop/Dialogs/SettingsDialog.xaml.cs` | `LoadFfiState()` pre-populates port+token; `PrimaryButtonClick` wired to `wdmm_openapi_start` |
| `docs/DESKTOP-SHELL-DEMO.md` | Updated destinations table, Settings section, files table |

## TODOs

- Replace placeholder pages with real UI (file browser, track list, playlist editor, etc.).
- Animate sidebar expand/collapse with a `DoubleAnimation` on `SidebarColumn.Width`.
- Persist sidebar state (expanded/collapsed) and last selected destination across launches.
- Support Bind Mode (Custom host) once `wdmm_openapi_start` accepts a bind address.
- Add a Rotate Token button to the Settings dialog (`wdmm_token_rotate`).

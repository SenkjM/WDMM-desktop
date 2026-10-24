# WDMM Desktop — Android Feature Parity

## Shell Chrome (locked, unchanged)

```
+--------------------------------------------------+
| [icon] WDMM            [caption buttons]         |  <- custom title bar (drag region)
+--------+-----------------------------------------+
| [<]    |                                         |  <- collapse button (explicit)
|        |                                         |
| Folders|                                         |
| Music  |         Content Frame                   |
| Playlist|        (real page per destination)     |
| Manga  |                                         |
| Network|                                         |
| Tasks  |                                         |
| Cast   |                                         |
| About  |                                         |
|        |                                         |
| v0.x.x |                                         |  <- FFI status strip
| [gear] Settings                                  |  <- Settings button (pane bottom)
+--------+-----------------------------------------+
```

Collapsed sidebar (52 px): icons only. Expanded (220 px): icon + label.

Chrome rules are unchanged from the shell demo:
- **Mica (BaseAlt)** — no `Background` on any root container.
- **Immersive title bar** — `ExtendsContentIntoTitleBar = true` + `SetTitleBar(AppTitleBar)`.
- **Settings** — pane-bottom button → `ContentDialog` modal (not a sidebar tab).
- **Sidebar toggle** — explicit Expand / Collapse buttons only.
- Unpackaged: `WindowsPackageType=None` + `WindowsAppSDKSelfContained=true`.

---

## Android Parity Matrix

| Destination | Status | Notes |
|-------------|--------|-------|
| **Folders** | partial | Local directory browser (path bar, Up, double-click, empty/error states, source toggle). Remote WebDAV: BLOCKED — `wdmm_webdav_list` not exported in wdmm-core @ d8b2217. Shows InfoBar with clear message; no C# WebDAV client invented. |
| **Music** | **real** | Track list from `wdmm_track_list()`. AutoSuggestBox search via `wdmm_track_search()`. Cast button hands off track to CastPage via `AppState.PendingCastTrack`. |
| **Playlists** | **OpenAPI bridge** | `GET/POST/DELETE /v1/playlists` + `GET /v1/playlists/:id/entries` via HttpClient when server running. InfoBar guard when OpenAPI down. Direct FFI `wdmm_playlist_list` not exported @ d8b2217. |
| **Manga** | stub | BLOCKED — `wdmm_manga_list` / chapter APIs not in FFI or OpenAPI @ d8b2217 (storage tables `manga_series`/`manga_chapters` exist; no CRUD yet). Rich library-shaped page with always-open InfoBar naming missing APIs. |
| **Network** | partial | WebDAV account form; account list persisted locally (`%LOCALAPPDATA%\Wdmm\accounts.json`). Sync Now wired to `wdmm_sync_trigger`. TODO: `wdmm_account_list` / `wdmm_account_save` FFI not yet exported. |
| **Tasks** | **real** | Queue list from `wdmm_queue_status_json()`. Pending count badge. Per-task cancel via `wdmm_queue_cancel`. |
| **Cast / DLNA** | **real** | DMR discovery via `wdmm_dlna_discover_json(2000 ms)`. USN-keyed compat prefs persisted to `%LOCALAPPDATA%\Wdmm\dmr_prefs.json`. Proxy-only cast enforced. Single-file mutex. HLS: **DEFERRED pending Core R12 / proxy-hls** — no submodule bump this slice, no Cast HLS unlock (see HLS section below). |
| **Settings** | **real** | `wdmm_openapi_start`, `wdmm_token_get`, `wdmm_token_rotate`. Bind Mode + Port + Token display. |
| **About** | **real** | `wdmm_version()`, OpenAPI running status + port, parity matrix summary. |

---

## HLS — Deferred pending Core R12 / proxy-hls

> **HLS cast is deferred in this slice.** Submodule remains at `d8b2217`.
> No submodule bump. No `wdmm_proxy_register_hls` wiring. No Cast HLS unlock.
>
> Unblocked by: `wdmm-core feat/proxy-hls-ffmpeg` merging into main (Core R12).
> When it lands: bump submodule, uncomment `wdmm_proxy_start_hls` / `wdmm_proxy_stop_hls`
> P/Invoke in `Generated/NativeMethods.g.cs`, and remove the HLS guard in `CastPage.CastBtn_Click`.

Cast / DLNA locked rules (unchanged — mirrors Android):
1. **DLNA → proxy only.** The proxy URL (`http://127.0.0.1:{port}/s/…`) is sent to the renderer; a raw `file://` or direct path is never passed.
2. **Single-file mutex.** Before registering a new proxy token, the previous one is unregistered.
3. **No auto HLS ↔ direct fallback.** The user's per-DMR `ForceDirect` choice is honoured verbatim.
4. **USN-keyed DMR preferences** — persisted to `%LOCALAPPDATA%\Wdmm\dmr_prefs.json`.
5. **HLS mode is blocked** until Core R12 / proxy-hls lands. The toggle is present but the cast aborts with an explicit message if `ForceDirect = false` is chosen.

---

## FFI Matrix

| FFI function | Exported? | Used by |
|---|---|---|
| `wdmm_version` | ✓ | MainWindow status strip, AboutPage |
| `wdmm_library_init` | ✓ | MainWindow constructor |
| `wdmm_track_list` | ✓ | MusicPage |
| `wdmm_track_search` | ✓ | MusicPage search |
| `wdmm_track_upsert` / `_get` / `_delete` | ✓ | NativeMethods (available) |
| `wdmm_queue_status_json` | ✓ | TasksPage |
| `wdmm_queue_cancel` | ✓ | TasksPage cancel |
| `wdmm_queue_enqueue` | ✓ | NativeMethods (available) |
| `wdmm_openapi_start` / `_stop` / `_is_running` / `_port` | ✓ | SettingsDialog |
| `wdmm_token_get` | ✓ | SettingsDialog, PlaylistsPage |
| `wdmm_token_rotate` | ✓ | SettingsDialog |
| `wdmm_dlna_discover_json` | ✓ | CastPage |
| `wdmm_dlna_cast` | ✓ | CastPage |
| `wdmm_proxy_start` / `_stop` / `_port` | ✓ | CastPage |
| `wdmm_proxy_register_file` | ✓ | CastPage |
| `wdmm_proxy_unregister` | ✓ | CastPage (single-file mutex) |
| `wdmm_sync_trigger` | ✓ | NetworkPage |
| `wdmm_proxy_start_hls` | **DEFERRED** | CastPage HLS path — blocked pending Core R12 / proxy-hls |
| `wdmm_webdav_list` | **TODO** | FoldersPage remote browse |
| `wdmm_playlist_list` / `_save` | **TODO** | PlaylistsPage (currently: OpenAPI bridge) |
| `wdmm_manga_list` / `_chapters` | **TODO** | MangaPage |
| `wdmm_account_list` / `_save` | **TODO** | NetworkPage (currently: local JSON) |

---

## OpenAPI Bridge (Playlists)

PlaylistsPage connects via HttpClient to `http://127.0.0.1:{port}/v1/playlists` when the
OpenAPI server is running (started from Settings). This is the interim path until
`wdmm_playlist_list` / `wdmm_playlist_save` are exported by wdmm-core.

Routes used:
- `GET  /v1/playlists`        → list all playlists (`{"playlists":[…]}`)
- `POST /v1/playlists`        → create (`{"name":"…"}` → 201 `{"id":…,"name":…,…}`)
- `GET  /v1/playlists/:id/entries` → list entries (`{"entries":[…]}`)
- `DELETE /v1/playlists/:id`  → delete + cascade entries (204)

All requests carry `Authorization: Bearer {TokenGet()}`.

---

## Submodule

Current SHA pinned in worktree: **d8b22178049d800ecfa4f4ad0879e0372ac33f71**

**Do not bump** this slice. HLS deferred — see section above.

To unlock HLS cast (after Core R12 lands):
```
cd rust/wdmm-core
git fetch origin feat/proxy-hls-ffmpeg
git checkout <merged-SHA>
cd ../..
git add rust/wdmm-core
git commit -m "chore(submodule): bump wdmm-core to feat/proxy-hls-ffmpeg SHA"
```
Then uncomment the `wdmm_proxy_start_hls` / `wdmm_proxy_stop_hls` P/Invoke declarations in
`WdmmDesktop/Generated/NativeMethods.g.cs` and remove the HLS guard in `CastPage.CastBtn_Click`.

---

## Local Persistence

Files written to `%LOCALAPPDATA%\Wdmm\`:

| File | Contents | Replace with FFI when |
|---|---|---|
| `library.db` | SQLite library (managed by Rust) | already FFI |
| `accounts.json` | WebDAV account list | `wdmm_account_list` / `_save` exported |
| `dmr_prefs.json` | Per-USN DMR compat prefs | `wdmm_dmr_pref_*` exported |

---

## Build and Run

### Prerequisites

- Windows 11 22H2+ (build 22621+)
- Visual Studio 2022 17.8+ with **Windows App SDK** workload
- .NET 8 SDK

### CLI (Windows)

```powershell
cd WdmmDesktop
dotnet build -c Debug
dotnet run --project WdmmDesktop.csproj
```

### DLL path (Windows)

`wdmm_ffi.dll` must be in the output directory (same folder as the `.exe`).
Build path from submodule: `rust\wdmm-core\target\release\wdmm_ffi.dll`
On Windows the `CopyRustDll` MSBuild target copies it automatically.

### Linux dev (C# compile-check only)

The project compiles on Linux (`net8.0-windows10.0.22621.0` target).
All FFI calls are wrapped in `try/catch`; a missing DLL is silently tolerated —
the app still launches, the status strip shows the load error, and all FFI-wired
actions are no-ops.

```bash
cd WdmmDesktop
dotnet build -c Debug
```

---

## Files Added / Changed

| Path | Change |
|------|--------|
| `WdmmDesktop/AppState.cs` | Cross-page state (PendingCastTrack for Music→Cast handoff) |
| `WdmmDesktop/LocalStore.cs` | JSON persistence helper for accounts + DMR prefs |
| `WdmmDesktop/Models/TrackItem.cs` | Track POCO (System.Text.Json) |
| `WdmmDesktop/Models/QueueTask.cs` | Queue task POCO |
| `WdmmDesktop/Models/DmrDevice.cs` | DMR device + DmrPref POCOs; locked cast rule docs |
| `WdmmDesktop/Models/WebDavAccount.cs` | WebDAV account POCO |
| `WdmmDesktop/Models/PlaylistItem.cs` | **NEW** — PlaylistItem + PlaylistEntryItem POCOs (OpenAPI shapes) |
| `WdmmDesktop/Pages/FoldersPage.xaml/.cs` | **UPDATED** — source toggle (Local/Remote), WebDAV blocked InfoBar, empty/error states |
| `WdmmDesktop/Pages/MusicPage.xaml/.cs` | Track list + search (real FFI); Cast button |
| `WdmmDesktop/Pages/PlaylistsPage.xaml/.cs` | **UPDATED** — full OpenAPI bridge; two-pane list+entries; create/delete dialogs |
| `WdmmDesktop/Pages/MangaPage.xaml/.cs` | **UPDATED** — rich library-shaped stub; always-open blocked InfoBar |
| `WdmmDesktop/Pages/NetworkPage.xaml/.cs` | WebDAV account form; Sync Now (real FFI) |
| `WdmmDesktop/Pages/TasksPage.xaml/.cs` | Queue list + cancel (real FFI) |
| `WdmmDesktop/Pages/CastPage.xaml/.cs` | DLNA discovery + proxy cast; locked rules enforced |
| `WdmmDesktop/Pages/AboutPage.xaml/.cs` | **UPDATED** — parity matrix reflects Folders/Playlists/Manga + HLS deferred |
| `WdmmDesktop/Dialogs/DmrInfoDialog.xaml/.cs` | Per-DMR info dialog (USN, location, compat) |
| `WdmmDesktop/Dialogs/SettingsDialog.xaml/.cs` | Rotate Token button (wdmm_token_rotate) |
| `WdmmDesktop/MainWindow.xaml` | Cast nav item (E93E glyph) |
| `WdmmDesktop/MainWindow.xaml.cs` | Real page routing; library_init on startup; SelectNavItem() |
| `WdmmDesktop/App.xaml.cs` | Exposed static MainWindow ref for cross-page nav |
| `WdmmDesktop/Generated/NativeMethods.g.cs` | HLS/Range stub comments for feat/proxy-hls-ffmpeg |
| `docs/DESKTOP-PARITY.md` | **UPDATED** — this file; Folders/Playlists/Manga matrix + HLS deferred note |

---

## Remaining Blockers

| Blocker | Unblocked by |
|---|---|
| Playlists direct FFI (`wdmm_playlist_list`) | wdmm-core exports FFI; replace OpenAPI bridge |
| Folders remote WebDAV (`wdmm_webdav_list`) | wdmm-core exports FFI |
| Manga library (`wdmm_manga_list` / `_chapters`) | wdmm-core adds CRUD + FFI export |
| Network account CRUD (`wdmm_account_*`) | wdmm-core exports FFI; replace local JSON |
| Cast HLS mode (`wdmm_proxy_start_hls`) | **Core R12 / proxy-hls** — bump submodule; see HLS section |
| Sidebar animation | Polish (DoubleAnimation on SidebarColumn.Width) — not parity-blocking |
| Sidebar state persistence | Persist last destination + expand/collapse across launches |

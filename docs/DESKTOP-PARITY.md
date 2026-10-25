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
| File source|                                    |
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
| **Folders** | partial | Local directory browser. Remote WebDAV browse lives on **File source** page (`wdmm_webdav_*`), not Folders. |
| **Music** | **real** | Track list from `wdmm_track_list()`. AutoSuggestBox search via `wdmm_track_search()`. Cast button hands off track to CastPage via `AppState.PendingCastTrack`. |
| **Playlists** | **real (FFI)** | `wdmm_playlist_*` NativeMethods wired; OpenAPI bridge remains for third-party. |
| **Manga** | **real** | `wdmm_manga_list` / `_get` / `_list_pages` / `_list_progress` read; detail dialog writes via `wdmm_manga_upsert` / `_set_progress` / `_delete`. |
| **File source (Network)** | **real** | Multi-provider directory browse via `wdmm_account_list_dir/mkdir/rename/remove` (Core account FFI); 9 providers: webdav, baidu_netdisk, 123_open, 115open, aliyundrive_open, terabox, netease_music, ctfile, crypt; sources via `wdmm_account_*`; remember-path settings FFI; Settings sync uses selected source + `wdmm_sync_trigger`. i18n zh/en via Loc + Resources.resw. Fallback to legacy `wdmm_webdav_*` for accounts without ID. |
| **Tasks** | **real** | Queue list from `wdmm_queue_status_json()`. Pending count badge. Per-task cancel via `wdmm_queue_cancel`. |
| **Cast / DLNA** | **real** | DMR discovery via `wdmm_dlna_discover_json(2000 ms)`. USN-keyed prefs in Core independent `dmr_prefs.db` via `wdmm_dmr_prefs_*` (`force_direct` + `override_name`); one-shot migrate from legacy `dmr_prefs.json` → `.bak`. Proxy-only cast; single-file mutex; HLS unlocked. LocalStream refuses until cloud/crypt drivers. NetworkDirect refuses (cache first). No silent HLS↔direct fallback. |
| **Settings** | **real** | TabView chrome (920×640). Sync: `sync_remote_root` / `sync_interval` / `playlist_sync_enabled` / `sync_encrypt_password` KV + `wdmm_sync_trigger`. Backup: passphrase + `backup_remote_path` + `wdmm_backup_upload` / `_list` / `_download_restore` (WDMMBK01/WDMMEN01). Sources via `wdmm_account_*`. Library `media_scan_*`. OpenAPI third-party only. |
| **About** | **real** | `wdmm_version()`, OpenAPI running status + port, parity matrix summary. |

---

## HLS — Unlocked (wdmm-core e236ece, P0.5)

HLS cast is now fully wired. Submodule bumped to `e236ece` (feat/proxy-hls-ffmpeg merged).

**LocalFile cast path:**
- `ForceDirect = true`  → `ProxyRegisterFile` — direct-stream proxy, no transcode.
- `ForceDirect = false` → `ProxyRegisterHls(device_id: Usn, filePath, modeJson: null)` — HLS transcode via ffmpeg. `device_id` is the renderer's Usn (stable, per locked rule 4). Returns `{"token","playlist_template","port"}`.

**LocalStream hook (prepared, not yet active):**
`CastPage.PerformProxyCast` has an explicit hook that returns a clear `NotSupported` error until a real `WdmmProxyRangeCallback` source exists. Do NOT invent a Dart bridge.

**NetworkDirect refuse:**
A bare http(s) URL has no local file to proxy. CastPage refuses with an explicit error (locked rule 1). Future path: materialize → LocalFile, or register_stream with a fetch callback.

Cast / DLNA locked rules (unchanged — mirrors Android):
1. **DLNA → proxy only.** The proxy URL is sent to the renderer; raw `file://` or direct path never passed.
2. **Single-file mutex.** Previous token unregistered before a new one is registered. HLS tokens use `ProxyUnregisterHls`; file tokens use `ProxyUnregister`.
3. **No auto HLS ↔ direct fallback.** The user's per-DMR `ForceDirect` choice is honoured verbatim.
4. **USN-keyed DMR preferences** — Core `dmr_prefs.db` via `wdmm_dmr_prefs_*` (legacy JSON migrated once).
5. **HLS mode is live.** `wdmm_proxy_register_hls` / `_unregister_hls` / `_hls_seek` wired and exposed via safe C# wrappers.

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
| `wdmm_proxy_register_file` | ✓ | CastPage (ForceDirect=true) |
| `wdmm_proxy_unregister` | ✓ | CastPage (single-file mutex, file tokens) |
| `wdmm_sync_trigger` | ✓ | SettingsDialog Sync & backup |
| `wdmm_backup_create` / `_restore` / `_upload` / `_list` / `_download_restore` | ✓ | SettingsDialog Sync & backup |
| `wdmm_dmr_prefs_init` / `_list` / `_get` / `_upsert` / `_delete` | ✓ | DmrPrefsStore → CastDialog / DmrInfoDialog |
| `wdmm_proxy_register_hls` | ✓ | CastPage (ForceDirect=false, HLS transcode) |
| `wdmm_proxy_unregister_hls` | ✓ | CastPage (single-file mutex, HLS tokens) |
| `wdmm_proxy_hls_seek` | ✓ | NativeMethods (wrapper present; cast UI may not call yet) |
| `wdmm_proxy_stream_alloc_buf` / `_free_buf` | ✓ | NativeMethods (bindings present) |
| `wdmm_proxy_register_stream` | ✓ | NativeMethods (binding + wrapper present; CastPage hook prepared — no stream source yet) |
| `wdmm_proxy_renew_stream_callback` | ✓ | NativeMethods (binding + wrapper present) |
| `wdmm_webdav_list` / `_mkdir` / `_rename` / `_remove` | ✓ | NetworkPage (legacy fallback; accounts without ID) |
| `wdmm_account_list` / `_get` / `_upsert` / `_delete` | ✓ | AccountStore → NetworkPage + Settings sync picker |
| `wdmm_account_list_dir` / `_mkdir` / `_rename` / `_remove` | ✓ | NetworkPage (multi-provider browse via CloudFfi) |
| `wdmm_playlist_list` / `_create` / `_delete` / `_list_entries` / `_set_entries` | ✓ | PlaylistsPage (FFI) |
| `wdmm_manga_*` | ✓ | MangaPage read + upsert/set_progress/delete |
| `wdmm_track_delete_by_source` | ✓ | NativeMethods (available; v5 cutover FFI) |
| `wdmm_library_clear_all` / `_clear_index` | ✓ | NativeMethods (available; v5 cutover FFI) |
| `wdmm_cue_album_upsert` / `_slice_upsert` / `_slice_delete` / `_album_delete` / `_remaining_slices` | ✓ | NativeMethods (available; v5 cutover FFI; corrected ABI signatures in 42d0a90) |
| `wdmm_deleted_track_upsert` / `_clear` / `_list` / `_unpushed` / `_mark_pushed` / `_purge_up_to` / `wdmm_tombstoned_remote_paths` | ✓ | NativeMethods (available; v6 tombstone FFI) |
| `wdmm_sync_state_get` / `_save` / `_clear` | ✓ | NativeMethods (available; v6 sync_state FFI) |

---

## OpenAPI (third-party only)

Playlists / library / accounts are Desktop-internal via FFI. OpenAPI remains an
opt-in HTTP surface for external clients (Settings → 第三方/开发者).

---

## Submodule

Current SHA pinned: **abc1f4a** (v6: deleted_tracks + sync_state tombstone tracking + per-remote sync cursors).

---

## Local Persistence

Files written to `%LOCALAPPDATA%\Wdmm\`:

| File | Contents | Replace with FFI when |
|---|---|---|
| `library.db` | SQLite library (managed by Rust) | already FFI |
| ~~`accounts.json`~~ | sources now in Core DB | `wdmm_account_*` via AccountStore |
| `dmr_prefs.db` | Per-USN DMR prefs (Core) | `wdmm_dmr_prefs_*` via DmrPrefsStore |
| ~~`dmr_prefs.json`~~ | legacy; migrated once → `.bak` | — |

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
| `WdmmDesktop/Models/WebDavAccount.cs` | **UPDATED** — free-form credentials dictionary for multi-provider support (Wave C) |
| `WdmmDesktop/Models/PlaylistItem.cs` | **NEW** — PlaylistItem + PlaylistEntryItem POCOs (OpenAPI shapes) |
| `WdmmDesktop/Pages/FoldersPage.xaml/.cs` | **UPDATED** — source toggle (Local/Remote), WebDAV blocked InfoBar, empty/error states |
| `WdmmDesktop/Pages/MusicPage.xaml/.cs` | Track list + search (real FFI); Cast button |
| `WdmmDesktop/Pages/PlaylistsPage.xaml/.cs` | **UPDATED** — full OpenAPI bridge; two-pane list+entries; create/delete dialogs |
| `WdmmDesktop/Pages/MangaPage.xaml/.cs` | **UPDATED** — list + detail write (upsert/progress/delete) via manga FFI |
| `WdmmDesktop/Pages/NetworkPage.xaml/.cs` | **UPDATED** — multi-provider support (9 providers); dynamic credential fields per provider; browse via CloudFfi account FFI |
| `WdmmDesktop/Services/CloudFfi.cs` | **NEW** — public FFI facade for driver and account operations (Wave C) |
| `WdmmDesktop/Pages/TasksPage.xaml/.cs` | Queue list + cancel (real FFI) |
| `WdmmDesktop/Pages/CastPage.xaml/.cs` | DLNA discovery + proxy cast; locked rules enforced |
| `WdmmDesktop/Pages/AboutPage.xaml/.cs` | **UPDATED** — parity matrix reflects Folders/Playlists/Manga + HLS deferred |
| `WdmmDesktop/Dialogs/DmrInfoDialog.xaml/.cs` | Per-DMR info dialog (USN, location, compat) |
| `WdmmDesktop/Dialogs/SettingsDialog.xaml/.cs` | Rotate Token button (wdmm_token_rotate) |
| `WdmmDesktop/MainWindow.xaml` | Cast nav item (E93E glyph) |
| `WdmmDesktop/MainWindow.xaml.cs` | Real page routing; library_init on startup; SelectNavItem() |
| `WdmmDesktop/App.xaml.cs` | Exposed static MainWindow ref for cross-page nav |
| `WdmmDesktop/Models/ContentDeliveryKind.cs` | **NEW** — `ContentDeliveryKind` enum + `ContentDeliveryKindHelper.Infer` (mirrors Core @ e236ece) |
| `WdmmDesktop/Generated/NativeMethods.g.cs` | **UPDATED** — HLS/stream P/Invoke + safe wrappers (replaces deferred stubs) |
| `WdmmDesktop/Pages/CastPage.xaml/.cs` | **UPDATED** — HLS unlocked; ContentDeliveryKind cast path; LocalStream hook; NetworkDirect refuse |
| `WdmmDesktop/Models/DmrDevice.cs` | **UPDATED** — comments reflect HLS live; `_activeTokenIsHls` mutex docs |
| `WdmmDesktop/Pages/AboutPage.xaml/.cs` | **UPDATED** — parity matrix reflects HLS live; LocalStream/NetworkDirect status |
| `docs/DESKTOP-PARITY.md` | **UPDATED** — this file; HLS section + FFI matrix + submodule SHA + Wave C multi-provider |

---

## Remaining Blockers

| Blocker | Unblocked by |
|---|---|
| Playlists direct FFI | **DONE** — `wdmm_playlist_*` |
| Manga library | **DONE** — read + upsert/set_progress/delete |
| Cast HLS mode (`wdmm_proxy_register_hls`) | **DONE** — wired in P0.5 (wdmm-core e236ece) |
| Backup archive FFI | **DONE** — Settings Sync tab + Core `9d01e09` |
| DMR prefs Core store | **DONE** — `dmr_prefs.db` + CastDialog |
| LocalStream cast (`wdmm_proxy_register_stream`) | Bindings + hook present; needs desktop cloud/crypt driver |
| Sidebar animation | Polish (DoubleAnimation on SidebarColumn.Width) — not parity-blocking |
| Sidebar state persistence | Persist last destination + expand/collapse across launches |

---

## Settings / Backup / DMR (2026-10-25)

- Core `abc1f4a` (Wave C + v6 tombstones + sync_state): `wdmm_driver_*`, `wdmm_account_list_dir`, `wdmm_account_*`, `wdmm_backup_*` (WDMMBK01/WDMMEN01, PBKDF2 120k), independent `dmr_prefs.db` + `wdmm_dmr_prefs_*`, `wdmm_deleted_track_*`, `wdmm_sync_state_*`. Core now dual-reads Android binary BK on restore (Desktop inherits via FFI).
- Desktop: AccountStore; Settings TabView Sync+Backup wired; Cast DMR prefs via FFI (json migrate); Manga write UI; **File source multi-provider via Core account FFI (webdav, baidu_netdisk, 123_open, 115open, aliyundrive_open, terabox, netease_music, ctfile, crypt)**; crypt nested source with real credentials from resolved account.
- Still out of scope / true blockers: **LocalStream cast needs desktop cloud/crypt drivers (CloudFfi account operations available, but cast glue incomplete)**; local-lib folder mount extras (post-refactor).

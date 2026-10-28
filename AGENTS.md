# WDMM-desktop — Agent Instructions

## Repository role

WDMM-desktop is the WinUI 3 (C#) GUI layer for WDMM. It couples to WDMM-core via
source-level FFI (csbindgen / P-Invoke). OpenAPI is optional (settings toggle).

## CloudDriver is the single authority for cloud file ops

All in-app cloud file operations go through **`CloudDriver`** in WDMM-core. This
is the "virtual file layer" in product language — there is no second trait.

- Sync, backup, browse: call `CloudDriver` via FFI (`wdmm_sync_trigger`,
  `wdmm_sync_full`, `wdmm_backup_*`, etc.). Never reach past it into raw HTTP.
- **Do not** introduce a `VirtualFs`, `ObjectFs`, or duplicate file-access layer.
- Sync FFI takes `account_id + remote_dir (+ with_covers)` — never `url/user/pass`.
  Backup FFI retains `url/user/pass` until explicitly cut in Core.
- Missing put/get on a driver → driver error. No silent WebDAV-only fallback.

## FFI coupling

- Native bindings: `WdmmDesktop/Generated/NativeMethods.g.cs` (csbindgen output).
- Submodule: `rust/wdmm-core` — always bump to the target Core SHA in the same commit that updates callers.
- Build: `dotnet build WdmmDesktop.sln`

## One-shot coding rule (mandatory)

Finish the new authority completely, then delete the old path and wire callers. No
feature flags, no dual-write/dual-read, no soft fallbacks. See `CLAUDE.md`.

## Verification

After any FFI update: build solution and confirm no C# compiler errors.
`dotnet build WdmmDesktop.sln`

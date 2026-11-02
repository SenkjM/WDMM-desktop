# WDMM-desktop — Agent Instructions

## Repository role

WDMM-desktop is the WinUI 3 (C#) GUI layer for WDMM. It couples to WDMM-core via
source-level FFI (csbindgen / P-Invoke). OpenAPI is optional (settings toggle).

## WDMM-VFS is the single authority for cloud file ops

All in-app cloud file operations go through **`WdmmVfs`** in WDMM-core (product name
**WDMM-VFS** — formerly `CloudDriver`). This is the "virtual file layer" in product
language — there is no second trait.

- Sync, backup, browse: call `WdmmVfs` via FFI (`wdmm_sync_trigger`,
  `wdmm_sync_full`, `wdmm_backup_*`, etc.). Never reach past it into raw HTTP.
- **Do not** introduce a `VirtualFs`, `ObjectFs`, or duplicate file-access layer.
- Sync FFI takes `account_id + remote_dir (+ with_covers)` — never `url/user/pass`.
  Backup FFI retains `url/user/pass` until explicitly cut in Core.
- Missing put/get on a driver → driver error. No silent WebDAV-only fallback.

## FFI coupling

- Native bindings: `WdmmDesktop/Generated/NativeMethods.g.cs` (csbindgen output).
- Submodule: `rust/wdmm-core` — always bump to the target Core SHA in the same commit
  that updates callers.
- Build Rust FFI: `cargo build --release --package wdmm-ffi` (inside `rust/wdmm-core`).
- Generate C# bindings: `csbindgen --input target/release/wdmm_ffi.dll --output WdmmDesktop/Generated/NativeMethods.g.cs`

## Build and test

```bash
# Build the solution
dotnet build WdmmDesktop.sln

# Run tests (if a test project is added)
dotnet test WdmmDesktop.sln
```

Smoke-test checklist after build: project opens in Visual Studio 2022, solution builds
(`Ctrl+Shift+B`), window launches without crash, settings UI renders.

## Branch and commit conventions

- Active branch: `refactor/rust-core`
- Commit prefixes: `feat:`, `fix:`, `refactor:`, `docs:`, `test:`, `chore:`
- Do not commit to `main` directly. No force-push. No triggering CI via API.
- Submodule bump rule: pin `rust/wdmm-core` to the target Core SHA in the same commit
  that updates C# callers.

## CI workflows

`.github/workflows/testbuild.yml` — full solution build check (no signing) on push/PR.

## Where docs live

- Rust Core API and constraint notes: `rust/wdmm-core/crates/*/docs/API.md` (via submodule)
- Agent instructions: this file (`AGENTS.md`)

## One-shot coding rule (mandatory)

Finish the new authority completely, then delete the old path and wire callers. No
feature flags, no dual-write/dual-read, no soft fallbacks. See `CLAUDE.md`.

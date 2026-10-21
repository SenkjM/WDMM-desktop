# WDMM-desktop — Windows WinUI 3 Frontend

**WebDAV Media Manager** Windows GUI using **WinUI 3** (Windows App SDK 1.6). Communicates with [WDMM-core](https://github.com/SenkjM/WDMM-core) Rust logic via **csbindgen-generated P/Invoke bindings** (primary path) or optional OpenAPI (localhost).

**Smoke-test target**: project opens in Visual Studio 2022, window launches without crash, settings UI renders. Full feature parity with Android is not required (per `saf_work/wdmm-split-impl-brief.md §B`).

---

## Project Structure

```
WdmmDesktop/
├── WdmmDesktop.csproj          # .NET 8 + Windows App SDK 1.6 + WinUI 3
├── App.xaml / App.xaml.cs      # Application entry point
├── MainWindow.xaml / .cs       # Main window + OpenAPI toggle
├── app.manifest                # DPI awareness + Win10/11 compat
├── Assets/                     # Images / resources (empty for now)
└── Generated/                  # csbindgen output (NativeMethods.g.cs) goes here
```

---

## Prerequisites

- **Visual Studio 2022** (17.8 or newer) with:
  - ".NET Desktop Development" workload
  - "Windows App SDK C# Templates" component
  - "Windows 10 SDK (10.0.19041.0)" or newer
- **.NET 8 SDK** (included with VS 2022 17.8+)
- **Rust toolchain** (for building `wdmm-ffi` from WDMM-core)

---

## Build Instructions

### 1. Clone Sibling Repos

```bash
cd /your/projects
git clone https://github.com/SenkjM/WDMM-desktop.git
git clone https://github.com/SenkjM/WDMM-core.git
```

Expected layout:
```
/your/projects/
├── WDMM-core/
└── WDMM-desktop/
```

### 2. Build Rust Core + Generate FFI Bindings

```bash
cd ../WDMM-core
cargo build --release --package wdmm-ffi
```

This produces `target/release/wdmm_ffi.dll` (and `.lib` for linking).

### 3. Generate C# Bindings with csbindgen

Install `csbindgen` (if not already):

```bash
cargo install csbindgen
```

Generate bindings:

```bash
csbindgen --input target/release/wdmm_ffi.dll \
          --output ../WDMM-desktop/WdmmDesktop/Generated/NativeMethods.g.cs \
          --namespace WdmmDesktop.Generated
```

**Alternative**: manually write P/Invoke wrappers in `Generated/NativeMethods.g.cs` referencing the C ABI from `wdmm-ffi/src/lib.rs`:

```csharp
using System;
using System.Runtime.InteropServices;

namespace WdmmDesktop.Generated
{
    [StructLayout(LayoutKind.Sequential)]
    public struct FfiString
    {
        public IntPtr ptr;
    }

    public static class NativeMethods
    {
        const string DLL = "wdmm_ffi.dll";

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern FfiString wdmm_version();

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern int wdmm_openapi_status();

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern FfiString wdmm_hello();

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern void wdmm_ffi_free_string(FfiString s);
    }
}
```

### 4. Copy DLL to Output Directory

Place `wdmm_ffi.dll` in the same directory as the built `.exe` so the runtime can find it:

- **Option A** (manual): copy `../WDMM-core/target/release/wdmm_ffi.dll` to `WdmmDesktop/bin/x64/Debug/` after each Rust rebuild.
- **Option B** (MSBuild post-build event): add to `.csproj`:

```xml
<Target Name="CopyRustDll" AfterTargets="Build">
  <Copy SourceFiles="$(SolutionDir)..\..\WDMM-core\target\release\wdmm_ffi.dll"
        DestinationFolder="$(OutDir)" />
</Target>
```

### 5. Open in Visual Studio & Build

```bash
cd ../WDMM-desktop
start WdmmDesktop.sln
```

(If `.sln` doesn't exist, open `WdmmDesktop/WdmmDesktop.csproj` directly in VS.)

**Build**: `Ctrl+Shift+B` or menu *Build → Build Solution*.

**Run**: `F5` (Debug) or `Ctrl+F5` (Release without debugger).

### 6. Smoke Test

- Window should launch with title "WDMM Desktop".
- Status text should show "Ready (FFI not yet hooked)" or "WDMM Core v0.1.0" if FFI is connected.
- Settings stub displays: Bind Mode dropdown, Port textbox, Token display (read-only).
- "Start / Stop" button shows alert "OpenAPI toggle not yet implemented".

✅ **Smoke passed** = window launches without crash; UI renders correctly.

---

## FFI Hookup Notes

### Load Path Convention

C# P/Invoke searches:
1. Same directory as `.exe`
2. System PATH
3. `runtimeconfig.json` additional probing paths

**Recommendation**: keep `wdmm_ffi.dll` in the output directory. For packaging (MSIX), include it as `Content` in `.csproj`:

```xml
<ItemGroup>
  <Content Include="..\..\..\WDMM-core\target\release\wdmm_ffi.dll">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </Content>
</ItemGroup>
```

### csbindgen Output

`csbindgen` generates:
- Struct wrappers for FFI types (`FfiString`)
- P/Invoke `[DllImport]` declarations for all `#[no_mangle] extern "C"` functions in Rust

Place generated file in `WdmmDesktop/Generated/NativeMethods.g.cs` and reference it in `MainWindow.xaml.cs`.

### Memory Management

`wdmm_ffi` returns owned strings via `FfiString { ptr: *mut c_char }`. **Always call `wdmm_ffi_free_string()`** after reading:

```csharp
var version = NativeMethods.wdmm_version();
var versionStr = Marshal.PtrToStringAnsi(version.ptr);
// Use versionStr...
NativeMethods.wdmm_ffi_free_string(version);
```

Failing to free causes memory leaks.

### Cross-Compile Limitations

If Rust cross-compile to `x86_64-pc-windows-msvc` isn't available in your environment:
- Use a Windows VM / GitHub Actions runner to build `wdmm_ffi.dll`.
- Or manually write minimal P/Invoke stubs (as shown above) and test with a pre-built DLL from another machine.

---

## Current Status (First Bootstrap)

- ✅ WinUI 3 project structure complete
- ✅ App + MainWindow XAML stubs
- ✅ Settings page UI stub (bind mode / port / token display)
- ✅ csbindgen integration notes
- ✅ FFI hello/version export stubs in WDMM-core
- ⚠️ OpenAPI start/stop logic **not yet implemented** (button is placeholder)
- ⚠️ No actual account management / library browsing UI yet (future rounds)

**Known gaps**: Per brief, full feature parity with Android is not required this round. Goal is "smoke test passes + entry points documented."

---

## References

- **Rust Core**: [WDMM-core](https://github.com/SenkjM/WDMM-core)
- **Constraints**: `../WDMM-core/docs/CONSTRAINTS.md`
- **Brief**: `/workspace/saf_work/wdmm-split-impl-brief.md §B`
- **csbindgen**: https://github.com/Cysharp/csbindgen
- **Windows App SDK Docs**: https://learn.microsoft.com/windows/apps/windows-app-sdk/

---

License: **Apache-2.0** (per existing LICENSE file)

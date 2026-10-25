using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using WdmmDesktop.Services;

namespace WdmmDesktop
{
    public partial class App : Application
    {
        /// <summary>
        /// The single application window. Exposed so pages can reach the main
        /// window to request sidebar selection changes (e.g. MusicPage → CastPage).
        /// </summary>
        public static MainWindow? MainWindow { get; private set; }

        // P/Invoke: add a directory to the native DLL search path.
        // BOOL SetDllDirectoryW(LPCWSTR lpPathName)
        // After this call the search order becomes:
        //   1) exe directory  2) lpPathName  3) system dirs  4) PATH
        // so wdmm_ffi.dll in bin\lib\ is found without touching PATH.
        [DllImport("kernel32", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetDllDirectoryW(string lpPathName);

        public App()
        {
            // ── Portable DLL path setup ───────────────────────────────────────
            // In the portable zip, wdmm_ffi.dll lives in bin\lib\ (not beside
            // the exe) to keep the bin\ root clean.  Call SetDllDirectoryW
            // before InitializeComponent so the very first P/Invoke into
            // wdmm_ffi resolves correctly no matter where the zip is extracted.
            //
            // When lib\ does not exist (flat dev build, DLL beside the exe)
            // we skip the call — wdmm_ffi.dll is found in the exe directory
            // via the default DLL search order.
            var exeDir = Path.GetDirectoryName(Environment.ProcessPath)
                         ?? AppContext.BaseDirectory;
            var libDir = Path.Combine(exeDir, "lib");
            if (Directory.Exists(libDir))
                SetDllDirectoryW(libDir);

            InitializeComponent();
        }

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            try { AccountStore.MigrateFromLocalJsonIfNeeded(); }
            catch { /* best-effort legacy migrate */ }
            MainWindow = new MainWindow();
            MainWindow.Activate();
        }
    }
}

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WdmmDesktop.Models;

namespace WdmmDesktop.Drivers
{
    /// <summary>
    /// Stub driver registry for provider-independent cloud operations.
    /// This class is never called in practice — Desktop uses <see cref="Services.CloudFfi"/>
    /// wdmm_driver_* FFI bindings for all driver operations.
    /// </summary>
    internal static class DriverRegistry
    {
        public static List<CloudFileItem> List(string providerType, string configJson, string path)
        {
            throw new System.NotImplementedException("Desktop uses CloudFfi.DriverList");
        }

        public static void Mkdir(string providerType, string configJson, string path)
        {
            throw new System.NotImplementedException("Desktop uses CloudFfi.DriverMkdir");
        }

        public static void Rename(string providerType, string configJson, string oldPath, string newPath)
        {
            throw new System.NotImplementedException("Desktop uses CloudFfi.DriverRename");
        }

        public static void Remove(string providerType, string configJson, string path)
        {
            throw new System.NotImplementedException("Desktop uses CloudFfi.DriverRemove");
        }

        public static CloudFileItem Get(string providerType, string configJson, string path)
        {
            throw new System.NotImplementedException("Desktop uses CloudFfi.DriverGet");
        }
    }
}

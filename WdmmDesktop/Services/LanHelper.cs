using System;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace WdmmDesktop.Services
{
    /// <summary>
    /// Windows example platform adapter — LAN IP selection for DLNA cast.
    ///
    /// Mirrors the Android <c>_getLocalIpFor(targetIp)</c> in
    /// <c>dlna_cast_service.dart</c>: picks the local NIC address that shares a
    /// /24 subnet with the target DLNA renderer.  The result is passed to the
    /// Rust proxy as bind_mode = <c>"lan_if:&lt;ip&gt;"</c> so the proxy binds
    /// on the correct LAN interface and its cast URL is reachable by the renderer.
    ///
    /// This file is intentionally thin — it is the D1 (PLAN.md) boundary example
    /// showing what belongs in the platform layer (NIC enumeration) vs. core
    /// (proxy bind / DLNA control).
    ///
    /// Note: <c>NativeMethods.wdmm_proxy_start</c> in the generated bindings
    /// currently takes <c>(ushort port)</c>; once those bindings are regenerated
    /// after the Android B3.b/B3.d Core changes (<c>bind_mode_cstr</c> param),
    /// pass <c>GetBindModeFor(rendererHost)</c> as the first argument.  Until
    /// then the Windows proxy always binds loopback — this helper documents the
    /// gap and is ready to wire.
    /// </summary>
    public static class LanHelper
    {
        /// <summary>
        /// Returns the local IPv4 address that shares a /24 subnet with
        /// <paramref name="targetIp"/>, or <c>null</c> if none is found.
        ///
        /// Falls back to the first non-loopback IPv4 address when no /24 match
        /// exists (e.g. renderer is on a different subnet).
        /// </summary>
        /// <param name="targetIp">IPv4 address of the DLNA renderer.</param>
        public static string? GetLocalIpFor(string targetIp)
        {
            if (!IPAddress.TryParse(targetIp, out var target))
                return null;
            if (target.AddressFamily != AddressFamily.InterNetwork)
                return null;

            var targetBytes = target.GetAddressBytes();  // 4 bytes
            string? fallback = null;

            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up)
                    continue;
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    continue;

                var props = nic.GetIPProperties();
                foreach (var uni in props.UnicastAddresses)
                {
                    if (uni.Address.AddressFamily != AddressFamily.InterNetwork)
                        continue;

                    var localBytes = uni.Address.GetAddressBytes();
                    fallback ??= uni.Address.ToString();

                    // /24 match: first three octets equal.
                    if (localBytes[0] == targetBytes[0] &&
                        localBytes[1] == targetBytes[1] &&
                        localBytes[2] == targetBytes[2])
                    {
                        return uni.Address.ToString();
                    }
                }
            }

            return fallback;
        }

        /// <summary>
        /// Returns the Rust proxy bind_mode string to use for casting to
        /// <paramref name="rendererHost"/>.
        ///
        /// <list type="bullet">
        ///   <item><c>"lan_if:&lt;ip&gt;"</c> — a matching LAN NIC was found</item>
        ///   <item><c>"loopback"</c>        — no LAN NIC found (cast will fail on remote renderers)</item>
        /// </list>
        /// </summary>
        public static string GetBindModeFor(string rendererHost)
        {
            var ip = GetLocalIpFor(rendererHost);
            return ip is not null ? $"lan_if:{ip}" : "loopback";
        }
    }
}

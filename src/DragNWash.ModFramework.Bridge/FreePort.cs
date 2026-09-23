using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace DragNWash.ModFramework.Bridge
{
    // Finds a port the Bridge can listen on when its own was refused, for the
    // tab's "Use a free port". Everything here stays on 127.0.0.1, as the
    // Bridge does, and changes nothing on the system: it only asks.
    internal static class FreePort
    {
        internal const int Tries = 200;

        // The first port after `from` the game may listen on, or 0 when none
        // of the next Tries is. Slow enough (netsh, a bind per port) to belong
        // on a worker thread.
        internal static int After(int from)
        {
            List<int[]> excluded = ExcludedRanges();
            for (int port = from + 1, n = 0; port <= 65535 && n < Tries; port++, n++)
            {
                if (Excluded(excluded, port)) continue;
                if (CanListen(port)) return port;
            }
            return 0;
        }

        private static bool Excluded(List<int[]> ranges, int port)
        {
            foreach (int[] r in ranges)
            {
                if (port >= r[0] && port <= r[1]) return true;
            }
            return false;
        }

        // Windows' excluded port ranges ("netsh interface ipv4 show
        // excludedportrange protocol=tcp", which needs no administrator).
        // A bind already fails in the ranges Windows took for Hyper-V, WSL or
        // Docker, but not in the ones an administrator set aside for some
        // program (tried on Windows 11: a bind there goes through), and the
        // Bridge must not take those either. The table's lines are two numbers
        // in any language; the header and the footnote are skipped. Empty off
        // Windows, or when netsh can't be asked, and then the bind decides.
        private static List<int[]> ExcludedRanges()
        {
            var ranges = new List<int[]>();
            if (Environment.OSVersion.Platform != PlatformID.Win32NT) return ranges;
            try
            {
                string netsh = Path.Combine(Environment.SystemDirectory, "netsh.exe");
                if (!File.Exists(netsh)) return ranges;
                // No socket of the Bridge's is open to be inherited: this only
                // runs while it can't listen.
                var info = new ProcessStartInfo(netsh, "interface ipv4 show excludedportrange protocol=tcp")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true,
                };
                using (Process process = Process.Start(info))
                {
                    var read = process.StandardOutput.ReadToEndAsync();
                    if (!read.Wait(5000))
                    {
                        try { process.Kill(); } catch { }
                        BridgePlugin.Log.LogDebug("[bridge] netsh did not answer; looking for a port by binding alone.");
                        return ranges;
                    }
                    foreach (string line in read.Result.Split('\n'))
                    {
                        Match m = Regex.Match(line, @"^\s*(\d+)\s+(\d+)\s*\*?\s*$");
                        if (m.Success && int.TryParse(m.Groups[1].Value, out int start) && int.TryParse(m.Groups[2].Value, out int end))
                        {
                            ranges.Add(new[] { start, end });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                BridgePlugin.Log.LogDebug($"[bridge] Could not read Windows' excluded ports ({ex.Message}); looking for a port by binding alone.");
            }
            return ranges;
        }

        // Listens on the port for a moment, on the loopback address only, and
        // lets it go. A failed bind (set aside by Windows, or taken) is simply
        // a port to skip.
        private static bool CanListen(int port)
        {
            var probe = new TcpListener(IPAddress.Loopback, port);
            try
            {
                // On Windows: refused too while any program holds the port on
                // any address, not only on 127.0.0.1.
                if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                {
                    try { probe.ExclusiveAddressUse = true; } catch (Exception) { }
                }
                probe.Start();
                return true;
            }
            catch (SocketException)
            {
                return false;
            }
            finally
            {
                try { probe.Stop(); } catch { }
            }
        }
    }
}

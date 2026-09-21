using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace DragNWash.ModFramework.Bridge
{
    // The one token AI clients send (Authorization: Bearer <token>): 32 random
    // bytes, kept in the user's own profile (LocalApplicationData), not in the
    // game folder, so other accounts on the computer cannot read it.
    internal static class BridgeToken
    {
        private static string _token;

        internal static string FilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DragNWash ModFramework", "bridge-token.txt");

        internal static string Value
        {
            get
            {
                if (_token != null) return _token;
                try
                {
                    if (File.Exists(FilePath))
                    {
                        string saved = File.ReadAllText(FilePath).Trim();
                        if (saved.Length >= 32) return _token = saved;
                    }
                }
                catch (Exception ex)
                {
                    BridgePlugin.Log.LogWarning($"[bridge] Could not read the token file: {ex.Message}; making a new token.");
                }
                return Renew();
            }
        }

        // A new token; every client must be set up again.
        internal static string Renew()
        {
            var bytes = new byte[32];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
            _token = Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllText(FilePath, _token);
                OwnerOnly(FilePath);
            }
            catch (Exception ex)
            {
                BridgePlugin.Log.LogWarning($"[bridge] Could not write the token file: {ex.Message}; the token lasts this session only.");
            }
            return _token;
        }

        [DllImport("libc", SetLastError = true)]
        private static extern int chmod(string path, uint mode);

        // On Windows the file is in the user's profile, which no other account
        // can read. On Linux and the Steam Deck the profile is not private by
        // itself - a file lands at 644 - so the token is set to the owner's own
        // read and write (600), and nobody else on the machine can pick it up.
        private static void OwnerOnly(string path)
        {
            PlatformID kind = Environment.OSVersion.Platform;
            if (kind != PlatformID.Unix && kind != PlatformID.MacOSX)
            {
                return;
            }
            try
            {
                if (chmod(path, 0x180) != 0)   // 0600
                {
                    BridgePlugin.Log.LogDebug("[bridge] Could not set the token file to the owner's own; it is readable by this computer's other users.");
                }
            }
            catch (Exception ex)
            {
                BridgePlugin.Log.LogDebug($"[bridge] Could not set the token file's permissions: {ex.Message}");
            }
        }

        // Compares in constant time, so the time taken says nothing about the token.
        internal static bool Matches(string given)
        {
            string expected = Value;
            if (given == null) return false;
            int diff = given.Length ^ expected.Length;
            for (int i = 0; i < expected.Length; i++)
            {
                diff |= (i < given.Length ? given[i] : 0) ^ expected[i];
            }
            return diff == 0;
        }
    }
}

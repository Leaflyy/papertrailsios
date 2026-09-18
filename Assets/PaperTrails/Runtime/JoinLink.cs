using System;

namespace PaperTrails
{
    // Shareable join links: papertrails://join?code=ABC123 (Relay) or
    // papertrails://join?ip=192.168.1.10 (LAN). Pure logic, no Unity
    // dependency: covered by tests. The OS handoff (intent-filter on
    // Android, URL scheme on iOS) lives in the manifest/plist tooling.
    public static class JoinLink
    {
        public const string Scheme = "papertrails";
        public static string RelayLink(string code) => "papertrails://join?code=" + (code ?? "").Trim().ToUpperInvariant();
        public static string LanLink(string address) => "papertrails://join?ip=" + (address ?? "").Trim();
        public static bool TryParse(string url, out bool online, out string target)
        {
            online = true; target = null;
            if (string.IsNullOrEmpty(url)) return false;
            string u = url.Trim();
            if (!u.StartsWith("papertrails://", StringComparison.OrdinalIgnoreCase)) return false;
            int q = u.IndexOf('?');
            if (q < 0 || q == u.Length - 1) return false;
            foreach (string part in u.Substring(q + 1).Split('&'))
            {
                int eq = part.IndexOf('=');
                if (eq <= 0) continue;
                string key = part.Substring(0, eq).Trim().ToLowerInvariant();
                string val = part.Substring(eq + 1).Trim();
                if (key == "code" && IsCode(val)) { online = true; target = val.ToUpperInvariant(); return true; }
                if (key == "ip" && IsAddress(val)) { online = false; target = val; return true; }
            }
            return false;
        }
        static bool IsCode(string v)
        {
            if (v == null || v.Length < 4 || v.Length > 12) return false;
            foreach (char c in v) if (!char.IsLetterOrDigit(c)) return false;
            return true;
        }
        static bool IsAddress(string v)
        {
            if (string.IsNullOrEmpty(v) || v.Length > 15) return false;
            string[] quad = v.Split('.');
            if (quad.Length != 4) return false;
            foreach (string p in quad) if (!byte.TryParse(p, out _)) return false;
            return true;
        }
    }
}

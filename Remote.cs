using System;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace Relais
{
    /// <summary>
    /// Alertes envoyées sur le téléphone : webhook Discord (salon privé) et/ou ntfy (appli gratuite, sans compte).
    /// Les envois partent en arrière-plan pour ne jamais bloquer Relais.
    /// </summary>
    public static class Remote
    {
        [StructLayout(LayoutKind.Sequential)]
        struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }

        [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

        static Remote()
        {
            try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; } catch { } // TLS 1.2
        }

        /// <summary>Temps depuis la dernière action clavier/souris sur le PC.</summary>
        public static TimeSpan Idle
        {
            get
            {
                try
                {
                    LASTINPUTINFO li = new LASTINPUTINFO();
                    li.cbSize = (uint)Marshal.SizeOf(typeof(LASTINPUTINFO));
                    if (!GetLastInputInfo(ref li)) return TimeSpan.Zero;
                    uint ms = unchecked((uint)Environment.TickCount - li.dwTime);
                    return TimeSpan.FromMilliseconds(ms);
                }
                catch { return TimeSpan.Zero; }
            }
        }

        public static bool Configured(Settings s)
        {
            return !string.IsNullOrEmpty(s.DiscordWebhook) || !string.IsNullOrEmpty(s.NtfyTopic);
        }

        /// <summary>Envoie si configuré et (option) si tu es absent du PC.</summary>
        public static void Send(Settings s, string title, string text, bool force)
        {
            if (!Configured(s)) return;
            if (!force && s.RemoteOnlyWhenAway && Idle.TotalMinutes < s.AwayMinutes) return;
            string discord = s.DiscordWebhook, ntfy = s.NtfyTopic;
            ThreadPool.QueueUserWorkItem(delegate
            {
                if (!string.IsNullOrEmpty(discord)) Try("Discord", delegate { PostDiscord(discord, title, text); });
                if (!string.IsNullOrEmpty(ntfy)) Try("ntfy", delegate { PostNtfy(ntfy, title, text); });
            });
        }

        static void Try(string what, Action a)
        {
            try { a(); }
            catch (Exception ex) { Program.Log("Alerte " + what + " : " + ex.Message); }
        }

        static string JsonEscape(string s)
        {
            StringBuilder sb = new StringBuilder();
            foreach (char c in s ?? "")
            {
                if (c == '"') sb.Append("\\\"");
                else if (c == '\\') sb.Append("\\\\");
                else if (c == '\n') sb.Append("\\n");
                else if (c == '\r') { }
                else if (c < 0x20) sb.Append(' ');
                else sb.Append(c);
            }
            return sb.ToString();
        }

        static void PostDiscord(string url, string title, string text)
        {
            string body = "{\"username\":\"Relais\",\"content\":\"**" + JsonEscape(title) + "** — " + JsonEscape(text) + "\"}";
            Post(url, body, "application/json", null);
        }

        static void PostNtfy(string topic, string title, string text)
        {
            string url = topic.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? topic : "https://ntfy.sh/" + topic.Trim().Trim('/');
            // en-tête Title en ASCII (les accents passent dans le corps du message)
            string asciiTitle = Ascii(title);
            Post(url, text, "text/plain; charset=utf-8", asciiTitle);
        }

        static string Ascii(string s)
        {
            string n = (s ?? "").Normalize(NormalizationForm.FormD);
            StringBuilder sb = new StringBuilder();
            foreach (char c in n) if (c < 128) sb.Append(c);
            return sb.ToString();
        }

        static void Post(string url, string body, string contentType, string ntfyTitle)
        {
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = "POST";
            req.ContentType = contentType;
            req.UserAgent = "Relais";
            req.Timeout = 10000;
            if (ntfyTitle != null) { req.Headers["Title"] = ntfyTitle; req.Headers["Tags"] = "video_game"; }
            byte[] data = Encoding.UTF8.GetBytes(body);
            req.ContentLength = data.Length;
            using (Stream s = req.GetRequestStream()) s.Write(data, 0, data.Length);
            using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse()) { }
        }

        /// <summary>Envoi de test, résultat rapporté sur le fil UI.</summary>
        public static void Test(Settings s, Action<string> done)
        {
            string discord = s.DiscordWebhook, ntfy = s.NtfyTopic;
            SynchronizationContext ui = SynchronizationContext.Current;
            ThreadPool.QueueUserWorkItem(delegate
            {
                StringBuilder r = new StringBuilder();
                if (string.IsNullOrEmpty(discord) && string.IsNullOrEmpty(ntfy)) r.Append("Renseigne un webhook Discord ou un sujet ntfy.");
                if (!string.IsNullOrEmpty(discord))
                {
                    try { PostDiscord(discord, "Relais", "Test réussi : les alertes arriveront ici."); r.Append("Discord : OK. "); }
                    catch (Exception ex) { r.Append("Discord : échec (" + ex.Message + "). "); }
                }
                if (!string.IsNullOrEmpty(ntfy))
                {
                    try { PostNtfy(ntfy, "Relais", "Test réussi : les alertes arriveront ici."); r.Append("ntfy : OK."); }
                    catch (Exception ex) { r.Append("ntfy : échec (" + ex.Message + ")."); }
                }
                string msg = r.ToString();
                if (ui != null) ui.Post(delegate { done(msg); }, null); else done(msg);
            });
        }
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Threading;
using System.Windows.Forms;

namespace Relais
{
    /// <summary>
    /// Mises à jour via les « Releases » d'un dépôt GitHub (ex. « tonpseudo/relais »).
    /// La release doit contenir un fichier Relais.exe (le workflow GitHub Actions fourni s'en charge).
    /// </summary>
    public static class Updater
    {
        public sealed class Info
        {
            public Version Version;
            public string Tag, ExeUrl, Notes, Page;
        }

        static Updater()
        {
            try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; } catch { }
        }

        public sealed class FriendlyException : Exception
        {
            public FriendlyException(string m) : base(m) { }
        }

        static bool Is404(WebException ex)
        {
            HttpWebResponse r = ex.Response as HttpWebResponse;
            return r != null && r.StatusCode == HttpStatusCode.NotFound;
        }

        public static Version Current { get { return typeof(Updater).Assembly.GetName().Version; } }

        static string Get(string url)
        {
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = "Relais-Updater";
            req.Accept = "application/vnd.github+json";
            req.Timeout = 15000;
            using (WebResponse resp = req.GetResponse())
            using (StreamReader r = new StreamReader(resp.GetResponseStream()))
                return r.ReadToEnd();
        }

        public static Version ParseTag(string tag)
        {
            string t = (tag ?? "").Trim().TrimStart('v', 'V');
            int cut = t.IndexOfAny(new char[] { '-', '+', ' ' });
            if (cut > 0) t = t.Substring(0, cut);
            Version v;
            if (!Version.TryParse(t, out v)) return null;
            return new Version(v.Major, v.Minor, Math.Max(0, v.Build), Math.Max(0, v.Revision));
        }

        /// <summary>Interroge GitHub (bloquant — à appeler hors du fil UI).</summary>
        public static Info Fetch(string repo)
        {
            repo = (repo ?? "").Trim().Trim('/');
            if (repo.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase)) repo = repo.Substring(19).Trim('/');
            if (repo.Split('/').Length != 2) throw new FormatException("Dépôt invalide : écris « pseudo/depot ».");
            Dictionary<string, object> d = null;
            try
            {
                d = Settings.Json().Deserialize<Dictionary<string, object>>(Get("https://api.github.com/repos/" + repo + "/releases/latest"));
            }
            catch (WebException ex)
            {
                if (!Is404(ex)) throw;
                // 404 : soit le dépôt n'existe pas / est privé, soit il n'a encore aucune release publiée
                try { Get("https://api.github.com/repos/" + repo); }
                catch (WebException ex2)
                {
                    if (Is404(ex2)) throw new FriendlyException("Dépôt « " + repo + " » introuvable.\n\nVérifie l'orthographe exacte (pseudo/depot, comme dans l'adresse github.com/pseudo/depot) et que le dépôt est bien PUBLIC (Settings > General > Danger Zone > Change visibility).");
                    throw;
                }
                System.Collections.ArrayList all = Settings.Json().Deserialize<System.Collections.ArrayList>(Get("https://api.github.com/repos/" + repo + "/releases?per_page=10"));
                foreach (object o in all)
                {
                    Dictionary<string, object> r = o as Dictionary<string, object>;
                    if (r != null && !(r.ContainsKey("draft") && r["draft"] is bool && (bool)r["draft"])) { d = r; break; }
                }
                if (d == null) throw new FriendlyException("Le dépôt « " + repo + " » existe, mais aucune Release n'est encore publiée.\n\nSur GitHub : Releases > Draft a new release > tag « v2.0.1 » > Publish release. Le workflow y ajoutera Relais.exe quelques minutes plus tard (onglet Actions).");
            }
            Info i = new Info();
            i.Tag = d.ContainsKey("tag_name") ? d["tag_name"] as string : null;
            i.Version = ParseTag(i.Tag);
            i.Notes = d.ContainsKey("body") ? d["body"] as string : "";
            i.Page = d.ContainsKey("html_url") ? d["html_url"] as string : "";
            object assets;
            if (d.TryGetValue("assets", out assets) && assets is System.Collections.ArrayList)
                foreach (object o in (System.Collections.ArrayList)assets)
                {
                    Dictionary<string, object> a = o as Dictionary<string, object>;
                    if (a == null) continue;
                    string name = a["name"] as string;
                    if (string.Equals(name, "Relais.exe", StringComparison.OrdinalIgnoreCase))
                        i.ExeUrl = a["browser_download_url"] as string;
                }
            return i;
        }

        /// <summary>Vérification automatique (une fois par jour) ou manuelle.</summary>
        public static void CheckInBackground(App app, bool manual)
        {
            Settings s = app.S;
            if (string.IsNullOrEmpty(s.UpdateRepo))
            {
                if (manual) MessageBox.Show(app.Main, "Indique d'abord ton dépôt GitHub (ex. tonpseudo/relais) dans Options > Mises à jour.", "Relais");
                return;
            }
            if (!manual && (!s.CheckUpdates || s.LastUpdateCheck == App.Today)) return;
            string repo = s.UpdateRepo;
            SynchronizationContext ui = SynchronizationContext.Current;
            ThreadPool.QueueUserWorkItem(delegate
            {
                Info info = null; string error = null;
                try { info = Fetch(repo); }
                catch (FriendlyException ex) { error = ex.Message; }
                catch (WebException ex)
                {
                    HttpWebResponse r = ex.Response as HttpWebResponse;
                    error = r != null && (int)r.StatusCode == 403
                        ? "GitHub limite le nombre de vérifications (60 par heure). Réessaie un peu plus tard."
                        : "Connexion à GitHub impossible : " + ex.Message;
                }
                catch (Exception ex) { error = ex.Message; }
                SendOrPostCallback done = delegate
                {
                    s.LastUpdateCheck = App.Today;
                    s.Save();
                    if (error != null)
                    {
                        Program.Log("Mise à jour : " + error);
                        if (manual) MessageBox.Show(app.Main, error, "Relais — mises à jour", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }
                    if (info.Version == null || info.Version <= Current)
                    {
                        if (manual) app.Toast.ShowMessage("Relais est à jour (v" + Current.ToString(3) + ")", false);
                        return;
                    }
                    app.AvailableUpdate = info;
                    Offer(app, info);
                };
                if (ui != null) ui.Post(done, null); else done(null);
            });
        }

        public static void Offer(App app, Info info)
        {
            if (string.IsNullOrEmpty(info.ExeUrl))
            {
                if (MessageBox.Show(app.Main, "Relais " + info.Tag + " est disponible, mais la release ne contient pas de Relais.exe.\nOuvrir la page de téléchargement ?",
                    "Relais — mise à jour", MessageBoxButtons.YesNo) == DialogResult.Yes) try { Process.Start(info.Page); } catch { }
                return;
            }
            string notes = (info.Notes ?? "").Trim();
            if (notes.Length > 600) notes = notes.Substring(0, 600) + "…";
            if (MessageBox.Show(app.Main, "Relais " + info.Tag + " est disponible (tu as la v" + Current.ToString(3) + ").\n\n" + notes + "\n\nMettre à jour maintenant ? Relais redémarrera tout seul.",
                "Relais — mise à jour", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                Install(app, info);
        }

        /// <summary>Télécharge le nouvel exe, puis un petit script remplace l'ancien après la fermeture de Relais.</summary>
        public static void Install(App app, Info info)
        {
            string exe = Application.ExecutablePath;
            string dir = Path.GetDirectoryName(exe);
            string tmpExe = Path.Combine(Path.GetTempPath(), "Relais.update.exe");
            app.Toast.ShowMessage("Téléchargement de " + info.Tag + "…", false, 30);
            SynchronizationContext ui = SynchronizationContext.Current;
            ThreadPool.QueueUserWorkItem(delegate
            {
                string error = null;
                try
                {
                    using (WebClient wc = new WebClient())
                    {
                        wc.Headers["User-Agent"] = "Relais-Updater";
                        wc.DownloadFile(info.ExeUrl, tmpExe);
                    }
                    if (new FileInfo(tmpExe).Length < 20000) throw new IOException("fichier téléchargé invalide");
                }
                catch (Exception ex) { error = ex.Message; }
                SendOrPostCallback done = delegate
                {
                    if (error != null)
                    {
                        MessageBox.Show(app.Main, "Échec du téléchargement : " + error, "Relais");
                        return;
                    }
                    // test d'écriture dans le dossier de Relais
                    try { string probe = Path.Combine(dir, ".relais-write-test"); File.WriteAllText(probe, "x"); File.Delete(probe); }
                    catch
                    {
                        MessageBox.Show(app.Main, "Relais n'a pas le droit d'écrire dans son dossier (" + dir + ").\nDéplace Relais dans un dossier à toi (ex. Documents) ou utilise l'installateur.", "Relais");
                        return;
                    }
                    int pid = Process.GetCurrentProcess().Id;
                    string cmd = Path.Combine(Path.GetTempPath(), "relais-update.cmd");
                    File.WriteAllText(cmd,
                        "@echo off\r\n" +
                        ":wait\r\n" +
                        "tasklist /FI \"PID eq " + pid + "\" 2>nul | find \"" + pid + "\" >nul && (timeout /t 1 /nobreak >nul & goto wait)\r\n" +
                        "copy /y \"" + exe + "\" \"" + exe + ".old\" >nul\r\n" +
                        "copy /y \"" + tmpExe + "\" \"" + exe + "\" >nul || (copy /y \"" + exe + ".old\" \"" + exe + "\" >nul)\r\n" +
                        "start \"\" \"" + exe + "\" --relaunch\r\n" +
                        "del \"" + tmpExe + "\" >nul 2>&1\r\n" +
                        "del \"%~f0\"\r\n", System.Text.Encoding.Default);
                    ProcessStartInfo psi = new ProcessStartInfo("cmd.exe", "/c \"" + cmd + "\"");
                    psi.CreateNoWindow = true;
                    psi.WindowStyle = ProcessWindowStyle.Hidden;
                    psi.UseShellExecute = false;
                    Process.Start(psi);
                    Program.Log("Mise à jour vers " + info.Tag + " lancée.");
                    app.Quit();
                };
                if (ui != null) ui.Post(done, null); else done(null);
            });
        }
    }
}

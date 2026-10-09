using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;

namespace Relais
{
    /// <summary>
    /// Sauvegarde des réglages dans un Gist GitHub secret (ton compte, ton jeton) :
    /// profils, fiches, crafts, minuteurs, stats… pour tout retrouver sur un autre PC.
    /// </summary>
    public static class CloudSync
    {
        const string FileName = "relais-config.json";
        const string Description = "Relais — sauvegarde des réglages";

        static CloudSync()
        {
            try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; } catch { }
        }

        static string Call(string method, string url, string token, string body)
        {
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = method;
            req.UserAgent = "Relais (organizer Dofus)";
            req.Accept = "application/vnd.github+json";
            req.Headers["Authorization"] = "token " + token.Trim();
            req.Timeout = 20000;
            if (body != null)
            {
                byte[] b = Encoding.UTF8.GetBytes(body);
                req.ContentType = "application/json";
                req.ContentLength = b.Length;
                using (Stream s = req.GetRequestStream()) s.Write(b, 0, b.Length);
            }
            try
            {
                using (WebResponse resp = req.GetResponse())
                using (StreamReader r = new StreamReader(resp.GetResponseStream(), Encoding.UTF8)) return r.ReadToEnd();
            }
            catch (WebException ex)
            {
                HttpWebResponse hr = ex.Response as HttpWebResponse;
                if (hr != null)
                {
                    int code = (int)hr.StatusCode;
                    if (code == 401) throw new Exception(L.T("Jeton GitHub refusé (vérifie-le, et qu'il a bien la permission « gist »)."));
                    if (code == 404) throw new Exception(L.T("Sauvegarde introuvable (supprimée ?)."));
                    if (code == 403) throw new Exception(L.T("GitHub refuse l'accès (jeton sans permission « gist » ou trop de requêtes)."));
                    throw new Exception(L.T("GitHub a répondu ") + code + ".");
                }
                throw new Exception(L.T("Pas de connexion à GitHub (") + ex.Status + ").");
            }
        }

        /// <summary>Contenu envoyé : la config sans le jeton.</summary>
        public static string Payload(Settings s)
        {
            string tok = s.GistToken;
            try { s.GistToken = ""; return Settings.Json().Serialize(s); }
            finally { s.GistToken = tok; }
        }

        /// <summary>Envoie la config (crée le Gist au premier envoi). Renvoie un message lisible.</summary>
        public static string Upload(Settings s, string payload)
        {
            if (string.IsNullOrEmpty(s.GistToken)) throw new Exception(L.T("Colle d'abord ton jeton GitHub."));
            Dictionary<string, object> file = new Dictionary<string, object>();
            file["content"] = payload;
            Dictionary<string, object> files = new Dictionary<string, object>();
            files[FileName] = file;
            Dictionary<string, object> body = new Dictionary<string, object>();
            body["description"] = Description;
            body["files"] = files;
            if (string.IsNullOrEmpty(s.GistId)) s.GistId = FindExisting(s.GistToken);
            string resp;
            if (string.IsNullOrEmpty(s.GistId))
            {
                body["public"] = false;
                resp = Call("POST", "https://api.github.com/gists", s.GistToken, Settings.Json().Serialize(body));
                Dictionary<string, object> d = Settings.Json().DeserializeObject(resp) as Dictionary<string, object>;
                if (d != null && d.ContainsKey("id")) s.GistId = Convert.ToString(d["id"]);
            }
            else
                Call("PATCH", "https://api.github.com/gists/" + s.GistId, s.GistToken, Settings.Json().Serialize(body));
            s.LastSync = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            return L.T("Sauvegarde envoyée (") + s.LastSync + ").";
        }

        /// <summary>Cherche une sauvegarde Relais existante sur le compte (nouveau PC).</summary>
        static string FindExisting(string token)
        {
            string resp = Call("GET", "https://api.github.com/gists?per_page=100", token, null);
            IList list = Settings.Json().DeserializeObject(resp) as IList;
            if (list == null) return null;
            foreach (object o in list)
            {
                Dictionary<string, object> g = o as Dictionary<string, object>;
                if (g == null) continue;
                Dictionary<string, object> files = g.ContainsKey("files") ? g["files"] as Dictionary<string, object> : null;
                if (files != null && files.ContainsKey(FileName)) return Convert.ToString(g["id"]);
            }
            return null;
        }

        /// <summary>Télécharge la sauvegarde et l'écrit à la place de config.json (le jeton local est gardé).</summary>
        public static string Download(Settings s)
        {
            if (string.IsNullOrEmpty(s.GistToken)) throw new Exception(L.T("Colle d'abord ton jeton GitHub."));
            if (string.IsNullOrEmpty(s.GistId)) s.GistId = FindExisting(s.GistToken);
            if (string.IsNullOrEmpty(s.GistId)) throw new Exception(L.T("Aucune sauvegarde Relais sur ce compte GitHub pour l'instant."));
            string resp = Call("GET", "https://api.github.com/gists/" + s.GistId, s.GistToken, null);
            Dictionary<string, object> d = Settings.Json().DeserializeObject(resp) as Dictionary<string, object>;
            Dictionary<string, object> files = d != null && d.ContainsKey("files") ? d["files"] as Dictionary<string, object> : null;
            Dictionary<string, object> f = files != null && files.ContainsKey(FileName) ? files[FileName] as Dictionary<string, object> : null;
            if (f == null) throw new Exception(L.T("La sauvegarde ne contient pas de réglages Relais."));
            string content = f.ContainsKey("content") ? Convert.ToString(f["content"]) : null;
            bool truncated = f.ContainsKey("truncated") && f["truncated"] is bool && (bool)f["truncated"];
            if (truncated && f.ContainsKey("raw_url"))
            {
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(Convert.ToString(f["raw_url"]));
                req.UserAgent = "Relais (organizer Dofus)";
                using (WebResponse r2 = req.GetResponse())
                using (StreamReader sr = new StreamReader(r2.GetResponseStream(), Encoding.UTF8)) content = sr.ReadToEnd();
            }
            Settings remote = Settings.Json().Deserialize<Settings>(content);
            if (remote == null) throw new Exception(L.T("Sauvegarde illisible."));
            remote.GistToken = s.GistToken;
            remote.GistId = s.GistId;
            remote.AutoSync = s.AutoSync;
            remote.LastSync = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            remote.Fix();
            remote.Save();
            return remote.Profiles.Count + L.T(" profil(s) récupéré(s).");
        }

        /// <summary>Lance une opération en arrière-plan et rappelle sur le fil de l'interface.</summary>
        public static void Run(SynchronizationContext ui, Func<string> op, Action<string, bool> done)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                string msg; bool ok;
                try { msg = op(); ok = true; }
                catch (Exception ex) { msg = ex.Message; ok = false; Program.Log("Synchro : " + ex.Message); }
                ui.Post(delegate { done(msg, ok); }, null);
            });
        }
    }
}

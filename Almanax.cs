using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;

namespace Relais
{
    /// <summary>
    /// Almanax du jour via l'API publique dofusdude (https://api.dofusdu.de) : offrande, bonus, kamas.
    /// Chargé une fois par jour, en arrière-plan.
    /// </summary>
    public static class Almanax
    {
        public sealed class Day
        {
            public string Date;
            public string ItemName;
            public int Quantity;
            public string BonusName;
            public string BonusText;
            public int Kamas;
            public Image Icon;
        }

        static Day cached;
        static bool loading;
        static string failedDate;
        public static event EventHandler Loaded;

        static Almanax()
        {
            try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; } catch { }
        }

        public static string TodayKey { get { return DateTime.Now.ToString("yyyy-MM-dd"); } }

        /// <summary>Almanax du jour s'il est chargé ; sinon lance le chargement et renvoie null.</summary>
        public static Day Get(SynchronizationContext ui, string lang)
        {
            string today = TodayKey;
            if (cached != null && cached.Date == today) return cached;
            if (loading || failedDate == today) return null;
            loading = true;
            ThreadPool.QueueUserWorkItem(delegate
            {
                Day d = null;
                try { d = Fetch(today, lang); }
                catch (Exception ex) { Program.Log("Almanax : " + ex.Message); }
                ui.Post(delegate
                {
                    loading = false;
                    if (d != null) cached = d; else failedDate = today;
                    if (Loaded != null) Loaded(null, EventArgs.Empty);
                }, null);
            });
            return null;
        }

        /// <summary>Réessayer (ex. après une coupure réseau).</summary>
        public static void Retry() { failedDate = null; }

        static Dictionary<string, object> Obj(Dictionary<string, object> d, string k)
        {
            object v;
            return d != null && d.TryGetValue(k, out v) ? v as Dictionary<string, object> : null;
        }

        static string Str(Dictionary<string, object> d, string k)
        {
            object v;
            return d != null && d.TryGetValue(k, out v) && v != null ? Convert.ToString(v, CultureInfo.InvariantCulture) : null;
        }

        static int Int(Dictionary<string, object> d, string k)
        {
            object v;
            if (d == null || !d.TryGetValue(k, out v) || v == null) return 0;
            try { return Convert.ToInt32(v, CultureInfo.InvariantCulture); } catch { return 0; }
        }

        static Day Fetch(string date, string lang)
        {
            string url = "https://api.dofusdu.de/dofus3/v1/" + (lang == "en" ? "en" : "fr") + "/almanax/" + date;
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = "Relais (organizer Dofus)";
            req.Accept = "application/json";
            req.Timeout = 15000;
            string json;
            using (WebResponse resp = req.GetResponse())
            using (StreamReader r = new StreamReader(resp.GetResponseStream(), Encoding.UTF8)) json = r.ReadToEnd();
            Dictionary<string, object> root = Settings.Json().DeserializeObject(json) as Dictionary<string, object>;
            if (root == null) return null;
            Day d = new Day();
            d.Date = date;
            Dictionary<string, object> bonus = Obj(root, "bonus");
            d.BonusText = Str(bonus, "description") ?? "";
            d.BonusName = Str(Obj(bonus, "type"), "name") ?? "";
            d.Kamas = Int(root, "reward_kamas");
            Dictionary<string, object> trib = Obj(root, "tribute");
            Dictionary<string, object> item = Obj(trib, "item");
            d.ItemName = Str(item, "name") ?? "?";
            d.Quantity = Math.Max(1, Int(trib, "quantity"));
            string icon = Str(Obj(item, "image_urls"), "icon");
            if (!string.IsNullOrEmpty(icon))
            {
                try
                {
                    HttpWebRequest ir = (HttpWebRequest)WebRequest.Create(icon);
                    ir.UserAgent = req.UserAgent; ir.Timeout = 10000;
                    using (WebResponse resp = ir.GetResponse())
                    using (Stream s = resp.GetResponseStream())
                    using (MemoryStream ms = new MemoryStream())
                    {
                        s.CopyTo(ms);
                        ms.Position = 0;
                        d.Icon = new Bitmap(Image.FromStream(ms));
                    }
                }
                catch { }
            }
            // le texte du bonus peut contenir du balisage simple
            d.BonusText = System.Text.RegularExpressions.Regex.Replace(d.BonusText, "<[^>]+>", "").Replace("\n", " ").Trim();
            return d;
        }
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;

namespace Relais
{
    /// <summary>
    /// Client minimal de l'API publique DofusDB (https://api.dofusdb.fr) : recherche d'objets, recettes, donjons.
    /// Lecture seule, rien n'est envoyé au jeu.
    /// </summary>
    public static class DofusDb
    {
        const string Base = "https://api.dofusdb.fr";

        public sealed class Item
        {
            public int Id;
            public string Name;
            public int Level;
            public override string ToString() { return Name + (Level > 0 ? "  (niv. " + Level + ")" : ""); }
        }

        public sealed class Ingredient
        {
            public int Id;
            public string Name;
            public int Qty;
        }

        static DofusDb()
        {
            try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; } catch { }
        }

        static object GetJson(string pathAndQuery)
        {
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(Base + pathAndQuery);
            req.UserAgent = "Relais (organizer Dofus)";
            req.Accept = "application/json";
            req.Timeout = 15000;
            using (WebResponse resp = req.GetResponse())
            using (StreamReader r = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                return Settings.Json().DeserializeObject(r.ReadToEnd());
        }

        static IList DataOf(object root)
        {
            Dictionary<string, object> d = root as Dictionary<string, object>;
            if (d != null && d.ContainsKey("data")) return d["data"] as IList;
            return root as IList;
        }

        static string Fr(object nameObj)
        {
            Dictionary<string, object> d = nameObj as Dictionary<string, object>;
            if (d == null) return nameObj as string;
            object v;
            if (d.TryGetValue("fr", out v) && v is string) return (string)v;
            foreach (object o in d.Values) if (o is string) return (string)o;
            return null;
        }

        static int Int(Dictionary<string, object> d, string key)
        {
            object v;
            if (d == null || !d.TryGetValue(key, out v) || v == null) return 0;
            try { return Convert.ToInt32(v, CultureInfo.InvariantCulture); } catch { return 0; }
        }

        /// <summary>« Épée de Boisaille » -> « epee-de-boisaille » (DofusDB cherche sur le slug).</summary>
        public static string Slug(string s)
        {
            string n = (s ?? "").Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
            StringBuilder sb = new StringBuilder();
            foreach (char c in n)
            {
                UnicodeCategory cat = CharUnicodeInfo.GetUnicodeCategory(c);
                if (cat == UnicodeCategory.NonSpacingMark) continue;
                if (char.IsLetterOrDigit(c)) sb.Append(c);
                else if (sb.Length > 0 && sb[sb.Length - 1] != '-') sb.Append('-');
            }
            return sb.ToString().Trim('-');
        }

        static Item ToItem(Dictionary<string, object> d)
        {
            Item it = new Item();
            it.Id = Int(d, "id");
            object n;
            it.Name = d.TryGetValue("name", out n) ? Fr(n) : null;
            if (string.IsNullOrEmpty(it.Name)) it.Name = "Objet " + it.Id;
            it.Level = Int(d, "level");
            return it;
        }

        public static List<Item> Search(string text)
        {
            List<Item> list = new List<Item>();
            string q = Uri.EscapeDataString(Slug(text));
            object root = GetJson("/items?slug.fr[$search]=" + q + "&$limit=20&lang=fr");
            IList data = DataOf(root);
            if (data != null)
                foreach (object o in data)
                {
                    Dictionary<string, object> d = o as Dictionary<string, object>;
                    if (d != null) list.Add(ToItem(d));
                }
            return list;
        }

        static IEnumerable<int> Flatten(object o)
        {
            IList l = o as IList;
            if (l == null)
            {
                if (o != null) yield return Convert.ToInt32(o, CultureInfo.InvariantCulture);
                yield break;
            }
            foreach (object x in l)
                foreach (int v in Flatten(x)) yield return v;
        }

        /// <summary>Recette d'un objet (null si l'objet ne se crafte pas).</summary>
        public static List<Ingredient> Recipe(int itemId)
        {
            object root = GetJson("/recipes?resultId=" + itemId + "&lang=fr");
            IList data = DataOf(root);
            if (data == null || data.Count == 0) return null;
            Dictionary<string, object> r = data[0] as Dictionary<string, object>;
            if (r == null) return null;
            List<int> ids = new List<int>(Flatten(r.ContainsKey("ingredientIds") ? r["ingredientIds"] : null));
            List<int> qty = new List<int>(Flatten(r.ContainsKey("quantities") ? r["quantities"] : null));
            List<Ingredient> list = new List<Ingredient>();
            Dictionary<int, string> names = new Dictionary<int, string>();

            // noms : parfois fournis directement dans « ingredients »
            object ing;
            if (r.TryGetValue("ingredients", out ing) && ing is IList)
                foreach (object o in (IList)ing)
                {
                    Dictionary<string, object> d = o as Dictionary<string, object>;
                    if (d == null) continue;
                    Item it = ToItem(d);
                    names[it.Id] = it.Name;
                }
            List<int> missing = new List<int>();
            foreach (int id in ids) if (!names.ContainsKey(id)) missing.Add(id);
            if (missing.Count > 0)
            {
                StringBuilder q = new StringBuilder("/items?$limit=50&lang=fr");
                foreach (int id in missing) q.Append("&id[$in][]=" + id);
                IList items = DataOf(GetJson(q.ToString()));
                if (items != null)
                    foreach (object o in items)
                    {
                        Dictionary<string, object> d = o as Dictionary<string, object>;
                        if (d == null) continue;
                        Item it = ToItem(d);
                        names[it.Id] = it.Name;
                    }
            }
            for (int i = 0; i < ids.Count; i++)
            {
                Ingredient g = new Ingredient();
                g.Id = ids[i];
                g.Qty = i < qty.Count ? qty[i] : 1;
                string nm;
                g.Name = names.TryGetValue(g.Id, out nm) ? nm : "Objet " + g.Id;
                list.Add(g);
            }
            return list;
        }

        /// <summary>Liste des donjons (nom + niveau conseillé), triée par niveau.</summary>
        public static List<Item> Dungeons()
        {
            List<Item> list = new List<Item>();
            for (int skip = 0; skip < 400; skip += 50)
            {
                IList data = DataOf(GetJson("/dungeons?$limit=50&$skip=" + skip + "&lang=fr"));
                if (data == null || data.Count == 0) break;
                foreach (object o in data)
                {
                    Dictionary<string, object> d = o as Dictionary<string, object>;
                    if (d == null) continue;
                    Item it = ToItem(d);
                    it.Level = Int(d, "optimalPlayerLevel");
                    list.Add(it);
                }
                if (data.Count < 50) break;
            }
            list.Sort(delegate (Item a, Item b) { int c = a.Level.CompareTo(b.Level); return c != 0 ? c : string.Compare(a.Name, b.Name, StringComparison.CurrentCulture); });
            return list;
        }

        /// <summary>Exécute un appel en arrière-plan et rend le résultat sur le fil UI.</summary>
        public static void Run<T>(Func<T> work, Action<T, Exception> done)
        {
            SynchronizationContext ui = SynchronizationContext.Current;
            ThreadPool.QueueUserWorkItem(delegate
            {
                T res = default(T); Exception err = null;
                try { res = work(); } catch (Exception ex) { err = ex; Program.Log("DofusDB : " + ex.Message); }
                if (ui != null) ui.Post(delegate { done(res, err); }, null); else done(res, err);
            });
        }
    }
}

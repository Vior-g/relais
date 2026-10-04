using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace Relais
{
    /// <summary>Un raccourci : touches modificatrices + une touche clavier ou un bouton de souris.</summary>
    public sealed class Hotkey
    {
        public const int CTRL = 1, ALT = 2, SHIFT = 4, WIN = 8;
        public const string MOUSE4 = "Souris4", MOUSE5 = "Souris5", MOUSEMID = "SourisMilieu";

        public int Mods;
        public string Key; // nom de Keys (ex. "F1") ou MOUSE4 / MOUSE5 / MOUSEMID

        public Hotkey(int mods, string key) { Mods = mods; Key = key; }

        public bool IsMouse { get { return Key != null && Key.StartsWith("Souris"); } }

        public static Hotkey Parse(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            string[] parts = s.Split('+');
            int mods = 0;
            for (int i = 0; i < parts.Length - 1; i++)
            {
                string p = parts[i].Trim().ToLowerInvariant();
                if (p == "ctrl") mods |= CTRL;
                else if (p == "alt") mods |= ALT;
                else if (p == "maj" || p == "shift") mods |= SHIFT;
                else if (p == "win") mods |= WIN;
            }
            string key = parts[parts.Length - 1].Trim();
            if (key.Length == 0) return null;
            return new Hotkey(mods, key);
        }

        /// <summary>Identifiant canonique utilisé pour la sauvegarde et les comparaisons.</summary>
        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            if ((Mods & CTRL) != 0) sb.Append("Ctrl+");
            if ((Mods & ALT) != 0) sb.Append("Alt+");
            if ((Mods & SHIFT) != 0) sb.Append("Maj+");
            if ((Mods & WIN) != 0) sb.Append("Win+");
            sb.Append(Key);
            return sb.ToString();
        }

        /// <summary>Texte lisible pour l'interface.</summary>
        public string Display()
        {
            StringBuilder sb = new StringBuilder();
            if ((Mods & CTRL) != 0) sb.Append("Ctrl + ");
            if ((Mods & ALT) != 0) sb.Append("Alt + ");
            if ((Mods & SHIFT) != 0) sb.Append("Maj + ");
            if ((Mods & WIN) != 0) sb.Append("Win + ");
            sb.Append(PrettyKey(Key));
            return sb.ToString();
        }

        public static string PrettyKey(string k)
        {
            switch (k)
            {
                case MOUSE4: return "Souris 4";
                case MOUSE5: return "Souris 5";
                case MOUSEMID: return "Clic molette";
                case "Oemtilde": case "Oem7": return "²";
                case "Next": return "Page suiv.";
                case "PageUp": return "Page préc.";
                case "Capital": return "Verr. Maj";
                case "Space": return "Espace";
                case "Tab": return "Tab";
                case "Return": return "Entrée";
                case "Back": return "Retour";
                case "Delete": return "Suppr";
                case "Insert": return "Inser";
                case "Home": return "Début";
                case "End": return "Fin";
                case "Up": return "Haut";
                case "Down": return "Bas";
                case "Left": return "Gauche";
                case "Right": return "Droite";
            }
            if (k.StartsWith("NumPad")) return "Pavé " + k.Substring(6);
            if (k.Length == 2 && k[0] == 'D' && char.IsDigit(k[1])) return k.Substring(1);
            return k;
        }
    }

    public sealed class Member
    {
        public string Name { get; set; }
        public string Hotkey { get; set; }
        public bool Enabled { get; set; }
        public Member() { Enabled = true; }
    }

    /// <summary>Position enregistrée d'une fenêtre (disposition de profil).</summary>
    public sealed class WinPos
    {
        public string Name { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public int W { get; set; }
        public int H { get; set; }
        public bool Max { get; set; }
    }

    public sealed class TimerItem
    {
        public string Name { get; set; }
        public int Minutes { get; set; }
        public long EndUtc { get; set; } // ticks UTC, 0 = arrêté
    }

    public sealed class Reminder
    {
        public string Name { get; set; }
        public string Time { get; set; } // HH:mm
        public bool Enabled { get; set; }
        public string LastFired { get; set; } // yyyy-MM-dd
        public Reminder() { Enabled = true; }
    }

    public sealed class StatEntry
    {
        public double Active { get; set; } // secondes au premier plan
        public double Online { get; set; } // secondes connecté
    }

    /// <summary>Fiche d'un personnage (v2).</summary>
    public sealed class CharSheet
    {
        public int Level { get; set; }
        public string Jobs { get; set; }
        public string StuffLink { get; set; }
        public List<CheckItem> Goals { get; set; }
        public List<CheckItem> Daily { get; set; }
        public List<CheckItem> Dungeons { get; set; }
        public string DailyDate { get; set; }
        public CharSheet() { Goals = new List<CheckItem>(); Daily = new List<CheckItem>(); Dungeons = new List<CheckItem>(); }
        public void Fix()
        {
            if (Goals == null) Goals = new List<CheckItem>();
            if (Daily == null) Daily = new List<CheckItem>();
            if (Dungeons == null) Dungeons = new List<CheckItem>();
        }
    }

    public sealed class CheckItem
    {
        public string Text { get; set; }
        public bool Done { get; set; }
        public string Who { get; set; }   // perso assigné (craft)
        public int Qty { get; set; }
    }

    /// <summary>Projet de craft (v2) : un objet, ses ingrédients, qui s'en occupe.</summary>
    public sealed class CraftProject
    {
        public string Name { get; set; }
        public int ItemId { get; set; }
        public int Count { get; set; }
        public List<CheckItem> Ingredients { get; set; }
        public CraftProject() { Count = 1; Ingredients = new List<CheckItem>(); }
    }

    public sealed class Profile
    {
        public string Name { get; set; }
        public string Hotkey { get; set; }
        public List<WinPos> Layout { get; set; }
        public List<Member> Members { get; set; }
        public Profile() { Members = new List<Member>(); }

        public Member Find(string name)
        {
            foreach (Member m in Members)
                if (string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase)) return m;
            return null;
        }
    }

    public sealed class Settings
    {
        public List<Profile> Profiles { get; set; }
        public string CurrentProfile { get; set; }

        public string KeyNext { get; set; }
        public string KeyPrev { get; set; }
        public string KeyLeader { get; set; }
        public string KeyToggleBar { get; set; }
        public string KeyPause { get; set; }
        public string KeyStack { get; set; }
        public string KeyMosaic { get; set; }

        public bool AutoSwitchOnTurn { get; set; }
        public bool AlertOnDisconnect { get; set; }
        public bool AlertSound { get; set; }
        public int BarOpacity { get; set; }
        public bool BarLarge { get; set; }
        public bool BarAutoHide { get; set; }
        public int LastTab { get; set; }

        // v1.3
        public string KeyInvite { get; set; }
        public string KeyLayout { get; set; }
        public bool AutoApplyLayout { get; set; }
        public bool PriorityBoost { get; set; }
        public bool AudioMuteBackground { get; set; }
        public bool AudioKeepLeader { get; set; }
        public string AccentColor { get; set; }
        public Dictionary<string, int> CharVolume { get; set; }
        public Dictionary<string, string> Notes { get; set; }
        public Dictionary<string, string> CharColors { get; set; }
        public Dictionary<string, string> CharImages { get; set; }

        public CharSheet SheetOf(string name)
        {
            CharSheet s;
            if (!Sheets.TryGetValue(name, out s) || s == null) { s = new CharSheet(); Sheets[name] = s; }
            return s;
        }
        public List<TimerItem> Timers { get; set; }
        public List<Reminder> Reminders { get; set; }
        public Dictionary<string, Dictionary<string, StatEntry>> Stats { get; set; }

        // v2.0
        public string KeyPreview { get; set; }
        public string KeyLastChar { get; set; }
        public bool OverlayEnabled { get; set; }
        public bool OverlayFrame { get; set; }
        public bool OverlayShowTimer { get; set; }
        public int OverlayCorner { get; set; } // 0 haut-gauche, 1 haut-droite, 2 bas-gauche, 3 bas-droite
        public bool BarHoverPreview { get; set; }
        public bool AutoProfile { get; set; }
        public bool LightTheme { get; set; }
        public bool OnboardingDone { get; set; }
        public string DiscordWebhook { get; set; }
        public string NtfyTopic { get; set; }
        public bool RemoteOnlyWhenAway { get; set; }
        public int AwayMinutes { get; set; }
        public bool RemoteDisconnect { get; set; }
        public bool RemoteTimers { get; set; }
        public bool RemoteTurn { get; set; }
        public string UpdateRepo { get; set; }
        public bool CheckUpdates { get; set; }
        public string LastUpdateCheck { get; set; }
        public Dictionary<string, CharSheet> Sheets { get; set; }
        public List<CraftProject> Crafts { get; set; }
        public int PreviewX { get; set; }
        public int PreviewY { get; set; }
        public int PreviewW { get; set; }
        public int PreviewH { get; set; }

        public bool OnlyWhenDofusFocused { get; set; }
        public bool AutoAddNewCharacters { get; set; }
        public bool ShowBar { get; set; }
        public bool BarVertical { get; set; }
        public bool BarShowNames { get; set; }
        public int BarX { get; set; }
        public int BarY { get; set; }
        public bool MinimizeToTray { get; set; }
        public bool StartMinimized { get; set; }

        public Settings()
        {
            Profiles = new List<Profile>();
            KeyNext = "Souris5";
            KeyPrev = "Souris4";
            KeyLeader = "F1";
            KeyToggleBar = "Ctrl+F12";
            KeyPause = "Pause";
            OnlyWhenDofusFocused = true;
            AutoAddNewCharacters = true;
            ShowBar = true;
            BarVertical = false;
            BarShowNames = true;
            BarX = -1;
            BarY = -1;
            MinimizeToTray = true;
            AutoSwitchOnTurn = true;
            AlertOnDisconnect = true;
            AlertSound = true;
            BarOpacity = 100;
            PriorityBoost = true;
            OverlayEnabled = true;
            OverlayFrame = true;
            OverlayShowTimer = true;
            BarHoverPreview = true;
            RemoteOnlyWhenAway = true;
            AwayMinutes = 3;
            RemoteDisconnect = true;
            RemoteTimers = true;
            CheckUpdates = true;
            PreviewX = -1;
            Fix();
        }

        /// <summary>Complète les champs absents (anciennes configs).</summary>
        public void Fix()
        {
            if (Profiles == null) Profiles = new List<Profile>();
            foreach (Profile p in Profiles) if (p.Members == null) p.Members = new List<Member>();
            if (CharVolume == null) CharVolume = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (Notes == null) Notes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (CharColors == null) CharColors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (CharImages == null) CharImages = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (Timers == null) Timers = new List<TimerItem>();
            if (Reminders == null) Reminders = new List<Reminder>();
            if (Stats == null) Stats = new Dictionary<string, Dictionary<string, StatEntry>>();
            if (Sheets == null) Sheets = new Dictionary<string, CharSheet>(StringComparer.OrdinalIgnoreCase);
            foreach (CharSheet cs in Sheets.Values) if (cs != null) cs.Fix();
            if (Crafts == null) Crafts = new List<CraftProject>();
            foreach (CraftProject cp in Crafts) if (cp.Ingredients == null) cp.Ingredients = new List<CheckItem>();
            if (AwayMinutes < 1) AwayMinutes = 3;
            if (OverlayCorner < 0 || OverlayCorner > 3) OverlayCorner = 0;
            if (BarOpacity < 30 || BarOpacity > 100) BarOpacity = 100;
            if (LastTab < 0 || LastTab > 5) LastTab = 0;
        }

        static Dictionary<string, T> CI<T>(Dictionary<string, T> d)
        {
            return new Dictionary<string, T>(d, StringComparer.OrdinalIgnoreCase);
        }

        public static JavaScriptSerializer Json()
        {
            JavaScriptSerializer j = new JavaScriptSerializer();
            j.MaxJsonLength = int.MaxValue;
            return j;
        }

        public Profile GetCurrent()
        {
            {
                foreach (Profile p in Profiles)
                    if (p.Name == CurrentProfile) return p;
                if (Profiles.Count == 0)
                {
                    Profile p = new Profile();
                    p.Name = "Ma team";
                    Profiles.Add(p);
                }
                CurrentProfile = Profiles[0].Name;
                return Profiles[0];
            }
        }

        // ---- persistance ----

        public static string Dir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Relais"); }
        }

        public static string FilePath { get { return Path.Combine(Dir, "config.json"); } }

        public static Settings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    string json = File.ReadAllText(FilePath, Encoding.UTF8);
                    Settings s = Json().Deserialize<Settings>(json);
                    if (s != null)
                    {
                        s.Fix();
                        // dictionnaires insensibles à la casse des noms de persos
                        s.CharVolume = CI(s.CharVolume); s.Notes = CI(s.Notes);
                        s.CharColors = CI(s.CharColors); s.CharImages = CI(s.CharImages);
                        s.Sheets = CI(s.Sheets);
                        return s;
                    }
                }
            }
            catch (Exception ex)
            {
                try { File.Copy(FilePath, FilePath + ".bak", true); } catch { }
                MessageBox.Show("Configuration illisible, une nouvelle a été créée.\n(ancienne sauvegardée en config.json.bak)\n\n" + ex.Message,
                    "Relais", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            return new Settings();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                string json = Json().Serialize(this);
                string tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, json, Encoding.UTF8);
                if (File.Exists(FilePath)) File.Delete(FilePath);
                File.Move(tmp, FilePath);
            }
            catch { }
        }
    }
}

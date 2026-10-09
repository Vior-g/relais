using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace Relais
{
    /// <summary>Cœur de l'application : état, détection, raccourcis, icône de notification.</summary>
    public sealed class App : ApplicationContext
    {
        public readonly Settings S;
        public List<GameWindow> Windows = new List<GameWindow>();
        public IntPtr ForegroundHandle = IntPtr.Zero;
        public bool Paused;

        public event EventHandler StateChanged;   // fenêtres / premier plan / pause
        public event EventHandler ProfileChanged; // contenu du profil modifié

        readonly Dictionary<string, GameWindow> byName = new Dictionary<string, GameWindow>(StringComparer.OrdinalIgnoreCase);
        HashSet<IntPtr> dofusHandles = new HashSet<IntPtr>();
        Dictionary<string, Action> bindings = new Dictionary<string, Action>();
        string pauseKey;

        InputHook hook;
        readonly SynchronizationContext ui;
        readonly System.Windows.Forms.Timer timer;
        readonly NotifyIcon tray;
        readonly ToolStripMenuItem trayPause, trayProfiles;
        public Toast Toast;
        public OverlayManager Overlays;
        public CombatTracker Combat;
        public SynchronizationContext Ui { get { return ui; } }
        public PhoneServer Phone;
        /// <summary>Vrai après une restauration cloud : la config sur disque ne doit pas être écrasée.</summary>
        public bool SkipSaveOnExit;
        bool syncDirty = true;
        DateTime lastAutoSync = DateTime.Now;

        public void SetPhone(bool on)
        {
            S.PhoneRemote = on; S.Save();
            if (Phone == null) Phone = new PhoneServer(this);
            if (on) Phone.Start(); else Phone.Stop();
            if (on && Phone.Error != null) Toast.ShowMessage(L.T("Télécommande : ") + Phone.Error, true, 6);
        }

        public void CloudUpload(Action<string, bool> done)
        {
            string payload = CloudSync.Payload(S);
            CloudSync.Run(ui, delegate { return CloudSync.Upload(S, payload); }, delegate (string msg, bool ok)
            {
                if (ok) { S.Save(); syncDirty = false; Record("info", L.T("Sauvegarde cloud envoyée")); }
                if (done != null) done(msg, ok);
            });
        }

        public void CloudDownload(Action<string, bool> done)
        {
            CloudSync.Run(ui, delegate { return CloudSync.Download(S); }, delegate (string msg, bool ok)
            {
                if (ok) SkipSaveOnExit = true;
                if (done != null) done(msg, ok);
            });
        }

        /// <summary>Envoi automatique toutes les 30 min si quelque chose a changé.</summary>
        void AutoSyncTick()
        {
            if (!S.AutoSync || string.IsNullOrEmpty(S.GistToken) || !syncDirty) return;
            if ((DateTime.Now - lastAutoSync).TotalMinutes < 30) return;
            lastAutoSync = DateTime.Now;
            CloudUpload(null);
        }

        /// <summary>Journal des derniers événements (affiché sur l'accueil).</summary>
        public sealed class LogEvent { public DateTime At; public string Text; public string Kind; }
        public readonly List<LogEvent> Events = new List<LogEvent>();
        public event EventHandler EventsChanged;

        public void Record(string kind, string text)
        {
            LogEvent e = new LogEvent();
            e.At = DateTime.Now; e.Text = text; e.Kind = kind;
            Events.Insert(0, e);
            if (Events.Count > 40) Events.RemoveAt(Events.Count - 1);
            if (EventsChanged != null) EventsChanged(this, EventArgs.Empty);
        }
        public HoverPreview Hover;
        PreviewForm previewWall;
        readonly Dictionary<IntPtr, DateTime> flashing = new Dictionary<IntPtr, DateTime>();
        readonly Dictionary<IntPtr, DateTime> lastRemoteTurn = new Dictionary<IntPtr, DateTime>();
        IntPtr prevActiveDofus = IntPtr.Zero;
        public Updater.Info AvailableUpdate;
        bool firstScan = true;
        readonly Dictionary<IntPtr, DateTime> lastFlash = new Dictionary<IntPtr, DateTime>();
        public readonly Icon AppIcon;
        public MainForm Main;
        public BarForm Bar;
        IntPtr mainHandle, barHandle;
        string signature = "";

        public App()
        {
            S = Settings.Load();
            L.En = S.Language == "en";
            S.GetCurrent();
            PruneStats();
            Theme.ApplyBase(S.LightTheme);
            Theme.ApplyAccent(S.AccentColor);
            Theme.ColorOverride = CharColor;
            Theme.ImageProvider = AvatarImage;
            AppIcon = Theme.MakeIcon(Theme.S(32));

            Toast = new Toast();
            Combat = new CombatTracker(this);
            System.Windows.Forms.Timer ct = new System.Windows.Forms.Timer();
            ct.Interval = 1000;
            ct.Tick += delegate { Combat.Tick(); AutoSyncTick(); };
            ct.Start();

            // Formulaire caché pour obtenir un contexte de synchronisation UI
            Main = new MainForm(this);
            mainHandle = Main.Handle;
            ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();

            Bar = new BarForm(this);
            barHandle = Bar.Handle;

            hook = new InputHook();
            hook.OnHotkey = OnHotkey;
            Program.Log("Écoute des raccourcis : " + hook.Status);
            if (!hook.Installed) RetryHook(1);
            else if (!hook.MouseOk) Program.Log("Boutons de souris indisponibles comme raccourcis (code " + hook.MsError + ")");

            // Icône de notification
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add(L.T("Ouvrir Relais"), null, delegate { ShowMain(); });
            trayPause = new ToolStripMenuItem(L.T("Mettre en pause"), null, delegate { TogglePause(); });
            menu.Items.Add(trayPause);
            trayProfiles = new ToolStripMenuItem(L.T("Profil"));
            menu.Items.Add(trayProfiles);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(L.T("Palette de commandes…"), null, delegate { OpenPalette(); });
            menu.Items.Add(L.T("Aperçus en direct"), null, delegate { TogglePreviewWall(); });
            menu.Items.Add(L.T("Superposer les fenêtres"), null, delegate { StackWindows(); });
            menu.Items.Add(L.T("Fenêtres en mosaïque"), null, delegate { MosaicWindows(); });
            menu.Items.Add(L.T("Restaurer la disposition du profil"), null, delegate { ApplyLayout(false); });
            menu.Items.Add(L.T("Copier l'invitation suivante"), null, delegate { InviteNext(); });
            menu.Items.Add(L.T("Afficher / masquer la barre"), null, delegate { ToggleBar(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(L.T("Quitter"), null, delegate { Quit(); });
            tray = new NotifyIcon();
            tray.Icon = AppIcon;
            tray.Text = L.T("Relais — organizer multicompte");
            tray.ContextMenuStrip = menu;
            tray.Visible = true;
            tray.MouseClick += delegate (object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) ShowMain(); };

            ProfileChanged += delegate { RebuildTrayProfiles(); };
            RebuildTrayProfiles();
            RebuildBindings();
            Scan();

            timer = new System.Windows.Forms.Timer();
            timer.Interval = 250;
            timer.Tick += delegate { Scan(); };
            timer.Start();

            if (Autostart.Enabled) Autostart.Enabled = true; // met à jour le chemin si l'exe a été déplacé
            Hover = new HoverPreview();
            Overlays = new OverlayManager(this);
            if (S.PhoneRemote) SetPhone(true);
            Settings.Saved += delegate { syncDirty = true; };
            if (S.ShowBar) Bar.ShowBar();
            if (!S.StartMinimized && !Program.StartHidden) ShowMain();
            if (!S.OnboardingDone)
            {
                System.Windows.Forms.Timer ob = new System.Windows.Forms.Timer();
                ob.Interval = 600;
                ob.Tick += delegate { ob.Stop(); ob.Dispose(); ShowMain(); new Onboarding(this).ShowDialog(Main); };
                ob.Start();
            }
            Updater.CheckInBackground(this, false);
            if (S.OnboardingDone) ShowWhatsNewIfNeeded();
            else S.LastSeenVersion = typeof(App).Assembly.GetName().Version.ToString(3);
            Record("info", L.T("Relais démarré"));
        }

        // ---------------- v2 : tour, aperçus, aller-retour, profil auto, partage ----------------

        public IntPtr LastActive { get { return lastActiveDofus; } }

        public void QuickTimer(int minutes)
        {
            TimerItem t = new TimerItem();
            t.Name = L.T("Minuteur ") + Fmt.Duration(minutes * 60);
            t.Minutes = minutes;
            t.EndUtc = DateTime.UtcNow.AddMinutes(minutes).Ticks;
            S.Timers.Add(t);
            TimersEdited();
            Toast.ShowMessage(L.T("Minuteur lancé : ") + Fmt.Duration(minutes * 60), false);
            Record("timer", L.T("Minuteur lancé (") + Fmt.Duration(minutes * 60) + ")");
        }

        static Cmd C(string group, int icon, string title, string sub, Action run)
        {
            Cmd c = new Cmd();
            c.Group = group; c.Icon = icon; c.Title = title; c.Sub = sub; c.Run = run;
            return c;
        }

        /// <summary>Toutes les commandes proposées par la palette (Ctrl+K).</summary>
        public List<Cmd> BuildCommands()
        {
            List<Cmd> l = new List<Cmd>();
            List<GameWindow> rot = Rotation();
            // persos d'abord
            foreach (GameWindow w in rot)
            {
                string n = w.Name;
                Member m = S.GetCurrent().Find(n);
                Hotkey hk = m == null ? null : Hotkey.Parse(m.Hotkey);
                Cmd c = C(L.T("Perso"), -1, L.T("Aller sur ") + n, (w.Class.Length > 0 ? w.Class : L.T("Personnage")) + (hk != null ? " · " + hk.Display() : ""), delegate { Activate(n); });
                c.Avatar = n; c.AvatarClass = w.Class; c.KeepFocus = true;
                l.Add(c);
            }
            l.Add(C(L.T("Action"), 7, L.T("Superposer les fenêtres"), L.T("Toutes à la place de la fenêtre active"), delegate { StackWindows(); }));
            l.Add(C(L.T("Action"), 7, L.T("Mosaïque"), L.T("Répartir les fenêtres en grille"), delegate { MosaicWindows(); }));
            l.Add(C(L.T("Action"), 7, L.T("Aperçus en direct"), L.T("Tous tes comptes en miniature"), delegate { TogglePreviewWall(); }));
            l.Add(C(L.T("Action"), 7, L.T("Inviter la team"), L.T("Copie le prochain « /invite »"), delegate { InviteNext(); }));
            l.Add(C(L.T("Action"), 7, L.T("Restaurer la disposition"), L.T("Fenêtres du profil à leur place"), delegate { ApplyLayout(false); }));
            l.Add(C(L.T("Action"), 7, L.T("Enregistrer la disposition"), L.T("Mémorise la place de chaque fenêtre"), delegate { SaveLayout(); }));
            l.Add(C(L.T("Action"), 7, L.T("Chef de team"), L.T("Aller sur le perso n°1"), delegate { Leader(); }));
            l[l.Count - 1].KeepFocus = true;
            l.Add(C(L.T("Action"), 7, L.T("Perso d'avant"), L.T("Aller-retour entre les deux derniers persos"), delegate { LastChar(); }));
            l[l.Count - 1].KeepFocus = true;
            l.Add(C(L.T("Action"), 5, Paused ? L.T("Reprendre Relais") : L.T("Mettre Relais en pause"), L.T("Raccourcis suspendus pendant la pause"), delegate { TogglePause(); }));
            l.Add(C(L.T("Action"), 5, S.ShowBar ? L.T("Masquer la mini-barre") : L.T("Afficher la mini-barre"), "", delegate { ToggleBar(); }));
            foreach (Profile p in S.Profiles)
            {
                string pn = p.Name;
                l.Add(C(L.T("Profil"), 0, L.T("Profil : ") + pn, p.Members.Count + L.T(" perso(s)") + (pn == S.GetCurrent().Name ? L.T(" · actuel") : ""), delegate { SwitchProfile(pn); }));
            }
            foreach (GameWindow w in rot)
            {
                string n = w.Name, cl = w.Class;
                Cmd f = C(L.T("Perso"), -1, L.T("Fiche de ") + n, L.T("Niveau, objectifs, quotidien, donjons"), delegate { ShowMain(); OpenSheet(n); });
                f.Avatar = n; f.AvatarClass = cl; f.KeepFocus = true; l.Add(f);
                Cmd inv = C(L.T("Perso"), -1, L.T("Copier /invite ") + n, L.T("Pour l'inviter dans le groupe"), delegate { CopyInvite(n); });
                inv.Avatar = n; inv.AvatarClass = cl; l.Add(inv);
                Cmd mu = C(L.T("Perso"), -1, (VolumeOf(n) == 0 ? L.T("Remettre le son de ") : L.T("Couper le son de ")) + n, L.T("Volume de la fenêtre"), delegate { SetVolume(n, VolumeOf(n) == 0 ? 100 : 0); });
                mu.Avatar = n; mu.AvatarClass = cl; l.Add(mu);
            }
            foreach (TimerItem t in S.Timers)
            {
                TimerItem tt = t;
                l.Add(C(L.T("Minuteur"), 2, (t.EndUtc > 0 ? L.T("Relancer : ") : L.T("Lancer : ")) + t.Name, Fmt.Duration(t.Minutes * 60), delegate
                {
                    tt.EndUtc = DateTime.UtcNow.AddMinutes(tt.Minutes).Ticks; TimersEdited();
                    Toast.ShowMessage(L.T("Minuteur lancé : ") + tt.Name, false);
                }));
            }
            string[] pages = { L.T("Accueil"), "Team", L.T("Raccourcis"), L.T("Minuteurs"), "Craft", "Stats", "Options" };
            int[] icons = { 6, 0, 1, 2, 3, 4, 5 };
            for (int i = 0; i < pages.Length; i++)
            {
                int pi = i;
                Cmd c = C(L.T("Page"), icons[i], L.T("Ouvrir : ") + pages[i], L.T("Fenêtre de Relais"), delegate { ShowMain(); Main.GoTo(pi); });
                c.KeepFocus = true; l.Add(c);
            }
            l.Add(C(L.T("Réglage"), 5, (S.AutoSwitchOnTurn ? L.T("Désactiver") : L.T("Activer")) + L.T(" : aller au perso dont c'est le tour"), "", delegate { S.AutoSwitchOnTurn = !S.AutoSwitchOnTurn; S.Save(); NotifyProfileChanged(); Toast.ShowMessage(L.T("Tour auto ") + (S.AutoSwitchOnTurn ? L.T("activé") : L.T("désactivé")), false); }));
            l.Add(C(L.T("Réglage"), 5, (S.OverlayEnabled ? L.T("Désactiver") : L.T("Activer")) + L.T(" : vignettes en jeu"), "", delegate { S.OverlayEnabled = !S.OverlayEnabled; S.Save(); Overlays.Sync(); NotifyProfileChanged(); }));
            l.Add(C(L.T("Réglage"), 5, (S.SoundsEnabled ? L.T("Couper") : L.T("Activer")) + L.T(" : sons de Relais"), "", delegate { S.SoundsEnabled = !S.SoundsEnabled; S.Save(); NotifyProfileChanged(); Toast.ShowMessage(L.T("Sons ") + (S.SoundsEnabled ? L.T("activés") : L.T("coupés")), false); }));
            l.Add(C(L.T("Réglage"), 5, (S.AudioMuteBackground ? L.T("Désactiver") : L.T("Activer")) + L.T(" : son seulement sur la fenêtre active"), "", delegate { S.AudioMuteBackground = !S.AudioMuteBackground; S.Save(); ApplyAudio(true); NotifyProfileChanged(); }));
            Screen[] screens = Screen.AllScreens;
            if (screens.Length > 1)
                foreach (Screen sc in screens)
                {
                    Screen s2 = sc;
                    l.Add(C(L.T("Action"), 7, L.T("Team sur l'") + ScreenLabel(sc).Substring(0, 1).ToLowerInvariant() + ScreenLabel(sc).Substring(1), L.T("Toutes les fenêtres en mosaïque sur cet écran"), delegate { TeamToScreen(s2); }));
                }
            if (Combat.Active) l.Add(C(L.T("Action"), 7, L.T("Terminer le combat"), L.T("Tour ") + Combat.Round + " · " + Combat.Short(), delegate { Combat.EndNow(); }));
            l.Add(C(L.T("Outil"), 5, S.PhoneRemote ? L.T("Couper la télécommande") : L.T("Activer la télécommande"), L.T("Changer de perso depuis le téléphone"), delegate { SetPhone(!S.PhoneRemote); if (S.PhoneRemote && Phone.Error == null) Toast.ShowMessage(L.T("Télécommande : ") + Phone.Url, false, 6); }));
            if (!string.IsNullOrEmpty(S.GistToken))
                l.Add(C(L.T("Outil"), 5, L.T("Envoyer la sauvegarde en ligne"), "Gist GitHub" + (string.IsNullOrEmpty(S.LastSync) ? "" : L.T(" · dernière : ") + S.LastSync), delegate { CloudUpload(delegate (string m, bool ok) { Toast.ShowMessage(m, !ok, 3); }); }));
            l.Add(C(L.T("Outil"), 5, L.T("Soutenir Relais"), L.T("Un don ou une étoile sur GitHub"), delegate { Links.OpenSupport(this); }));
            l.Add(C(L.T("Outil"), 5, L.T("Vérifier les mises à jour"), L.T("Sur ton dépôt GitHub"), delegate { ShowMain(); Updater.CheckInBackground(this, true); }));
            l[l.Count - 1].KeepFocus = true;
            l.Add(C(L.T("Outil"), 5, L.T("Quoi de neuf ?"), L.T("Les nouveautés de cette version"), delegate { ShowMain(); using (WhatsNew w = new WhatsNew(this)) w.ShowDialog(Main); }));
            l[l.Count - 1].KeepFocus = true;
            l.Add(C(L.T("Outil"), 5, L.T("Quitter Relais"), "", delegate { Quit(); }));
            l[l.Count - 1].KeepFocus = true;
            return l;
        }

        public void OpenPalette()
        {
            CommandPalette.Open(this);
        }

        /// <summary>« Quoi de neuf » au premier lancement d'une nouvelle version.</summary>
        void ShowWhatsNewIfNeeded()
        {
            string v = typeof(App).Assembly.GetName().Version.ToString(3);
            if (S.LastSeenVersion == v) return;
            bool first = string.IsNullOrEmpty(S.LastSeenVersion) && !S.OnboardingDone;
            S.LastSeenVersion = v;
            S.Save();
            if (first) return; // l'assistant de démarrage s'en charge
            System.Windows.Forms.Timer t = new System.Windows.Forms.Timer();
            t.Interval = 900;
            t.Tick += delegate { t.Stop(); t.Dispose(); ShowMain(); using (WhatsNew w = new WhatsNew(this)) w.ShowDialog(Main); };
            t.Start();
        }
        public int FlashingCount { get { return flashing.Count; } }
        public bool IsFlashing(IntPtr h) { return flashing.ContainsKey(h); }

        void ExpireFlashing()
        {
            if (flashing.Count == 0) return;
            IntPtr fg = Native.GetForegroundWindow();
            List<IntPtr> done = new List<IntPtr>();
            foreach (KeyValuePair<IntPtr, DateTime> kv in flashing)
                if (kv.Key == fg || !dofusHandles.Contains(kv.Key) || (DateTime.Now - kv.Value).TotalSeconds > 40) done.Add(kv.Key);
            foreach (IntPtr h in done) flashing.Remove(h);
            if (done.Count > 0 && StateChanged != null) StateChanged(this, EventArgs.Empty);
        }

        public void TogglePreviewWall()
        {
            if (previewWall != null && !previewWall.IsDisposed) { previewWall.Close(); previewWall = null; return; }
            previewWall = new PreviewForm(this);
            previewWall.FormClosed += delegate { previewWall = null; };
            previewWall.Show();
        }

        /// <summary>Retourne sur le perso utilisé juste avant (comme Alt+Tab).</summary>
        public void LastChar()
        {
            foreach (GameWindow w in Windows)
                if (w.Handle == prevActiveDofus) { Activate(w.Name); return; }
            Toast.ShowMessage(L.T("Pas encore de perso précédent"), false);
        }

        /// <summary>Choisit le profil qui contient tous les persos connectés (le plus petit qui convient).</summary>
        void AutoSelectProfile()
        {
            if (!S.AutoProfile || Windows.Count == 0 || S.Profiles.Count < 2) return;
            Profile best = null;
            foreach (Profile p in S.Profiles)
            {
                bool all = true;
                foreach (GameWindow w in Windows) if (p.Find(w.Name) == null) { all = false; break; }
                if (!all) continue;
                if (best == null || p.Members.Count < best.Members.Count) best = p;
            }
            if (best != null && best.Name != S.GetCurrent().Name) SwitchProfile(best.Name);
        }

        public string ExportCode(Profile p)
        {
            Dictionary<string, object> d = new Dictionary<string, object>();
            d["profile"] = p;
            Dictionary<string, string> colors = new Dictionary<string, string>();
            foreach (Member m in p.Members) { string c; if (S.CharColors.TryGetValue(m.Name, out c)) colors[m.Name] = c; }
            d["colors"] = colors;
            string json = Settings.Json().Serialize(d);
            using (System.IO.MemoryStream ms = new System.IO.MemoryStream())
            {
                using (System.IO.Compression.GZipStream gz = new System.IO.Compression.GZipStream(ms, System.IO.Compression.CompressionMode.Compress))
                {
                    byte[] b = System.Text.Encoding.UTF8.GetBytes(json);
                    gz.Write(b, 0, b.Length);
                }
                return "RELAIS2-" + Convert.ToBase64String(ms.ToArray());
            }
        }

        /// <summary>Importe un code de partage ; renvoie le nom du profil créé.</summary>
        public string ImportCode(string code)
        {
            code = (code ?? "").Trim();
            if (!code.StartsWith("RELAIS2-")) throw new FormatException(L.T("Ce n'est pas un code Relais (il doit commencer par RELAIS2-)."));
            byte[] raw = Convert.FromBase64String(code.Substring(8));
            string json;
            using (System.IO.MemoryStream ms = new System.IO.MemoryStream(raw))
            using (System.IO.Compression.GZipStream gz = new System.IO.Compression.GZipStream(ms, System.IO.Compression.CompressionMode.Decompress))
            using (System.IO.StreamReader r = new System.IO.StreamReader(gz, System.Text.Encoding.UTF8))
                json = r.ReadToEnd();
            Dictionary<string, object> d = Settings.Json().Deserialize<Dictionary<string, object>>(json);
            Profile p = Settings.Json().ConvertToType<Profile>(d["profile"]);
            if (p == null || string.IsNullOrEmpty(p.Name)) throw new FormatException(L.T("Code incomplet."));
            if (p.Members == null) p.Members = new List<Member>();
            string baseName = p.Name, name = baseName;
            for (int k = 2; ; k++)
            {
                bool exists = false;
                foreach (Profile q in S.Profiles) if (string.Equals(q.Name, name, StringComparison.OrdinalIgnoreCase)) exists = true;
                if (!exists) break;
                name = baseName + " (" + k + ")";
            }
            p.Name = name;
            if (p.Hotkey != null && Owner(p.Hotkey, null) != null) p.Hotkey = null;
            foreach (Member m in p.Members) if (m.Hotkey != null && Owner(m.Hotkey, null) != null) m.Hotkey = null;
            object colorsObj;
            if (d.TryGetValue("colors", out colorsObj) && colorsObj is Dictionary<string, object>)
                foreach (KeyValuePair<string, object> kv in (Dictionary<string, object>)colorsObj)
                    if (!S.CharColors.ContainsKey(kv.Key) && kv.Value is string) S.CharColors[kv.Key] = (string)kv.Value;
            S.Profiles.Add(p);
            ProfileEdited();
            return name;
        }

        /// <summary>Redémarre Relais (ex. changement de thème).</summary>
        public void Restart()
        {
            try
            {
                System.Diagnostics.Process.Start(Application.ExecutablePath, "--relaunch");
                Quit();
            }
            catch (Exception ex) { Program.Log("Redémarrage : " + ex.Message); }
        }

        // ---------------- détection ----------------

        int scanTick;
        bool pendingAutoProfile;
        void Scan()
        {
            // scan complet toutes les ~1 s, suivi du premier plan toutes les 250 ms
            bool full = (scanTick++ % 4) == 0;
            bool changed = false;
            if (full)
            {
                List<GameWindow> list = WindowScanner.Scan();
                string sig = "";
                foreach (GameWindow w in list) sig += w.Handle.ToInt64() + ":" + w.Name + "|";
                if (sig != signature)
                {
                    signature = sig;
                    DetectDisconnects(list);
                    pendingAutoProfile = true;
                    Windows = list;
                    byName.Clear();
                    HashSet<IntPtr> hs = new HashSet<IntPtr>();
                    foreach (GameWindow w in list) { byName[w.Name] = w; hs.Add(w.Handle); }
                    dofusHandles = hs;
                    changed = true;
                    if (pendingAutoProfile) { pendingAutoProfile = false; AutoSelectProfile(); }
                    AutoAdd();
                }
            }
            IntPtr fg = Native.GetForegroundWindow();
            if (fg != ForegroundHandle) { ForegroundHandle = fg; changed = true; }
            if (changed || full) { UpdateLastActive(); ApplyPriority(false); ApplyAudio(false); UpdateFallbackRegistration(); }
            AccumulateStats();
            ExpireFlashing();
            if (scanTick % 4 == 1) CheckTimers();
            if (scanTick % 12 == 2) ApplyAudio(true);
            if (changed)
            {
                string cur = null;
                foreach (GameWindow w in Windows) if (w.Handle == lastActiveDofus) cur = w.Name;
                string tip = Paused ? L.T("Relais — en pause") : L.T("Relais · ") + Windows.Count + L.T(" perso(s)") + (cur != null ? " · " + cur : "");
                if (tip.Length > 63) tip = tip.Substring(0, 63);
                if (tray.Text != tip) tray.Text = tip;
            }
            if (changed && StateChanged != null) StateChanged(this, EventArgs.Empty);
        }

        // ---------------- performance : priorité CPU ----------------

        IntPtr lastActiveDofus = IntPtr.Zero;
        readonly Dictionary<uint, System.Diagnostics.ProcessPriorityClass> prioApplied = new Dictionary<uint, System.Diagnostics.ProcessPriorityClass>();

        void UpdateLastActive()
        {
            IntPtr fg = Native.GetForegroundWindow();
            if (dofusHandles.Contains(fg))
            {
                if (fg != lastActiveDofus && lastActiveDofus != IntPtr.Zero) prevActiveDofus = lastActiveDofus;
                lastActiveDofus = fg;
            }
            else if (!dofusHandles.Contains(lastActiveDofus)) lastActiveDofus = IntPtr.Zero;
        }

        public void ApplyPriority(bool restoreAll)
        {
            foreach (GameWindow w in Windows)
            {
                System.Diagnostics.ProcessPriorityClass want;
                if (restoreAll || !S.PriorityBoost) want = System.Diagnostics.ProcessPriorityClass.Normal;
                else if (lastActiveDofus == IntPtr.Zero) continue;
                else want = w.Handle == lastActiveDofus ? System.Diagnostics.ProcessPriorityClass.AboveNormal : System.Diagnostics.ProcessPriorityClass.BelowNormal;
                System.Diagnostics.ProcessPriorityClass cur;
                if (prioApplied.TryGetValue(w.Pid, out cur) && cur == want) continue;
                if (!prioApplied.ContainsKey(w.Pid) && want == System.Diagnostics.ProcessPriorityClass.Normal) continue; // jamais touché
                try
                {
                    using (System.Diagnostics.Process p = System.Diagnostics.Process.GetProcessById((int)w.Pid)) p.PriorityClass = want;
                    prioApplied[w.Pid] = want;
                }
                catch (Exception ex) { prioApplied[w.Pid] = want; Program.Log("Priorité : " + ex.Message); }
            }
        }

        // ---------------- son ----------------

        bool audioTouched;

        public void ApplyAudio(bool force)
        {
            bool custom = false;
            foreach (KeyValuePair<string, int> kv in S.CharVolume) if (kv.Value != 100) { custom = true; break; }
            bool active = S.AudioMuteBackground || custom;
            if (!active && !audioTouched) return;
            List<GameWindow> rot = Rotation();
            string leader = rot.Count > 0 ? rot[0].Name : null;
            Dictionary<uint, int> targets = new Dictionary<uint, int>();
            foreach (GameWindow w in Windows)
            {
                int vol = 100;
                if (active)
                {
                    vol = VolumeOf(w.Name);
                    if (S.AudioMuteBackground)
                    {
                        bool audible = w.Handle == lastActiveDofus || lastActiveDofus == IntPtr.Zero
                            || (S.AudioKeepLeader && leader != null && string.Equals(w.Name, leader, StringComparison.OrdinalIgnoreCase));
                        if (!audible) vol = -1;
                    }
                }
                targets[w.Pid] = vol;
            }
            AudioMixer.Apply(targets, force);
            audioTouched = active;
        }

        public int VolumeOf(string name)
        {
            int v;
            return name != null && S.CharVolume.TryGetValue(name, out v) ? v : 100;
        }

        public void SetVolume(string name, int v)
        {
            if (v >= 100) S.CharVolume.Remove(name); else S.CharVolume[name] = Math.Max(0, v);
            S.Save();
            ApplyAudio(true);
            Toast.ShowMessage(L.T("Volume de ") + name + " : " + v + " %", false);
        }

        // ---------------- statistiques ----------------

        DateTime lastStatTick = DateTime.UtcNow;
        DateTime lastStatSave = DateTime.UtcNow;

        public static string Today { get { return DateTime.Now.ToString("yyyy-MM-dd"); } }

        void AccumulateStats()
        {
            DateTime now = DateTime.UtcNow;
            double dt = (now - lastStatTick).TotalSeconds;
            lastStatTick = now;
            if (dt <= 0 || dt > 5) return;
            if (Windows.Count == 0) return;
            string day = Today;
            Dictionary<string, StatEntry> d;
            if (!S.Stats.TryGetValue(day, out d)) { d = new Dictionary<string, StatEntry>(); S.Stats[day] = d; }
            IntPtr fg = ForegroundHandle;
            foreach (GameWindow w in Windows)
            {
                StatEntry e;
                if (!d.TryGetValue(w.Name, out e)) { e = new StatEntry(); d[w.Name] = e; }
                e.Online += dt;
                if (w.Handle == fg) e.Active += dt;
            }
            if (Rotation().Count > 0)
            {
                string pk = "@" + S.GetCurrent().Name;
                StatEntry pe;
                if (!d.TryGetValue(pk, out pe)) { pe = new StatEntry(); d[pk] = pe; }
                pe.Online += dt;
                if (dofusHandles.Contains(fg)) pe.Active += dt;
            }
            if ((now - lastStatSave).TotalSeconds > 60) { lastStatSave = now; S.Save(); }
        }

        void PruneStats()
        {
            string limit = DateTime.Now.AddDays(-370).ToString("yyyy-MM-dd");
            List<string> old = new List<string>();
            foreach (string k in S.Stats.Keys) if (string.CompareOrdinal(k, limit) < 0) old.Add(k);
            foreach (string k in old) S.Stats.Remove(k);
        }

        public void ResetStats()
        {
            S.Stats.Clear();
            S.Save();
        }

        // ---------------- minuteurs & rappels ----------------

        public event EventHandler TimersChanged;

        void CheckTimers()
        {
            long now = DateTime.UtcNow.Ticks;
            bool changed = false;
            foreach (TimerItem t in S.Timers)
            {
                if (t.EndUtc > 0 && now >= t.EndUtc)
                {
                    t.EndUtc = 0;
                    changed = true;
                    Alarm(L.T("Minuteur terminé : ") + t.Name);
                }
            }
            DateTime local = DateTime.Now;
            string today = Today, hm = local.ToString("HH:mm");
            foreach (Reminder r in S.Reminders)
            {
                if (!r.Enabled || string.IsNullOrEmpty(r.Time) || r.LastFired == today) continue;
                if (string.CompareOrdinal(hm, r.Time) >= 0)
                {
                    r.LastFired = today;
                    changed = true;
                    Alarm(L.T("Rappel : ") + r.Name);
                }
            }
            if (changed) { S.Save(); if (TimersChanged != null) TimersChanged(this, EventArgs.Empty); }
        }

        public void Alarm(string text)
        {
            Toast.ShowMessage(text, false, 8);
            try { if (S.SoundsEnabled) Sounds.Play(S, Sounds.Kind.Timer); else System.Media.SystemSounds.Asterisk.Play(); } catch { }
            Record("timer", text);
            try { tray.ShowBalloonTip(6000, "Relais", text, ToolTipIcon.Info); } catch { }
            Program.Log(text);
            if (S.RemoteTimers) Remote.Send(S, "Relais", text, false);
        }

        public void TimersEdited()
        {
            S.Save();
            if (TimersChanged != null) TimersChanged(this, EventArgs.Empty);
        }

        // ---------------- invitations (presse-papiers) ----------------

        int inviteIndex;
        DateTime lastInvite = DateTime.MinValue;

        public static bool CopyText(string text)
        {
            for (int i = 0; i < 5; i++)
            {
                try { Clipboard.SetText(text); return true; }
                catch { System.Threading.Thread.Sleep(30); }
            }
            return false;
        }

        public void CopyInvite(string name)
        {
            if (CopyText(L.T("/invite ") + name)) Toast.ShowMessage(L.T("Copié : /invite ") + name + L.T(" — colle-le dans le chat"), false);
        }

        /// <summary>Copie l'invitation du perso suivant de la team (le chef invite les autres).</summary>
        public void InviteNext()
        {
            List<GameWindow> rot = Rotation();
            if (rot.Count < 2) { Toast.ShowMessage(L.T("Il faut au moins 2 persos connectés"), false); return; }
            int n = rot.Count - 1;
            if ((DateTime.Now - lastInvite).TotalSeconds > 90 || inviteIndex >= n) inviteIndex = 0;
            lastInvite = DateTime.Now;
            string name = rot[inviteIndex + 1].Name;
            if (CopyText(L.T("/invite ") + name))
                Toast.ShowMessage(L.T("Copié : /invite ") + name + " (" + (inviteIndex + 1) + "/" + n + L.T(") — colle dans le chat de ") + rot[0].Name, false, 4);
            inviteIndex++;
        }

        // ---------------- dispositions de fenêtres ----------------

        public void SaveLayout()
        {
            List<WinPos> list = new List<WinPos>();
            foreach (GameWindow w in Windows)
            {
                Native.RECT r;
                if (!Native.GetWindowRect(w.Handle, out r)) continue;
                WinPos p = new WinPos();
                p.Name = w.Name; p.X = r.Left; p.Y = r.Top; p.W = r.Right - r.Left; p.H = r.Bottom - r.Top;
                p.Max = Native.IsZoomed(w.Handle);
                list.Add(p);
            }
            if (list.Count == 0) { Toast.ShowMessage(L.T("Aucune fenêtre à enregistrer"), false); return; }
            S.GetCurrent().Layout = list;
            S.Save();
            Toast.ShowMessage(L.T("Disposition enregistrée pour « ") + S.GetCurrent().Name + " » (" + list.Count + L.T(" fenêtres)"), false);
            if (ProfileChanged != null) ProfileChanged(this, EventArgs.Empty);
        }

        public void ApplyLayout(bool quiet)
        {
            List<WinPos> layout = S.GetCurrent().Layout;
            if (layout == null || layout.Count == 0)
            {
                if (!quiet) Toast.ShowMessage(L.T("Aucune disposition enregistrée pour ce profil"), false);
                return;
            }
            List<WindowLayout.Target> plan = new List<WindowLayout.Target>();
            foreach (WinPos p in layout)
            {
                GameWindow w = WindowOf(p.Name);
                if (w == null) continue;
                WindowLayout.Target t = new WindowLayout.Target();
                t.Win = w;
                t.Want = new Rectangle(p.X, p.Y, Math.Max(200, p.W), Math.Max(150, p.H));
                t.Maximize = p.Max;
                plan.Add(t);
            }
            RunLayout(plan, L.T("restaurée"), IntPtr.Zero);
        }

        // ---------------- apparence des persos ----------------

        readonly Dictionary<string, Image> imgCache = new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);

        public Color? CharColor(string name)
        {
            string hex;
            if (name == null || !S.CharColors.TryGetValue(name, out hex)) return null;
            try { return ColorTranslator.FromHtml(hex); } catch { return null; }
        }

        public Image AvatarImage(string name)
        {
            if (name == null) return null;
            Image img;
            if (imgCache.TryGetValue(name, out img)) return img;
            string path;
            img = null;
            if (S.CharImages.TryGetValue(name, out path) && System.IO.File.Exists(path))
            {
                try
                {
                    using (System.IO.MemoryStream ms = new System.IO.MemoryStream(System.IO.File.ReadAllBytes(path)))
                    using (Image tmp = Image.FromStream(ms))
                        img = new Bitmap(tmp);
                }
                catch { img = null; }
            }
            imgCache[name] = img;
            return img;
        }

        public void SetCharColor(string name, Color? c)
        {
            if (c == null) S.CharColors.Remove(name);
            else S.CharColors[name] = ColorTranslator.ToHtml(c.Value);
            S.Save();
            if (ProfileChanged != null) ProfileChanged(this, EventArgs.Empty);
        }

        /// <summary>Importe une image : recadrée en carré, 128 px, copiée dans le dossier de Relais.</summary>
        public void SetCharImage(string name, string sourcePath)
        {
            Image old;
            if (imgCache.TryGetValue(name, out old) && old != null) old.Dispose();
            imgCache.Remove(name);
            if (sourcePath == null) { S.CharImages.Remove(name); S.Save(); if (ProfileChanged != null) ProfileChanged(this, EventArgs.Empty); return; }
            string dir = System.IO.Path.Combine(Settings.Dir, "avatars");
            System.IO.Directory.CreateDirectory(dir);
            string safe = "";
            foreach (char ch in name) safe += char.IsLetterOrDigit(ch) ? ch : '_';
            string dest = System.IO.Path.Combine(dir, safe + "_" + DateTime.Now.Ticks + ".png");
            using (Image src = Image.FromFile(sourcePath))
            {
                int side = Math.Min(src.Width, src.Height);
                using (Bitmap b = new Bitmap(128, 128))
                {
                    using (Graphics g = Graphics.FromImage(b))
                    {
                        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                        g.DrawImage(src, new Rectangle(0, 0, 128, 128), new Rectangle((src.Width - side) / 2, (src.Height - side) / 2, side, side), GraphicsUnit.Pixel);
                    }
                    b.Save(dest, System.Drawing.Imaging.ImageFormat.Png);
                }
            }
            S.CharImages[name] = dest;
            S.Save();
            if (ProfileChanged != null) ProfileChanged(this, EventArgs.Empty);
        }

        public void NotifyProfileChanged()
        {
            if (ProfileChanged != null) ProfileChanged(this, EventArgs.Empty);
        }

        public void OpenSheet(string name)
        {
            using (SheetForm f = new SheetForm(this, name)) f.ShowDialog(Main.Visible ? (IWin32Window)Main : null);
        }

        /// <summary>Capture la fenêtre de jeu du perso puis propose de recadrer l'image pour son avatar.</summary>
        public void CaptureAvatar(string name)
        {
            GameWindow w = WindowOf(name);
            if (w == null) { Toast.ShowMessage(name + L.T(" n'est pas connecté"), false); return; }
            Switcher.Activate(w.Handle);
            System.Windows.Forms.Timer t = new System.Windows.Forms.Timer();
            t.Interval = 450;
            t.Tick += delegate
            {
                t.Stop(); t.Dispose();
                Bitmap shot = null;
                try
                {
                    Rectangle r = WindowLayout.RectOf(w.Handle);
                    Rectangle vs = SystemInformation.VirtualScreen;
                    r.Intersect(vs);
                    if (r.Width < 50 || r.Height < 50) throw new InvalidOperationException(L.T("fenêtre trop petite ou hors écran"));
                    shot = new Bitmap(r.Width, r.Height);
                    using (Graphics g = Graphics.FromImage(shot)) g.CopyFromScreen(r.Location, Point.Empty, r.Size);
                }
                catch (Exception ex)
                {
                    if (shot != null) shot.Dispose();
                    MessageBox.Show(Main, L.T("Capture impossible : ") + ex.Message, "Relais");
                    return;
                }
                ShowMain();
                using (CropForm f = new CropForm(shot, name))
                {
                    if (f.ShowDialog(Main) == DialogResult.OK && f.Result != null) SetCharImageBitmap(name, f.Result);
                }
            };
            t.Start();
        }

        public void SetCharImageBitmap(string name, Bitmap b)
        {
            Image old;
            if (imgCache.TryGetValue(name, out old) && old != null) old.Dispose();
            imgCache.Remove(name);
            string dir = System.IO.Path.Combine(Settings.Dir, "avatars");
            System.IO.Directory.CreateDirectory(dir);
            string safe = "";
            foreach (char ch in name) safe += char.IsLetterOrDigit(ch) ? ch : '_';
            string dest = System.IO.Path.Combine(dir, safe + "_" + DateTime.Now.Ticks + ".png");
            b.Save(dest, System.Drawing.Imaging.ImageFormat.Png);
            b.Dispose();
            S.CharImages[name] = dest;
            S.Save();
            NotifyProfileChanged();
            Toast.ShowMessage(L.T("Avatar de ") + name + L.T(" mis à jour"), false);
        }

        public void SetNote(string name, string note)
        {
            if (string.IsNullOrEmpty(note) || note.Trim().Length == 0) S.Notes.Remove(name);
            else S.Notes[name] = note;
            S.Save();
            if (ProfileChanged != null) ProfileChanged(this, EventArgs.Empty);
        }

        public string NoteOf(string name)
        {
            string n;
            return name != null && S.Notes.TryGetValue(name, out n) ? n : null;
        }

        public void SetAccent(Color c)
        {
            S.AccentColor = ColorTranslator.ToHtml(c);
            S.Save();
            Theme.ApplyAccent(S.AccentColor);
            foreach (Form f in Application.OpenForms) f.Invalidate(true);
        }

        void DetectDisconnects(List<GameWindow> now)
        {
            if (firstScan) { firstScan = false; return; }
            if (!S.AlertOnDisconnect) return;
            HashSet<string> still = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (GameWindow w in now) still.Add(w.Name);
            List<string> lost = new List<string>();
            foreach (GameWindow w in Windows)
            {
                if (still.Contains(w.Name)) continue;
                Member m = S.GetCurrent().Find(w.Name);
                if (m != null && m.Enabled) lost.Add(w.Name);
            }
            if (lost.Count == 0) return;
            string who = string.Join(", ", lost.ToArray());
            Toast.ShowMessage(lost.Count == 1 ? who + L.T(" s'est déconnecté") : who + L.T(" se sont déconnectés"), true);
            if (S.AlertSound) { if (S.SoundsEnabled) Sounds.Play(S, Sounds.Kind.Disconnect); else System.Media.SystemSounds.Exclamation.Play(); }
            Record("deco", lost.Count == 1 ? who + L.T(" s'est déconnecté") : who + L.T(" se sont déconnectés"));
            Program.Log("Déconnexion : " + who);
            if (S.RemoteDisconnect) Remote.Send(S, L.T("Déconnexion"), who + (lost.Count == 1 ? L.T(" s'est déconnecté") : L.T(" se sont déconnectés")), false);
        }

        void AutoAdd()
        {
            if (!S.AutoAddNewCharacters) return;
            Profile p = S.GetCurrent();
            bool added = false;
            foreach (GameWindow w in Windows)
            {
                if (p.Find(w.Name) == null)
                {
                    Member m = new Member();
                    m.Name = w.Name;
                    p.Members.Add(m);
                    added = true;
                }
            }
            if (added) { S.Save(); RebuildBindings(); if (ProfileChanged != null) ProfileChanged(this, EventArgs.Empty); }
        }

        public GameWindow WindowOf(string name)
        {
            GameWindow w;
            return name != null && byName.TryGetValue(name, out w) ? w : null;
        }

        /// <summary>Persos actifs et connectés, dans l'ordre d'initiative.</summary>
        public List<GameWindow> Rotation()
        {
            List<GameWindow> r = new List<GameWindow>();
            foreach (Member m in S.GetCurrent().Members)
            {
                if (!m.Enabled) continue;
                GameWindow w = WindowOf(m.Name);
                if (w != null) r.Add(w);
            }
            return r;
        }

        public string ForegroundName
        {
            get
            {
                foreach (GameWindow w in Windows) if (w.Handle == ForegroundHandle) return w.Name;
                return null;
            }
        }

        // ---------------- actions ----------------

        public void Activate(string name)
        {
            GameWindow w = WindowOf(name);
            if (w == null) return;
            bool changedChar = Native.GetForegroundWindow() != w.Handle;
            Switcher.Activate(w.Handle);
            ForegroundHandle = Native.GetForegroundWindow();
            if (changedChar) Sounds.Play(S, Sounds.Kind.Switch);
            if (StateChanged != null) StateChanged(this, EventArgs.Empty);
        }

        public void Step(int dir)
        {
            List<GameWindow> rot = Rotation();
            if (rot.Count == 0) return;
            IntPtr fg = Native.GetForegroundWindow();
            int i = rot.FindIndex(delegate (GameWindow w) { return w.Handle == fg; });
            int n = rot.Count;
            int next = i < 0 ? (dir > 0 ? 0 : n - 1) : ((i + dir) % n + n) % n;
            Activate(rot[next].Name);
        }

        public void Leader()
        {
            List<GameWindow> rot = Rotation();
            if (rot.Count > 0) Activate(rot[0].Name);
        }

        public bool GameOrOwnForeground()
        {
            IntPtr fg = Native.GetForegroundWindow();
            return dofusHandles.Contains(fg) || fg == mainHandle || fg == barHandle;
        }

        public void StackWindows()
        {
            List<GameWindow> rot = Rotation();
            if (rot.Count < 2) { Toast.ShowMessage(L.T("Il faut au moins 2 persos connectés"), false); return; }
            IntPtr fg = Native.GetForegroundWindow();
            IntPtr reference = dofusHandles.Contains(fg) ? fg : rot[0].Handle;
            if (Native.IsIconic(reference)) Native.ShowWindow(reference, Native.SW_RESTORE);
            if (WindowLayout.IsFullscreen(reference))
            {
                Toast.ShowMessage(L.T("Cette fenêtre Dofus est en plein écran : passe Dofus en mode fenêtré pour superposer"), true, 6);
                Program.Log("Superposer : référence en plein écran\r\n  " + WindowLayout.Describe(WindowOfHandle(reference)));
                return;
            }
            RunLayout(WindowLayout.PlanStack(rot, reference), L.T("alignée"), reference);
        }

        public void MosaicWindows()
        {
            List<GameWindow> rot = Rotation();
            if (rot.Count == 0) { Toast.ShowMessage(L.T("Aucun perso connecté"), false); return; }
            IntPtr fg = Native.GetForegroundWindow();
            Screen chosen = ScreenByName(S.LayoutScreen);
            RunLayout(WindowLayout.PlanMosaic(rot, dofusHandles.Contains(fg) ? fg : rot[0].Handle, chosen != null ? chosen.WorkingArea : Rectangle.Empty), L.T("placée"), IntPtr.Zero);
        }

        GameWindow WindowOfHandle(IntPtr h)
        {
            foreach (GameWindow w in Windows) if (w.Handle == h) return w;
            GameWindow g = new GameWindow(); g.Handle = h; g.Name = "?"; g.Class = ""; return g;
        }

        /// <summary>Applique un plan de positions puis vérifie ce que Dofus a réellement accepté.</summary>
        void RunLayout(List<WindowLayout.Target> plan, string verb, IntPtr activateAfter)
        {
            if (plan.Count == 0) { Toast.ShowMessage(L.T("Rien à déplacer"), false); return; }
            HashSet<IntPtr> fullscreen = new HashSet<IntPtr>();
            foreach (WindowLayout.Target t in plan) if (WindowLayout.IsFullscreen(t.Win.Handle)) fullscreen.Add(t.Win.Handle);
            WindowLayout.Apply(plan);
            if (activateAfter != IntPtr.Zero) Switcher.Activate(activateAfter);

            System.Windows.Forms.Timer check = new System.Windows.Forms.Timer();
            check.Interval = 700;
            check.Tick += delegate
            {
                check.Stop();
                check.Dispose();
                List<WindowLayout.Target> failed = new List<WindowLayout.Target>();
                foreach (WindowLayout.Target t in plan) if (!WindowLayout.Matches(t)) failed.Add(t);
                int ok = plan.Count - failed.Count;
                if (failed.Count == 0)
                {
                    Toast.ShowMessage(ok + L.T(" fenêtre") + (ok > 1 ? "s" : "") + " " + verb + (ok > 1 && !L.En ? "s" : ""), false);
                    return;
                }
                StringBuilderLog(L.T("Rangement : ") + failed.Count + "/" + plan.Count + L.T(" fenêtre(s) refusée(s)"), failed);
                bool denied = false, full = false, tooSmall = false;
                foreach (WindowLayout.Target t in failed)
                {
                    if (t.Denied) denied = true;
                    if (fullscreen.Contains(t.Win.Handle) || WindowLayout.IsFullscreen(t.Win.Handle)) full = true;
                    System.Drawing.Rectangle r = WindowLayout.RectOf(t.Win.Handle);
                    if (!t.Maximize && (r.Width > t.Want.Width + 16 || r.Height > t.Want.Height + 16)) tooSmall = true;
                }
                string who = failed.Count == 1 ? failed[0].Win.Name : failed.Count + L.T(" fenêtres");
                if (denied)
                    Toast.ShowMessage(L.T("Windows bloque le déplacement : Dofus est lancé en admin. Options > Relancer Relais en admin"), true, 8);
                else if (full)
                    Toast.ShowMessage(who + L.T(" en plein écran : passe Dofus en mode fenêtré (options d'affichage du jeu)"), true, 8);
                else if (tooSmall)
                    Toast.ShowMessage(who + L.T(" : Dofus impose une taille minimale, la grille est trop petite pour cet écran"), true, 8);
                else
                    Toast.ShowMessage(who + L.T(" n'a pas bougé — Options > Diagnostic des fenêtres, puis envoie-le à Claude"), true, 8);
            };
            check.Start();
        }

        static void StringBuilderLog(string title, List<WindowLayout.Target> failed)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder(title);
            foreach (WindowLayout.Target t in failed)
                sb.Append("\r\n  voulu " + t.Want.X + "," + t.Want.Y + " " + t.Want.Width + "×" + t.Want.Height + (t.Denied ? L.T(" (accès refusé)") : "") + "\r\n  " + WindowLayout.Describe(t.Win));
            Program.Log(sb.ToString());
        }

        /// <summary>Écran choisi dans les options (null = automatique ou écran débranché).</summary>
        public static Screen ScreenByName(string device)
        {
            if (string.IsNullOrEmpty(device)) return null;
            foreach (Screen s in Screen.AllScreens) if (s.DeviceName == device) return s;
            return null;
        }

        /// <summary>« Écran 2 · 1920×1080 » pour l'interface.</summary>
        public static string ScreenLabel(Screen s)
        {
            Screen[] all = Screen.AllScreens;
            int idx = Array.IndexOf(all, s) + 1;
            return L.T("Écran ") + idx + " · " + s.Bounds.Width + "×" + s.Bounds.Height + (s.Primary ? L.T(" (principal)") : "");
        }

        /// <summary>Envoie toute la team en mosaïque sur un écran précis.</summary>
        public void TeamToScreen(Screen s)
        {
            List<GameWindow> rot = Rotation();
            if (rot.Count == 0 || s == null) return;
            RunLayout(WindowLayout.PlanMosaic(rot, rot[0].Handle, s.WorkingArea), L.T("placée"), IntPtr.Zero);
        }

        /// <summary>Texte complet de diagnostic (copiable).</summary>
        public string Diagnostic()
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            bool? me = Native.IsElevated((uint)System.Diagnostics.Process.GetCurrentProcess().Id);
            sb.Append(L.T("Relais ") + typeof(App).Assembly.GetName().Version + L.T(" — admin : ") + (me == true ? "OUI" : "non") + "\r\n");
            sb.Append(L.T("Windows ") + Environment.OSVersion.Version + (Environment.Is64BitProcess ? L.T(" · 64 bits") : L.T(" · 32 bits")) + "\r\n");
            sb.Append(L.T("Écoute des raccourcis : ") + HookStatus + "\r\n");
            foreach (Screen sc in Screen.AllScreens)
                sb.Append(L.T("Écran ") + sc.DeviceName + " : " + sc.Bounds.Width + "×" + sc.Bounds.Height + L.T(" à ") + sc.Bounds.X + "," + sc.Bounds.Y + (sc.Primary ? L.T(" (principal)") : "") + "\r\n");
            sb.Append("\r\n" + Windows.Count + L.T(" fenêtre(s) Dofus détectée(s) :\r\n"));
            foreach (GameWindow w in Windows) sb.Append("• " + WindowLayout.Describe(w) + "\r\n");
            return sb.ToString();
        }

        public void SwitchProfile(string name)
        {
            Profile target = null;
            foreach (Profile p in S.Profiles) if (p.Name == name) target = p;
            if (target == null) return;
            if (S.CurrentProfile != name)
            {
                S.CurrentProfile = name;
                ProfileEdited();
                if (S.AutoApplyLayout) ApplyLayout(true);
            }
            Toast.ShowMessage(L.T("Profil : ") + name, false);
            Record("profile", L.T("Profil « ") + name + " »");
        }

        void RebuildTrayProfiles()
        {
            trayProfiles.DropDownItems.Clear();
            foreach (Profile p in S.Profiles)
            {
                string name = p.Name;
                ToolStripMenuItem it = new ToolStripMenuItem(name, null, delegate { SwitchProfile(name); });
                it.Checked = name == S.GetCurrent().Name;
                trayProfiles.DropDownItems.Add(it);
            }
        }

        /// <summary>Message du « shell hook » Windows (reçu par la fenêtre principale).</summary>
        public void OnShellMessage(int code, IntPtr h)
        {
            if (code == Native.HSHELL_WINDOWDESTROYED) { scanTick = 0; return; } // rescan immédiat
            if (code != Native.HSHELL_FLASH || !dofusHandles.Contains(h)) return;
            IntPtr fg = Native.GetForegroundWindow();
            if (fg == h) return;
            GameWindow gw = null;
            foreach (GameWindow w in Rotation()) if (w.Handle == h) gw = w;
            if (gw == null) return;
            bool isNew = !flashing.ContainsKey(h);
            flashing[h] = DateTime.Now;
            if (isNew)
            {
                if (StateChanged != null) StateChanged(this, EventArgs.Empty);
                Sounds.Play(S, Sounds.Kind.Turn);
                Record("turn", L.T("Tour de ") + gw.Name);
                Combat.Signal(gw.Name, true);
            }
            if (S.RemoteTurn)
            {
                DateTime lr;
                if (!lastRemoteTurn.TryGetValue(h, out lr) || (DateTime.Now - lr).TotalSeconds > 20)
                {
                    lastRemoteTurn[h] = DateTime.Now;
                    Remote.Send(S, "Dofus", gw.Name + L.T(" te réclame (début de tour ou notification)"), false);
                }
            }
            if (!S.AutoSwitchOnTurn || Paused) return;
            // ne vole pas le focus si tu es dans une autre application
            if (S.OnlyWhenDofusFocused && !dofusHandles.Contains(fg) && fg != mainHandle && fg != barHandle) return;
            DateTime last;
            if (lastFlash.TryGetValue(h, out last) && (DateTime.Now - last).TotalMilliseconds < 1500) return;
            lastFlash[h] = DateTime.Now;
            foreach (GameWindow w in Rotation())
                if (w.Handle == h) { Activate(w.Name); return; }
        }

        public void TogglePause()
        {
            Paused = !Paused;
            Toast.ShowMessage(Paused ? L.T("Relais en pause") : L.T("Relais actif"), false);
            trayPause.Text = Paused ? L.T("Reprendre") : L.T("Mettre en pause");
            tray.Text = Paused ? L.T("Relais — en pause") : L.T("Relais — organizer multicompte");
            if (StateChanged != null) StateChanged(this, EventArgs.Empty);
        }

        public void ToggleBar()
        {
            S.ShowBar = !S.ShowBar;
            S.Save();
            if (S.ShowBar) Bar.ShowBar(); else Bar.Hide();
            if (ProfileChanged != null) ProfileChanged(this, EventArgs.Empty);
        }

        public void ShowMain()
        {
            Main.Show();
            if (Main.WindowState == FormWindowState.Minimized) Main.WindowState = FormWindowState.Normal;
            Main.Activate();
        }

        public void RelaunchAsAdmin()
        {
            try
            {
                System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo(Application.ExecutablePath, "--relaunch");
                psi.UseShellExecute = true;
                psi.Verb = "runas";
                System.Diagnostics.Process.Start(psi);
                Quit();
            }
            catch (Exception ex) { Program.Log("Relance admin annulée : " + ex.Message); }
        }

        public void Quit()
        {
            timer.Stop();
            if (fallback != null) try { fallback.Dispose(); } catch { }
            try { ApplyPriority(true); } catch { }
            try
            {
                if (audioTouched)
                {
                    Dictionary<uint, int> all = new Dictionary<uint, int>();
                    foreach (GameWindow w in Windows) all[w.Pid] = 100;
                    AudioMixer.Apply(all, true);
                }
            }
            catch { }
            hook.Dispose();
            if (Phone != null) Phone.Stop();
            if (!SkipSaveOnExit)
            {
                S.Save();
                if (S.AutoSync && !string.IsNullOrEmpty(S.GistToken) && syncDirty)
                {
                    // dernier envoi, limité à quelques secondes
                    string payload = CloudSync.Payload(S);
                    Thread t = new Thread(delegate () { try { CloudSync.Upload(S, payload); } catch { } });
                    t.IsBackground = true; t.Start(); t.Join(6000);
                    S.Save();
                }
            }
            tray.Visible = false;
            tray.Dispose();
            Bar.Close();
            Toast.Close();
            try { Overlays.CloseAll(); } catch { }
            try { Hover.Close(); } catch { }
            if (previewWall != null) try { previewWall.Close(); } catch { }
            Main.AllowClose = true;
            Main.Close();
            ExitThread();
        }

        // ---------------- raccourcis ----------------

        public void ProfileEdited()
        {
            S.Save();
            RebuildBindings();
            if (ProfileChanged != null) ProfileChanged(this, EventArgs.Empty);
        }

        public void RebuildBindings()
        {
            Dictionary<string, Action> b = new Dictionary<string, Action>(StringComparer.OrdinalIgnoreCase);
            foreach (Member m in S.GetCurrent().Members)
            {
                Hotkey hk = Hotkey.Parse(m.Hotkey);
                if (hk == null || !m.Enabled) continue;
                string name = m.Name;
                b[hk.ToString()] = delegate { Activate(name); };
            }
            Bind(b, S.KeyNext, delegate { Step(+1); });
            Bind(b, S.KeyPrev, delegate { Step(-1); });
            Bind(b, S.KeyLeader, delegate { Leader(); });
            Bind(b, S.KeyToggleBar, delegate { ToggleBar(); });
            Bind(b, S.KeyPause, delegate { TogglePause(); });
            Bind(b, S.KeyStack, delegate { StackWindows(); });
            Bind(b, S.KeyMosaic, delegate { MosaicWindows(); });
            Bind(b, S.KeyInvite, delegate { InviteNext(); });
            Bind(b, S.KeyLayout, delegate { ApplyLayout(false); });
            Bind(b, S.KeyPreview, delegate { TogglePreviewWall(); });
            Bind(b, S.KeyPalette, delegate { OpenPalette(); });
            Bind(b, S.KeyLastChar, delegate { LastChar(); });
            foreach (Profile prof in S.Profiles)
            {
                string pn = prof.Name;
                Bind(b, prof.Hotkey, delegate { SwitchProfile(pn); });
            }
            if (S.WheelCycle)
            {
                string down = new Hotkey(Hotkey.ALT, Hotkey.WHEELDOWN).ToString(), up = new Hotkey(Hotkey.ALT, Hotkey.WHEELUP).ToString();
                if (!b.ContainsKey(down)) b[down] = delegate { Step(+1); };
                if (!b.ContainsKey(up)) b[up] = delegate { Step(-1); };
            }
            Hotkey p = Hotkey.Parse(S.KeyPause);
            pauseKey = p == null ? null : p.ToString();
            bindings = b;
            if (fallback != null) fallback.Rebuild(b);
        }

        static void Bind(Dictionary<string, Action> b, string key, Action a)
        {
            Hotkey hk = Hotkey.Parse(key);
            if (hk != null) b[hk.ToString()] = a;
        }

        // ---------------- écoute des raccourcis : nouvel essai puis mode de secours ----------------

        FallbackHotkeys fallback;
        public string HookStatus { get { return hook == null ? L.T("non démarrée") : hook.Status + (fallback != null ? L.T(" — mode de secours (clavier via Windows)") : ""); } }

        void RetryHook(int attempt)
        {
            System.Windows.Forms.Timer t = new System.Windows.Forms.Timer();
            t.Interval = 1000;
            t.Tick += delegate
            {
                t.Stop(); t.Dispose();
                if (hook != null && hook.Installed) return;
                InputHook h = new InputHook();
                if (h.Installed)
                {
                    if (hook != null) hook.Dispose();
                    hook = h;
                    hook.OnHotkey = OnHotkey;
                    Program.Log("Écoute des raccourcis installée au essai " + (attempt + 1));
                    return;
                }
                h.Dispose();
                if (attempt < 4) { RetryHook(attempt + 1); return; }
                Program.Log("Écoute des raccourcis impossible : " + hook.Status + " — passage en mode de secours");
                try { fallback = new FallbackHotkeys(this); fallback.Rebuild(bindings); UpdateFallbackRegistration(); }
                catch (Exception ex) { Program.Log("Mode de secours : " + ex.Message); fallback = null; }
                Toast.ShowMessage(L.T("Raccourcis en mode de secours : clavier uniquement (boutons de souris indisponibles)"), true, 8);
            };
            t.Start();
        }

        /// <summary>Raccourci reçu par le mode de secours (RegisterHotKey).</summary>
        public void OnFallbackHotkey(string id)
        {
            Action a;
            if (!bindings.TryGetValue(id, out a)) return;
            bool isPause = string.Equals(id, pauseKey, StringComparison.OrdinalIgnoreCase);
            if (Paused && !isPause) return;
            a();
        }

        /// <summary>Capture d'un raccourci au clavier quand l'écoute globale n'est pas disponible.</summary>
        public bool HandleCaptureKey(Keys keyData)
        {
            if (capturingBox == null || (hook != null && hook.Installed)) return false;
            Keys key = keyData & Keys.KeyCode;
            if (key == Keys.ControlKey || key == Keys.ShiftKey || key == Keys.Menu || key == Keys.LWin || key == Keys.RWin) return true;
            int mods = 0;
            if ((keyData & Keys.Control) != 0) mods |= Hotkey.CTRL;
            if ((keyData & Keys.Alt) != 0) mods |= Hotkey.ALT;
            if ((keyData & Keys.Shift) != 0) mods |= Hotkey.SHIFT;
            HotkeyBox box = capturingBox;
            capturingBox = null;
            if (mods == 0 && key == Keys.Escape) { box.Cancel(); return true; }
            if (mods == 0 && (key == Keys.Back || key == Keys.Delete)) { box.SetFromCapture(null); return true; }
            box.SetFromCapture(new Hotkey(mods, key.ToString()).ToString());
            return true;
        }

        void UpdateFallbackRegistration()
        {
            if (fallback == null) return;
            IntPtr fg = Native.GetForegroundWindow();
            bool want = !S.OnlyWhenDofusFocused || dofusHandles.Contains(fg) || fg == mainHandle || fg == barHandle;
            fallback.SetActive(want);
        }

        /// <summary>Appelé depuis le hook : doit rester très rapide.</summary>
        bool OnHotkey(Hotkey hk)
        {
            string id = hk.ToString();
            Action a;
            if (!bindings.TryGetValue(id, out a)) return false;
            bool isPause = string.Equals(id, pauseKey, StringComparison.OrdinalIgnoreCase);
            if (Paused && !isPause) return false;
            if (S.OnlyWhenDofusFocused)
            {
                IntPtr fg = Native.GetForegroundWindow();
                if (!dofusHandles.Contains(fg) && fg != mainHandle && fg != barHandle) return false;
            }
            ui.Post(delegate { a(); }, null);
            return true;
        }

        /// <summary>Capture du prochain raccourci pressé (pour les réglages).</summary>
        HotkeyBox capturingBox;

        public void Capture(HotkeyBox box)
        {
            if (capturingBox != null && capturingBox != box) capturingBox.Cancel();
            capturingBox = box;
            if (hook == null || !hook.Installed) return; // capture par la fenêtre (HandleCaptureKey)
            hook.CaptureCallback = delegate (Hotkey hk)
            {
                ui.Post(delegate
                {
                    if (capturingBox == box) capturingBox = null;
                    if (hk.Mods == 0 && hk.Key == "Escape") { box.Cancel(); return; }
                    if (hk.Mods == 0 && (hk.Key == "Back" || hk.Key == "Delete")) { box.SetFromCapture(null); return; }
                    box.SetFromCapture(hk.ToString());
                }, null);
            };
        }

        public void CancelCapture()
        {
            if (hook != null) hook.CaptureCallback = null;
            HotkeyBox b = capturingBox;
            capturingBox = null;
            if (b != null) b.Cancel();
        }

        /// <summary>Retourne à qui appartient déjà ce raccourci (pour éviter les doublons).</summary>
        public string Owner(string key, object except)
        {
            Hotkey hk = Hotkey.Parse(key);
            if (hk == null) return null;
            string id = hk.ToString();
            foreach (Member m in S.GetCurrent().Members)
                if (!ReferenceEquals(m, except) && Same(m.Hotkey, id)) return m.Name;
            if (!"next".Equals(except) && Same(S.KeyNext, id)) return L.T("Perso suivant");
            if (!"prev".Equals(except) && Same(S.KeyPrev, id)) return L.T("Perso précédent");
            if (!"leader".Equals(except) && Same(S.KeyLeader, id)) return L.T("Chef de team");
            if (!"bar".Equals(except) && Same(S.KeyToggleBar, id)) return L.T("Afficher la barre");
            if (!"pause".Equals(except) && Same(S.KeyPause, id)) return "Pause";
            if (!"stack".Equals(except) && Same(S.KeyStack, id)) return L.T("Superposer les fenêtres");
            if (!"mosaic".Equals(except) && Same(S.KeyMosaic, id)) return L.T("Mosaïque");
            if (!"invite".Equals(except) && Same(S.KeyInvite, id)) return L.T("Invitation suivante");
            if (!"layout".Equals(except) && Same(S.KeyLayout, id)) return L.T("Restaurer la disposition");
            if (!"preview".Equals(except) && Same(S.KeyPreview, id)) return L.T("Aperçus en direct");
            if (!"palette".Equals(except) && Same(S.KeyPalette, id)) return L.T("Palette de commandes");
            if (!"lastchar".Equals(except) && Same(S.KeyLastChar, id)) return L.T("Perso d'avant");
            foreach (Profile p in S.Profiles)
                if (!ReferenceEquals(p, except) && Same(p.Hotkey, id)) return L.T("Profil « ") + p.Name + " »";
            return null;
        }

        static bool Same(string a, string id)
        {
            Hotkey h = Hotkey.Parse(a);
            return h != null && string.Equals(h.ToString(), id, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>Lancement automatique avec Windows (clé « Run » de l'utilisateur, aucun droit admin).</summary>
    public static class Autostart
    {
        const string KEY = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string NAME = "Relais";

        public static bool Enabled
        {
            get
            {
                try
                {
                    using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(KEY))
                        return k != null && k.GetValue(NAME) != null;
                }
                catch { return false; }
            }
            set
            {
                try
                {
                    using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(KEY))
                    {
                        if (value) k.SetValue(NAME, "\"" + Application.ExecutablePath + "\" --tray");
                        else if (k.GetValue(NAME) != null) k.DeleteValue(NAME);
                    }
                }
                catch (Exception ex) { Program.Log("Autostart : " + ex.Message); }
            }
        }
    }

    /// <summary>
    /// Mode de secours si Windows refuse l'écoute globale du clavier/souris :
    /// raccourcis clavier enregistrés auprès de Windows (RegisterHotKey). Pas de boutons de souris dans ce mode.
    /// </summary>
    public sealed class FallbackHotkeys : NativeWindow, IDisposable
    {
        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)] static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint vk);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr h, int id);

        readonly App app;
        readonly Dictionary<int, string> ids = new Dictionary<int, string>();
        List<string> keys = new List<string>();
        bool active;

        public FallbackHotkeys(App app)
        {
            this.app = app;
            CreateParams cp = new CreateParams();
            cp.Caption = "RelaisHotkeys";
            CreateHandle(cp);
        }

        public void Rebuild(Dictionary<string, Action> bindings)
        {
            keys = new List<string>(bindings.Keys);
            if (active) { UnregisterAll(); RegisterAll(); }
        }

        public void SetActive(bool on)
        {
            if (on == active) return;
            active = on;
            if (on) RegisterAll(); else UnregisterAll();
        }

        void RegisterAll()
        {
            int id = 1;
            foreach (string k in keys)
            {
                Hotkey hk = Hotkey.Parse(k);
                if (hk == null || hk.IsMouse) continue;
                Keys vk;
                try { vk = (Keys)Enum.Parse(typeof(Keys), hk.Key, true); } catch { continue; }
                uint mods = 0x4000; // MOD_NOREPEAT
                if ((hk.Mods & Hotkey.ALT) != 0) mods |= 1;
                if ((hk.Mods & Hotkey.CTRL) != 0) mods |= 2;
                if ((hk.Mods & Hotkey.SHIFT) != 0) mods |= 4;
                if ((hk.Mods & Hotkey.WIN) != 0) mods |= 8;
                bool ok = false;
                try { ok = RegisterHotKey(Handle, id, mods, (uint)vk); } catch (Exception ex) { Program.Log("RegisterHotKey : " + ex.Message); return; }
                if (ok) ids[id] = hk.ToString();
                else Program.Log("Raccourci de secours refusé : " + hk.Display() + " (déjà pris par une autre appli ?)");
                id++;
            }
        }

        void UnregisterAll()
        {
            foreach (int id in ids.Keys) { try { UnregisterHotKey(Handle, id); } catch { } }
            ids.Clear();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0312) // WM_HOTKEY
            {
                string k;
                if (ids.TryGetValue(m.WParam.ToInt32(), out k)) app.OnFallbackHotkey(k);
                return;
            }
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            UnregisterAll();
            DestroyHandle();
        }
    }

    internal static class Program
    {
        public static string LogPath
        {
            get { return System.IO.Path.Combine(Settings.Dir, "journal.txt"); }
        }

        public static void Log(string msg)
        {
            try
            {
                System.IO.Directory.CreateDirectory(Settings.Dir);
                System.IO.File.AppendAllText(LogPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + msg + Environment.NewLine);
            }
            catch { }
        }

        public static bool StartHidden;

        static bool crashing;
        static void Crash(Exception ex, bool fatal)
        {
            string text = ex == null ? L.T("Erreur inconnue") : ex.ToString();
            Log((fatal ? L.T("ERREUR FATALE : ") : L.T("ERREUR : ")) + text);
            if (crashing) return;
            crashing = true;
            try
            {
                Clipboard.SetText(text);
            }
            catch { }
            MessageBox.Show(L.T("Relais a rencontré une erreur") + (fatal ? L.T(" et doit se fermer") : "") + ".\n\n" +
                (ex == null ? "" : ex.GetType().Name + " : " + ex.Message) +
                L.T("\n\nLe détail a été copié dans le presse-papiers et enregistré dans :\n") + LogPath +
                L.T("\n\nColle-le à Claude pour qu'il corrige."), L.T("Relais — erreur"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            crashing = false;
            if (fatal) Environment.Exit(1);
        }

        [STAThread]
        static void Main(string[] args)
        {
            bool relaunch = false;
            foreach (string a in args)
            {
                if (a == "--tray" || a == "--minimized") StartHidden = true;
                if (a == "--relaunch") relaunch = true;
            }
            using (Mutex mutex = new Mutex(false, "Relais_Organizer_SingleInstance"))
            {
                bool created;
                try { created = mutex.WaitOne(relaunch ? 10000 : 0); }
                catch (AbandonedMutexException) { created = true; }
                if (!created)
                {
                    MessageBox.Show(L.T("Relais est déjà lancé (regarde dans la zone de notification, en bas à droite)."), "Relais",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                Log(L.T("Démarrage de Relais ") + typeof(Program).Assembly.GetName().Version + " — " + Environment.OSVersion + L.T(", .NET ") + Environment.Version + (Environment.Is64BitProcess ? L.T(" 64 bits") : L.T(" 32 bits")));
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += delegate (object s, System.Threading.ThreadExceptionEventArgs e) { Crash(e.Exception, false); };
                AppDomain.CurrentDomain.UnhandledException += delegate (object s, UnhandledExceptionEventArgs e) { Crash(e.ExceptionObject as Exception, true); };
                try
                {
                    try { Native.SetProcessDPIAware(); } catch { }
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    ToolStripManager.Renderer = new WoodRenderer();
                    App app = new App();
                    Log(L.T("Interface prête."));
                    Application.Run(app);
                }
                catch (Exception ex)
                {
                    Crash(ex, true);
                }
                try { mutex.ReleaseMutex(); } catch { }
            }
        }
    }
}

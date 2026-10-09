using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Relais
{
    /// <summary>Une fenêtre de jeu Dofus détectée.</summary>
    public sealed class GameWindow
    {
        public IntPtr Handle;
        public uint Pid;
        public string Name;   // nom du personnage
        public string Class;  // classe (si lisible dans le titre)
        public string Title;
    }

    /// <summary>Recherche des fenêtres Dofus ouvertes (Dofus 3 / Unity et Dofus Rétro).</summary>
    public static class WindowScanner
    {
        public static readonly string[] Classes = {
            "Feca", "Osamodas", "Enutrof", "Sram", "Xélor", "Xelor", "Ecaflip", "Eniripsa", "Iop", "Crâ", "Cra",
            "Sadida", "Sacrieur", "Pandawa", "Roublard", "Zobal", "Steamer", "Eliotrope", "Huppermage",
            "Ouginak", "Forgelance"
        };

        static readonly Dictionary<uint, bool> pidCache = new Dictionary<uint, bool>();
        static readonly int selfPid = Process.GetCurrentProcess().Id;

        static bool IsDofusProcess(uint pid)
        {
            bool r;
            if (pidCache.TryGetValue(pid, out r)) return r;
            r = false;
            try
            {
                if (pid != selfPid)
                {
                    string n = Process.GetProcessById((int)pid).ProcessName.ToLowerInvariant();
                    r = n.Contains("dofus") && !n.Contains("launcher") && !n.Contains("organizer") && !n.Contains("relais");
                }
            }
            catch { }
            if (pidCache.Count > 512) pidCache.Clear();
            pidCache[pid] = r;
            return r;
        }

        public static List<GameWindow> Scan()
        {
            List<GameWindow> list = new List<GameWindow>();
            Native.EnumWindows(delegate (IntPtr h, IntPtr l)
            {
                if (!Native.IsWindowVisible(h)) return true;
                // fenêtres principales uniquement (pas de propriétaire)
                if (Native.GetWindow(h, 4 /*GW_OWNER*/) != IntPtr.Zero) return true;
                string title = Native.GetTitle(h);
                if (title.Length == 0) return true;
                uint pid;
                Native.GetWindowThreadProcessId(h, out pid);
                if (!IsDofusProcess(pid)) return true;

                GameWindow w = ParseTitle(title);
                if (w == null) return true; // écran de connexion / sélection de perso
                w.Handle = h;
                w.Pid = pid;
                list.Add(w);
                return true;
            }, IntPtr.Zero);
            return list;
        }

        /// <summary>
        /// Dofus 3 : « Pseudo - Classe - 3.x.x.x - Release »
        /// Dofus Rétro : « Pseudo - Dofus Retro v1.xx »
        /// Tant qu'aucun perso n'est connecté, le titre vaut « Dofus … » : on l'ignore.
        /// </summary>
        public static GameWindow ParseTitle(string title)
        {
            string[] parts = title.Split(new string[] { " - " }, StringSplitOptions.None);
            string name = parts[0].Trim();
            if (name.Length == 0 || parts.Length < 2) return null;
            if (name.StartsWith("Dofus", StringComparison.OrdinalIgnoreCase)) return null;

            GameWindow w = new GameWindow();
            w.Title = title;
            w.Name = name;
            w.Class = "";
            string c = parts[1].Trim();
            foreach (string k in Classes)
                if (string.Equals(k, c, StringComparison.OrdinalIgnoreCase)) { w.Class = k; break; }
            return w;
        }
    }

    /// <summary>Met une fenêtre au premier plan de façon fiable, sans envoyer aucune entrée au jeu.</summary>
    public static class Switcher
    {
        public static bool Activate(IntPtr h)
        {
            if (h == IntPtr.Zero || !Native.IsWindow(h)) return false;
            if (Native.GetForegroundWindow() == h) return true;

            if (Native.IsIconic(h)) Native.ShowWindow(h, Native.SW_RESTORE);

            IntPtr fg = Native.GetForegroundWindow();
            uint dummy;
            uint fgThread = fg == IntPtr.Zero ? 0 : Native.GetWindowThreadProcessId(fg, out dummy);
            uint me = Native.GetCurrentThreadId();
            bool attached = false;
            if (fgThread != 0 && fgThread != me) attached = Native.AttachThreadInput(me, fgThread, true);
            try
            {
                Native.BringWindowToTop(h);
                Native.SetForegroundWindow(h);
            }
            finally
            {
                if (attached) Native.AttachThreadInput(me, fgThread, false);
            }

            if (Native.GetForegroundWindow() != h)
                Native.SwitchToThisWindow(h, true);
            return Native.GetForegroundWindow() == h;
        }
    }

    /// <summary>Rangement des fenêtres de jeu (même taille/position, ou mosaïque), avec vérification du résultat.</summary>
    public static class WindowLayout
    {
        const uint FLAGS = Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_NOOWNERZORDER;

        public sealed class Target
        {
            public GameWindow Win;
            public System.Drawing.Rectangle Want;
            public bool Maximize;
            public bool Denied;
        }

        /// <summary>Fenêtre sans bordure qui couvre tout l'écran (mode « plein écran » / « plein écran fenêtré » de Dofus).</summary>
        public static bool IsFullscreen(IntPtr h)
        {
            Native.RECT r;
            if (!Native.GetWindowRect(h, out r)) return false;
            int style = Native.GetWindowLong(h, Native.GWL_STYLE);
            if ((style & Native.WS_CAPTION) == Native.WS_CAPTION) return false;
            System.Drawing.Rectangle b = System.Windows.Forms.Screen.FromHandle(h).Bounds;
            return r.Left <= b.Left && r.Top <= b.Top && r.Right >= b.Right && r.Bottom >= b.Bottom;
        }

        public static System.Drawing.Rectangle RectOf(IntPtr h)
        {
            Native.RECT r;
            Native.GetWindowRect(h, out r);
            return System.Drawing.Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
        }

        /// <summary>Applique les positions. Les fenêtres refusent parfois : le résultat est vérifié plus tard (Verify).</summary>
        public static void Apply(List<Target> targets)
        {
            foreach (Target t in targets)
            {
                IntPtr h = t.Win.Handle;
                if (Native.IsIconic(h) || Native.IsZoomed(h)) Native.ShowWindow(h, Native.SW_RESTORE);
                bool ok = Native.SetWindowPos(h, IntPtr.Zero, t.Want.X, t.Want.Y, t.Want.Width, t.Want.Height, FLAGS);
                if (!ok)
                {
                    int err = Marshal.GetLastWin32Error();
                    if (err == 5) t.Denied = true; // ERROR_ACCESS_DENIED : Dofus lancé en administrateur
                    else
                    {
                        // nouvel essai asynchrone (si la fenêtre est occupée)
                        Native.SetWindowPos(h, IntPtr.Zero, t.Want.X, t.Want.Y, t.Want.Width, t.Want.Height, FLAGS | Native.SWP_ASYNCWINDOWPOS);
                    }
                    Program.Log("SetWindowPos " + t.Win.Name + " : erreur " + err);
                }
                if (t.Maximize) Native.ShowWindow(h, Native.SW_MAXIMIZE);
            }
        }

        /// <summary>Compare la position obtenue à celle voulue (tolérance de quelques pixels).</summary>
        public static bool Matches(Target t)
        {
            if (t.Maximize) return Native.IsZoomed(t.Win.Handle);
            System.Drawing.Rectangle r = RectOf(t.Win.Handle);
            return Math.Abs(r.X - t.Want.X) <= 8 && Math.Abs(r.Y - t.Want.Y) <= 8
                && Math.Abs(r.Width - t.Want.Width) <= 16 && Math.Abs(r.Height - t.Want.Height) <= 16;
        }

        public static List<Target> PlanStack(List<GameWindow> wins, IntPtr reference)
        {
            List<Target> list = new List<Target>();
            bool refMax = Native.IsZoomed(reference);
            System.Drawing.Rectangle r = RectOf(reference);
            foreach (GameWindow w in wins)
            {
                if (w.Handle == reference) continue;
                Target t = new Target();
                t.Win = w;
                t.Maximize = refMax;
                t.Want = refMax ? System.Windows.Forms.Screen.FromHandle(reference).WorkingArea : r;
                list.Add(t);
            }
            return list;
        }

        public static List<Target> PlanMosaic(List<GameWindow> wins, IntPtr reference)
        {
            return PlanMosaic(wins, reference, System.Drawing.Rectangle.Empty);
        }

        /// <summary>Mosaïque sur une zone donnée (écran choisi) ou, si vide, sur l'écran de la fenêtre de référence.</summary>
        public static List<Target> PlanMosaic(List<GameWindow> wins, IntPtr reference, System.Drawing.Rectangle area)
        {
            List<Target> list = new List<Target>();
            int count = wins.Count;
            if (count == 0) return list;
            System.Drawing.Rectangle wa = !area.IsEmpty ? area : System.Windows.Forms.Screen.FromHandle(reference != IntPtr.Zero ? reference : wins[0].Handle).WorkingArea;
            int cols = (int)Math.Ceiling(Math.Sqrt(count));
            int rows = (int)Math.Ceiling(count / (double)cols);
            int cw = wa.Width / cols, ch = wa.Height / rows;
            for (int i = 0; i < count; i++)
            {
                Target t = new Target();
                t.Win = wins[i];
                t.Want = new System.Drawing.Rectangle(wa.X + (i % cols) * cw, wa.Y + (i / cols) * ch, cw, ch);
                list.Add(t);
            }
            return list;
        }

        /// <summary>Rapport détaillé des fenêtres (pour le diagnostic).</summary>
        public static string Describe(GameWindow w)
        {
            IntPtr h = w.Handle;
            System.Drawing.Rectangle r = RectOf(h);
            int style = Native.GetWindowLong(h, Native.GWL_STYLE);
            bool? elev = Native.IsElevated(w.Pid);
            string mode = IsFullscreen(h) ? "PLEIN ÉCRAN (sans bordure)" : Native.IsZoomed(h) ? "agrandie" : Native.IsIconic(h) ? "réduite" : "fenêtrée";
            return w.Name + " — " + mode
                + "\r\n    position " + r.X + "," + r.Y + "  taille " + r.Width + "×" + r.Height
                + "\r\n    style 0x" + style.ToString("X8") + " · classe " + Native.GetClass(h) + " · pid " + w.Pid
                + " · admin : " + (elev == null ? "inconnu (probablement oui)" : elev.Value ? "OUI" : "non");
        }
    }

    /// <summary>
    /// Hooks bas niveau clavier + souris. Une pression physique = au plus une bascule de fenêtre.
    /// Aucun événement n'est jamais rejoué ni envoyé vers le jeu.
    /// </summary>
    public sealed class InputHook : IDisposable
    {
        /// <summary>Retourne true si le raccourci a été consommé.</summary>
        public Func<Hotkey, bool> OnHotkey;
        /// <summary>Si non nul, le prochain raccourci est capturé (réglage) au lieu d'être exécuté.</summary>
        public Action<Hotkey> CaptureCallback;

        readonly Native.LowLevelProc kbProc, msProc; // références gardées pour le GC
        IntPtr kbHook, msHook;
        readonly HashSet<string> swallowedDown = new HashSet<string>();

        public InputHook()
        {
            kbProc = KeyboardProc;
            msProc = MouseProc;
            kbHook = Install(Native.WH_KEYBOARD_LL, kbProc, out KbError);
            msHook = Install(Native.WH_MOUSE_LL, msProc, out MsError);
        }

        public int KbError, MsError;

        /// <summary>Essaie plusieurs « modules » : certains PC refusent l'un mais acceptent l'autre.</summary>
        static IntPtr Install(int type, Native.LowLevelProc proc, out int error)
        {
            IntPtr[] mods = { Native.GetModuleHandle(null), Native.GetModuleHandle("user32.dll"), IntPtr.Zero };
            error = 0;
            foreach (IntPtr m in mods)
            {
                IntPtr h = Native.SetWindowsHookEx(type, proc, m, 0);
                if (h != IntPtr.Zero) { error = 0; return h; }
                error = Marshal.GetLastWin32Error();
            }
            return IntPtr.Zero;
        }

        public string Status
        {
            get
            {
                return "clavier " + (kbHook != IntPtr.Zero ? "OK" : "échec (code " + KbError + ")")
                    + ", souris " + (msHook != IntPtr.Zero ? "OK" : "échec (code " + MsError + ")");
            }
        }

        /// <summary>Clavier écouté (l'essentiel). La souris peut échouer seule : voir MouseOk.</summary>
        public bool Installed { get { return kbHook != IntPtr.Zero; } }
        public bool MouseOk { get { return msHook != IntPtr.Zero; } }

        static int CurrentMods()
        {
            int m = 0;
            if (Native.IsDown(0x11)) m |= Hotkey.CTRL;
            if (Native.IsDown(0x12)) m |= Hotkey.ALT;
            if (Native.IsDown(0x10)) m |= Hotkey.SHIFT;
            if (Native.IsDown(0x5B) || Native.IsDown(0x5C)) m |= Hotkey.WIN;
            return m;
        }

        static bool IsModifierVk(uint vk)
        {
            return vk == 0x10 || vk == 0x11 || vk == 0x12 || vk == 0xA0 || vk == 0xA1 || vk == 0xA2 ||
                   vk == 0xA3 || vk == 0xA4 || vk == 0xA5 || vk == 0x5B || vk == 0x5C;
        }

        IntPtr KeyboardProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                Native.KBDLLHOOKSTRUCT k = (Native.KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(Native.KBDLLHOOKSTRUCT));
                int msg = wParam.ToInt32();
                bool injected = (k.flags & Native.LLKHF_INJECTED) != 0;
                if (!injected && !IsModifierVk(k.vkCode))
                {
                    string keyName = ((System.Windows.Forms.Keys)k.vkCode).ToString();
                    if (msg == Native.WM_KEYDOWN || msg == Native.WM_SYSKEYDOWN)
                    {
                        if (swallowedDown.Contains(keyName)) return (IntPtr)1; // répétition auto : ignorée
                        Hotkey hk = new Hotkey(CurrentMods(), keyName);
                        if (Handle(hk)) { swallowedDown.Add(keyName); return (IntPtr)1; }
                    }
                    else if (msg == Native.WM_KEYUP || msg == Native.WM_SYSKEYUP)
                    {
                        if (swallowedDown.Remove(keyName)) return (IntPtr)1;
                    }
                }
            }
            return Native.CallNextHookEx(kbHook, nCode, wParam, lParam);
        }

        IntPtr MouseProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int msg = wParam.ToInt32();
                if (msg == 0x020A /*WM_MOUSEWHEEL*/)
                {
                    // molette : seulement avec une touche modificatrice (la molette seule reste au jeu)
                    int mods = CurrentMods();
                    if (mods != 0)
                    {
                        Native.MSLLHOOKSTRUCT m = (Native.MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(Native.MSLLHOOKSTRUCT));
                        if ((m.flags & Native.LLMHF_INJECTED) == 0)
                        {
                            short delta = (short)((m.mouseData >> 16) & 0xFFFF);
                            string key = delta > 0 ? Hotkey.WHEELUP : Hotkey.WHEELDOWN;
                            if (Handle(new Hotkey(mods, key))) return (IntPtr)1;
                        }
                    }
                }
                else if (msg == Native.WM_XBUTTONDOWN || msg == Native.WM_XBUTTONUP || msg == Native.WM_MBUTTONDOWN || msg == Native.WM_MBUTTONUP)
                {
                    Native.MSLLHOOKSTRUCT m = (Native.MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(Native.MSLLHOOKSTRUCT));
                    if ((m.flags & Native.LLMHF_INJECTED) == 0)
                    {
                        string key;
                        if (msg == Native.WM_MBUTTONDOWN || msg == Native.WM_MBUTTONUP) key = Hotkey.MOUSEMID;
                        else key = ((m.mouseData >> 16) & 0xFFFF) == 1 ? Hotkey.MOUSE4 : Hotkey.MOUSE5;

                        if (msg == Native.WM_XBUTTONDOWN || msg == Native.WM_MBUTTONDOWN)
                        {
                            if (Handle(new Hotkey(CurrentMods(), key))) { swallowedDown.Add(key); return (IntPtr)1; }
                        }
                        else if (swallowedDown.Remove(key)) return (IntPtr)1;
                    }
                }
            }
            return Native.CallNextHookEx(msHook, nCode, wParam, lParam);
        }

        bool Handle(Hotkey hk)
        {
            Action<Hotkey> cap = CaptureCallback;
            if (cap != null)
            {
                CaptureCallback = null;
                cap(hk);
                return true;
            }
            Func<Hotkey, bool> f = OnHotkey;
            return f != null && f(hk);
        }

        public void Dispose()
        {
            if (kbHook != IntPtr.Zero) { Native.UnhookWindowsHookEx(kbHook); kbHook = IntPtr.Zero; }
            if (msHook != IntPtr.Zero) { Native.UnhookWindowsHookEx(msHook); msHook = IntPtr.Zero; }
        }
    }
}

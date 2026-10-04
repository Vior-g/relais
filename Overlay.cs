using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Relais
{
    /// <summary>
    /// Vignette discrète dessinée par-dessus chaque fenêtre de jeu : nom, numéro d'ordre, minuteur,
    /// cadre coloré sur le perso actif et clignotement quand c'est son tour.
    /// La fenêtre est « attachée » au client Dofus (elle le suit et passe sous les autres applis avec lui)
    /// et laisse passer tous les clics : elle n'interagit jamais avec le jeu.
    /// </summary>
    public sealed class OverlayWindow : Form
    {
        [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr h, out Native.RECT r);
        [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr h, ref Native.POINT p);

        static readonly Color Key = Color.FromArgb(1, 2, 3);
        readonly App app;
        public readonly IntPtr Game;
        public string Name2 = "", Class2 = "", TimerText = "";
        public int Order;
        public bool Active, Flashing;
        int phase;

        public OverlayWindow(App app, IntPtr game)
        {
            this.app = app;
            Game = game;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = Key;
            TransparencyKey = Key;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            Bounds = new Rectangle(-32000, -32000, 10, 10);
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.Parent = Game; // fenêtre « possédée » par le client Dofus : reste juste au-dessus de lui
                cp.ExStyle |= Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW | 0x20 /*TRANSPARENT : clics traversants*/ | 0x80000 /*LAYERED*/;
                return cp;
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_MOUSEACTIVATE) { m.Result = (IntPtr)Native.MA_NOACTIVATE; return; }
            if (m.Msg == 0x0084 /*WM_NCHITTEST*/) { m.Result = (IntPtr)(-1) /*HTTRANSPARENT*/; return; }
            base.WndProc(ref m);
        }

        public Rectangle GameClientRect()
        {
            Native.RECT r;
            if (!GetClientRect(Game, out r)) return Rectangle.Empty;
            Native.POINT p = new Native.POINT();
            if (!ClientToScreen(Game, ref p)) return Rectangle.Empty;
            return new Rectangle(p.X, p.Y, r.Right - r.Left, r.Bottom - r.Top);
        }

        public void Track()
        {
            if (!Native.IsWindow(Game) || Native.IsIconic(Game) || !Native.IsWindowVisible(Game))
            {
                if (Visible) Hide();
                return;
            }
            Rectangle r = GameClientRect();
            if (r.Width < 50 || r.Height < 50) { if (Visible) Hide(); return; }
            if (Bounds != r) Bounds = r;
            if (!Visible) Show();
        }

        public void Tick()
        {
            if (Flashing) { phase++; Invalidate(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Key);
            Color c = Theme.ForCharacter(Name2, Class2);

            // cadre : perso actif (couleur du perso) ou tour en attente (clignote en couleur d'accent)
            bool showFrame = app.S.OverlayFrame && (Active || Flashing);
            if (showFrame)
            {
                Color fc = Flashing && !Active ? ((phase / 4) % 2 == 0 ? Theme.Accent : Key) : c;
                if (fc != Key)
                {
                    int t = Theme.S(3);
                    using (SolidBrush b = new SolidBrush(fc))
                    {
                        g.FillRectangle(b, 0, 0, Width, t);
                        g.FillRectangle(b, 0, Height - t, Width, t);
                        g.FillRectangle(b, 0, 0, t, Height);
                        g.FillRectangle(b, Width - t, 0, t, Height);
                    }
                }
            }

            // pastille : avatar + numéro + nom (+ minuteur)
            string label = Order + ". " + Name2;
            string sub = app.S.OverlayShowTimer ? TimerText : "";
            Font f = Theme.SmallBold;
            Size ts = TextRenderer.MeasureText(label, f);
            Size ss = sub.Length > 0 ? TextRenderer.MeasureText(sub, Theme.Small) : Size.Empty;
            int av = Theme.S(18);
            int w = Theme.S(10) + av + Theme.S(6) + Math.Max(ts.Width, ss.Width) + Theme.S(10);
            int h = sub.Length > 0 ? Theme.S(40) : Theme.S(26);
            int m = Theme.S(8);
            int corner = app.S.OverlayCorner;
            int x = (corner == 1 || corner == 3) ? Width - w - m : m;
            int y = (corner == 2 || corner == 3) ? Height - h - m : m;
            Rectangle pill = new Rectangle(x, y, w, h);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
            using (SolidBrush b = new SolidBrush(Theme.Sidebar)) g.FillRectangle(b, pill);
            using (Pen p = new Pen(Flashing ? Theme.Accent : (Active ? c : Theme.Border))) g.DrawRectangle(p, pill.X, pill.Y, pill.Width - 1, pill.Height - 1);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            Theme.Avatar(g, new RectangleF(x + Theme.S(8), y + (sub.Length > 0 ? Theme.S(4) : (h - av) / 2f), av, av), Name2, Class2, false);
            int tx = x + Theme.S(8) + av + Theme.S(6);
            TextRenderer.DrawText(g, label, f, new Rectangle(tx, y + (sub.Length > 0 ? Theme.S(4) : 0), w, sub.Length > 0 ? Theme.S(18) : h),
                Flashing ? Theme.Accent : Theme.Text, Theme.Sidebar, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            if (sub.Length > 0)
                TextRenderer.DrawText(g, sub, Theme.Small, new Rectangle(x + Theme.S(8), y + Theme.S(22), w, Theme.S(16)),
                    Theme.Muted, Theme.Sidebar, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
    }

    /// <summary>Crée, place et met à jour une vignette par fenêtre de la rotation.</summary>
    public sealed class OverlayManager
    {
        readonly App app;
        readonly Dictionary<IntPtr, OverlayWindow> map = new Dictionary<IntPtr, OverlayWindow>();
        readonly Timer timer = new Timer();
        int tick;

        public OverlayManager(App app)
        {
            this.app = app;
            timer.Interval = 100;
            timer.Tick += delegate { Step(); };
            timer.Start();
            app.StateChanged += delegate { Sync(); };
            app.ProfileChanged += delegate { Sync(); };
        }

        string NextTimerText()
        {
            TimerItem best = null;
            foreach (TimerItem t in app.S.Timers) if (t.EndUtc > 0 && (best == null || t.EndUtc < best.EndUtc)) best = t;
            if (best == null) return "";
            double left = (new DateTime(best.EndUtc, DateTimeKind.Utc) - DateTime.UtcNow).TotalSeconds;
            return best.Name + " · " + Fmt.Short(left);
        }

        public void Sync()
        {
            List<GameWindow> rot = app.S.OverlayEnabled ? app.Rotation() : new List<GameWindow>();
            HashSet<IntPtr> keep = new HashSet<IntPtr>();
            string timerText = NextTimerText();
            IntPtr active = app.LastActive;
            for (int i = 0; i < rot.Count; i++)
            {
                GameWindow w = rot[i];
                keep.Add(w.Handle);
                OverlayWindow o;
                if (!map.TryGetValue(w.Handle, out o))
                {
                    try { o = new OverlayWindow(app, w.Handle); map[w.Handle] = o; }
                    catch (Exception ex) { Program.Log("Vignette : " + ex.Message); continue; }
                }
                bool changed = o.Order != i + 1 || o.Name2 != w.Name || o.Active != (w.Handle == active)
                    || o.Flashing != app.IsFlashing(w.Handle) || o.TimerText != timerText;
                o.Order = i + 1; o.Name2 = w.Name; o.Class2 = w.Class;
                o.Active = w.Handle == active;
                o.Flashing = app.IsFlashing(w.Handle);
                o.TimerText = timerText;
                try { o.Track(); if (changed) o.Invalidate(); }
                catch (Exception ex) { Program.Log("Vignette " + w.Name + " : " + ex.Message); }
            }
            List<IntPtr> dead = new List<IntPtr>();
            foreach (IntPtr h in map.Keys) if (!keep.Contains(h)) dead.Add(h);
            foreach (IntPtr h in dead) { try { map[h].Close(); map[h].Dispose(); } catch { } map.Remove(h); }
        }

        void Step()
        {
            if (map.Count == 0) return;
            tick++;
            foreach (OverlayWindow o in map.Values) { try { o.Track(); o.Tick(); } catch { } }
            if (tick % 10 == 0) Sync(); // minuteur affiché : rafraîchi chaque seconde
        }

        public void CloseAll()
        {
            timer.Stop();
            foreach (OverlayWindow o in map.Values) { try { o.Close(); } catch { } }
            map.Clear();
        }
    }
}

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Relais
{
    /// <summary>
    /// Mini-barre flottante, toujours au-dessus, qui ne prend jamais le focus :
    /// un clic sur un perso = bascule sur sa fenêtre.
    /// </summary>
    public sealed class BarForm : Form
    {
        readonly App app;
        List<GameWindow> items = new List<GameWindow>();
        int hover = -1;
        bool moving;
        Point moveStart, formStart;
        readonly ToolTip tip = new ToolTip();

        public BarForm(App app)
        {
            this.app = app;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(1, 2, 3);
            TransparencyKey = Color.FromArgb(1, 2, 3);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            Text = "Relais — barre";
            app.StateChanged += delegate { UpdateVisibility(); if (Visible) Relayout(); };
            app.ProfileChanged += delegate { ApplyLook(); UpdateVisibility(); if (Visible) Relayout(); };
            ApplyLook();
            StartPulse();
            bool wasFight = false;
            app.Combat.Changed += delegate
            {
                bool now = app.Combat.Active;
                if (now != wasFight) { wasFight = now; if (Visible) Relayout(); }
                else if (Visible) Invalidate(CombatRect());
            };
        }

        bool Fight { get { return app.Combat != null && app.Combat.Active; } }

        /// <summary>Zone « combat » (tour + chrono) ajoutée au bout de la barre pendant un combat.</summary>
        Rectangle CombatRect()
        {
            int n = Math.Max(1, items.Count);
            if (Vertical) return new Rectangle(Pad, Grip + Pad + n * (TileH + Pad), TileW, Theme.S(40));
            return new Rectangle(Grip + Pad + n * (TileW + Pad), Pad, Theme.S(76), TileH);
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW | Native.WS_EX_TOPMOST;
                return cp;
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_MOUSEACTIVATE) { m.Result = (IntPtr)Native.MA_NOACTIVATE; return; }
            base.WndProc(ref m);
        }

        // ---------------- géométrie ----------------

        bool Vertical { get { return app.S.BarVertical; } }
        bool Names { get { return app.S.BarShowNames; } }
        int Grip { get { return Theme.S(26); } }
        int Pad { get { return Theme.S(6); } }
        int Av { get { return Theme.S(app.S.BarLarge ? 44 : 34); } }
        int TileH { get { return Av + Theme.S(14); } }
        int TileW { get { return Names ? Av + Theme.S(Vertical ? 112 : 92) : TileH; } }

        public void ApplyLook()
        {
            double o = Math.Max(30, Math.Min(100, app.S.BarOpacity)) / 100.0;
            if (Math.Abs(Opacity - o) > 0.001) Opacity = o;
        }

        bool placed;

        /// <summary>Gère « masquer hors jeu » : la barre n'apparaît que quand Dofus (ou Relais) est au premier plan.</summary>
        public void UpdateVisibility()
        {
            if (!app.S.ShowBar) { if (Visible) Hide(); return; }
            bool want = !app.S.BarAutoHide || app.GameOrOwnForeground();
            if (want && !Visible) { if (!placed) ShowBar(); else { Relayout(); Show(); } }
            else if (!want && Visible && !moving) Hide();
        }

        Rectangle TileRect(int i)
        {
            if (Vertical) return new Rectangle(Pad, Grip + Pad + i * (TileH + Pad), TileW, TileH);
            return new Rectangle(Grip + Pad + i * (TileW + Pad), Pad, TileW, TileH);
        }

        public void ShowBar()
        {
            Relayout();
            if (app.S.BarX < 0 || app.S.BarY < 0 || !OnScreen(new Point(app.S.BarX, app.S.BarY)))
            {
                Rectangle wa = Screen.PrimaryScreen.WorkingArea;
                app.S.BarX = wa.Left + (wa.Width - Width) / 2;
                app.S.BarY = wa.Top + Theme.S(8);
            }
            Location = new Point(app.S.BarX, app.S.BarY);
            placed = true;
            Show();
        }

        static bool OnScreen(Point p)
        {
            foreach (Screen s in Screen.AllScreens)
                if (s.WorkingArea.Contains(new Point(p.X + 10, p.Y + 10))) return true;
            return false;
        }

        public void Relayout()
        {
            items = app.Rotation();
            int n = Math.Max(1, items.Count);
            Size sz = Vertical
                ? new Size(TileW + 2 * Pad, Grip + Pad + n * (TileH + Pad) + (Fight ? Theme.S(40) + Pad : 0))
                : new Size(Grip + Pad + n * (TileW + Pad) + (Fight ? Theme.S(76) + Pad : 0), TileH + 2 * Pad);
            if (Size != sz) Size = sz;
            Invalidate();
        }

        // ---------------- dessin ----------------

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            Theme.Hq(g);
            RectangleF all = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            float rad = Vertical ? Theme.S(16) : Height / 2f;
            // fond bois + liseré doré
            using (System.Drawing.Drawing2D.GraphicsPath bgp = Theme.Round(all, rad))
            using (System.Drawing.Drawing2D.LinearGradientBrush lb = new System.Drawing.Drawing2D.LinearGradientBrush(
                new RectangleF(0, 0, Width, Height), Theme.Panel2, Theme.Sidebar, Vertical ? 0f : 90f))
                g.FillPath(lb, bgp);
            Theme.StrokeRound(g, app.Paused ? Theme.Accent : Color.FromArgb(150, Theme.Accent), 1.2f, all, rad);

            // poignée = petit blason
            float gs = Theme.S(16);
            RectangleF gr = Vertical
                ? new RectangleF((Width - gs) / 2f, Theme.S(6), gs, gs)
                : new RectangleF(Theme.S(8), (Height - gs) / 2f, gs, gs);
            Brand.DrawShield(g, gr);

            if (items.Count == 0)
            {
                Rectangle r = TileRect(0);
                Theme.DrawText(g, L.T("Aucun perso"), Theme.Small, Theme.Faint, r, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            }

            IntPtr fg = app.ForegroundHandle;
            for (int i = 0; i < items.Count; i++)
            {
                GameWindow w = items[i];
                Rectangle r = TileRect(i);
                bool active = w.Handle == fg;
                bool flash = app.IsFlashing(w.Handle);
                int av = Av;
                RectangleF ar = Names
                    ? new RectangleF(r.X + Theme.S(7), r.Y + (r.Height - av) / 2f, av, av)
                    : new RectangleF(r.X + (r.Width - av) / 2f, r.Y + (r.Height - av) / 2f, av, av);

                if (i == hover)
                    Theme.FillRound(g, Color.FromArgb(Theme.IsLight ? 110 : 90, Theme.Hover), r, r.Height / 2f);

                // tour en attente : anneau qui « respire »
                if (flash && !active)
                {
                    float k = (float)(0.5 + 0.5 * Math.Sin(pulse * 0.55));
                    float grow = Theme.S(3) + k * Theme.S(4);
                    using (Pen pp = new Pen(Color.FromArgb((int)(220 - 150 * k), Theme.Accent), Theme.S(2)))
                        g.DrawEllipse(pp, ar.X - grow, ar.Y - grow, ar.Width + 2 * grow, ar.Height + 2 * grow);
                }

                // anneau : doré si actif
                float ring = Theme.S(active ? 3 : 2);
                RectangleF rr = new RectangleF(ar.X - ring / 2f - 1, ar.Y - ring / 2f - 1, ar.Width + ring + 2, ar.Height + ring + 2);
                if (active)
                    using (System.Drawing.Drawing2D.LinearGradientBrush gb = new System.Drawing.Drawing2D.LinearGradientBrush(rr, ControlPaint.Light(Theme.Accent, 0.5f), ControlPaint.Dark(Theme.Accent, 0.2f), 60f))
                    using (Pen rp = new Pen(gb, ring)) g.DrawEllipse(rp, rr);
                else
                    using (Pen rp = new Pen(Color.FromArgb(180, Theme.Border), ring)) g.DrawEllipse(rp, rr);

                Theme.Avatar(g, ar, w.Name, w.Class, app.Paused);

                // pièce d'or : numéro d'ordre
                float cs = Theme.S(15);
                RectangleF coin = new RectangleF(ar.Right - cs + Theme.S(3), ar.Bottom - cs + Theme.S(3), cs, cs);
                using (System.Drawing.Drawing2D.LinearGradientBrush cb = new System.Drawing.Drawing2D.LinearGradientBrush(coin, ControlPaint.Light(Theme.Accent, 0.45f), ControlPaint.Dark(Theme.Accent, 0.15f), 90f))
                    g.FillEllipse(cb, coin);
                using (Pen cp2 = new Pen(Theme.Sidebar, 1.2f)) g.DrawEllipse(cp2, coin);
                Theme.DrawText(g, (i + 1).ToString(), Theme.F(6.5f, FontStyle.Bold), Theme.OnAccent, Rectangle.Round(coin),
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

                if (Names)
                {
                    Rectangle tr = new Rectangle((int)ar.Right + Theme.S(9), r.Y, r.Right - (int)ar.Right - Theme.S(12), r.Height);
                    Rectangle t1 = new Rectangle(tr.X, tr.Y + tr.Height / 2 - Theme.S(16), tr.Width, Theme.S(18));
                    Rectangle t2 = new Rectangle(tr.X, tr.Y + tr.Height / 2 + Theme.S(1), tr.Width, Theme.S(15));
                    Theme.DrawText(g, w.Name, active ? Theme.SmallBold : Theme.Small, active ? Theme.Text : Theme.Muted, t1,
                        TextFormatFlags.Left | TextFormatFlags.Bottom | TextFormatFlags.EndEllipsis);
                    bool plays = Fight && string.Equals(app.Combat.Current, w.Name, StringComparison.OrdinalIgnoreCase);
                    string sub = flash && !active ? L.T("à toi !") : plays ? L.T("joue") : (w.Class.Length > 0 ? w.Class : "");
                    Theme.DrawText(g, sub, Theme.F(7f, FontStyle.Regular), (flash && !active) || plays ? Theme.Accent : Theme.Faint, t2,
                        TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.EndEllipsis);
                }
            }

            if (Fight)
            {
                // repère du perso qui joue (la barre suit l'ordre d'initiative)
                for (int i = 0; i < items.Count; i++)
                    if (string.Equals(app.Combat.Current, items[i].Name, StringComparison.OrdinalIgnoreCase))
                    {
                        Rectangle r = TileRect(i);
                        float cx = Vertical ? r.X + Theme.S(3) : r.X + r.Width / 2f, cy = Vertical ? r.Y + r.Height / 2f : r.Bottom + Pad / 2f;
                        Brand.Diamond(g, Theme.Accent, cx, cy, Theme.S(3));
                    }
                Rectangle cr = CombatRect();
                Theme.FillRound(g, Color.FromArgb(Theme.IsLight ? 60 : 50, Theme.Accent), cr, Theme.S(12));
                Theme.StrokeRound(g, Color.FromArgb(140, Theme.Accent), 1f, cr, Theme.S(12));
                TimeSpan el = app.Combat.Elapsed;
                Rectangle a1 = new Rectangle(cr.X, cr.Y + cr.Height / 2 - Theme.S(17), cr.Width, Theme.S(18));
                Rectangle a2 = new Rectangle(cr.X, cr.Y + cr.Height / 2 + Theme.S(1), cr.Width, Theme.S(15));
                Theme.DrawText(g, L.T("Tour ") + app.Combat.Round, Theme.SmallBold, Theme.Accent, a1, TextFormatFlags.HorizontalCenter | TextFormatFlags.Bottom);
                Theme.DrawText(g, ((int)el.TotalMinutes) + ":" + el.Seconds.ToString("00"), Theme.F(7.5f, FontStyle.Regular), Theme.Muted, a2,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.Top);
            }

            if (app.Paused)
            {
                using (SolidBrush b2 = new SolidBrush(Color.FromArgb(170, Theme.Sidebar))) g.FillRectangle(b2, TileRect(0).X, 0, Width, Height);
                Theme.DrawText(g, L.T("EN PAUSE"), Theme.Serif(9f, FontStyle.Bold), Theme.Accent, ClientRectangle, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }

        // ---------------- souris ----------------

        int TileAt(Point p)
        {
            for (int i = 0; i < items.Count; i++) if (TileRect(i).Contains(p)) return i;
            return -1;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left && TileAt(e.Location) < 0)
            {
                moving = true;
                moveStart = Cursor.Position;
                formStart = Location;
                Capture = true;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (moving)
            {
                Point c = Cursor.Position;
                Location = new Point(formStart.X + c.X - moveStart.X, formStart.Y + c.Y - moveStart.Y);
                return;
            }
            int h = TileAt(e.Location);
            Cursor = h >= 0 ? Cursors.Hand : Cursors.SizeAll;
            if (h != hover)
            {
                hover = h;
                Invalidate();
                if (h >= 0)
                {
                    GameWindow w = items[h];
                    Member m = app.S.GetCurrent().Find(w.Name);
                    Hotkey hk = m == null ? null : Hotkey.Parse(m.Hotkey);
                    string t = w.Name + (w.Class.Length > 0 ? " (" + w.Class + ")" : "") + (hk != null ? L.T("\nRaccourci : ") + hk.Display() : "");
                    string note = app.NoteOf(w.Name);
                    if (note != null) t += "\n\n" + (note.Length > 300 ? note.Substring(0, 300) + "…" : note);
                    tip.SetToolTip(this, t);
                    if (app.S.BarHoverPreview)
                    {
                        Rectangle tr = TileRect(h);
                        Rectangle screen = new Rectangle(PointToScreen(tr.Location), tr.Size);
                        app.Hover.ShowFor(w.Handle, screen, Top < Screen.FromControl(this).WorkingArea.Top + Screen.FromControl(this).WorkingArea.Height / 2);
                    }
                }
                else
                {
                    tip.SetToolTip(this, L.T("Glisser pour déplacer · clic droit : options"));
                    app.Hover.HidePreview();
                }
            }
        }

        protected override void OnMouseLeave(EventArgs e) { hover = -1; app.Hover.HidePreview(); Invalidate(); base.OnMouseLeave(e); }

        readonly Timer pulseTimer = new Timer();
        int pulse;

        void StartPulse()
        {
            pulseTimer.Interval = 120;
            pulseTimer.Tick += delegate { if (app.FlashingCount > 0 && Visible) { pulse++; Invalidate(); } };
            pulseTimer.Start();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (moving)
            {
                moving = false;
                Capture = false;
                app.S.BarX = Location.X;
                app.S.BarY = Location.Y;
                app.S.Save();
                return;
            }
            if (e.Button == MouseButtons.Right)
            {
                int ti = TileAt(e.Location);
                if (ti >= 0)
                {
                    Rectangle tr = TileRect(ti);
                    app.Hover.HidePreview();
                    RadialMenu.Open(app, items[ti], PointToScreen(new Point(tr.X + Theme.S(7) + Av / 2, tr.Y + tr.Height / 2)));
                }
                else if (Fight && CombatRect().Contains(e.Location)) ShowCombatMenu(e.Location);
                else ShowMenu(e.Location);
                return;
            }
            if (e.Button == MouseButtons.Left)
            {
                int i = TileAt(e.Location);
                if (i >= 0) app.Activate(items[i].Name);
            }
        }

        void ShowCombatMenu(Point p)
        {
            ContextMenuStrip cm = new ContextMenuStrip();
            cm.Items.Add(L.T("Terminer ce combat"), null, delegate { app.Combat.EndNow(); });
            cm.Show(this, p);
        }

        void ShowMenu(Point p)
        {
            ContextMenuStrip cm = new ContextMenuStrip();
            cm.Items.Add(L.T("Ouvrir Relais"), null, delegate { app.ShowMain(); });
            cm.Items.Add(app.Paused ? L.T("Reprendre") : L.T("Mettre en pause"), null, delegate { app.TogglePause(); });
            cm.Items.Add(Vertical ? L.T("Barre horizontale") : L.T("Barre verticale"), null, delegate
            {
                app.S.BarVertical = !app.S.BarVertical; app.S.Save(); Relayout(); app.ProfileEdited();
            });
            cm.Items.Add(Names ? L.T("Masquer les noms") : L.T("Afficher les noms"), null, delegate
            {
                app.S.BarShowNames = !app.S.BarShowNames; app.S.Save(); Relayout(); app.ProfileEdited();
            });
            cm.Items.Add(new ToolStripSeparator());
            cm.Items.Add(L.T("Masquer la barre"), null, delegate { app.ToggleBar(); });
            cm.Show(this, p);
        }
    }

    /// <summary>Petite notification flottante (changement de profil, déconnexion…), sans prendre le focus.</summary>
    public sealed class Toast : Form
    {
        readonly Timer timer = new Timer();
        string text = "";
        bool warn;
        int ticks;

        public Toast()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(1, 2, 3);
            TransparencyKey = Color.FromArgb(1, 2, 3);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            timer.Interval = 50;
            timer.Tick += delegate
            {
                ticks--;
                if (ticks <= 0) { timer.Stop(); Hide(); return; }
                if (ticks < 8) Opacity = ticks / 8.0;
            };
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW | Native.WS_EX_TOPMOST | 0x20 /*TRANSPARENT*/;
                return cp;
            }
        }

        public void ShowMessage(string msg, bool warning)
        {
            ShowMessage(msg, warning, warning ? 4 : 1.6);
        }

        public void ShowMessage(string msg, bool warning, double seconds)
        {
            text = msg;
            warn = warning;
            Size ts = TextRenderer.MeasureText(msg, Theme.Bold);
            Size = new Size(ts.Width + Theme.S(48), Theme.S(40));
            Rectangle wa = Screen.FromPoint(Cursor.Position).WorkingArea;
            Location = new Point(wa.Left + (wa.Width - Width) / 2, wa.Top + Theme.S(78));
            ticks = (int)(seconds * 20);
            Opacity = 1;
            Invalidate();
            if (!Visible) Show();
            timer.Start();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            Theme.Hq(g);
            RectangleF r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            Theme.FillRound(g, Theme.Panel, r, Height / 2f);
            Theme.StrokeRound(g, warn ? Theme.Red : Theme.Accent, 1.4f, r, Height / 2f);
            using (SolidBrush b = new SolidBrush(warn ? Theme.Red : Theme.Accent))
                g.FillEllipse(b, Theme.S(16), Height / 2f - Theme.S(4), Theme.S(8), Theme.S(8));
            Theme.DrawText(g, text, Theme.Bold, Theme.Text, new Rectangle(Theme.S(32), 0, Width - Theme.S(40), Height),
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    /// <summary>Menu radial (clic droit sur un perso de la mini-barre) : aller, fiche, son, invitation.</summary>
    public sealed class RadialMenu : Form
    {
        static RadialMenu current;
        readonly App app;
        readonly GameWindow win;
        readonly string[] labels;
        int hover = -1;
        bool entered;
        readonly Timer watch = new Timer();
        float grow;

        public static void Open(App app, GameWindow w, Point center)
        {
            if (current != null && !current.IsDisposed) current.Close();
            current = new RadialMenu(app, w, center);
            current.Show();
        }

        RadialMenu(App app, GameWindow w, Point center)
        {
            this.app = app;
            win = w;
            bool muted = app.VolumeOf(w.Name) == 0;
            labels = new string[] { L.T("Aller"), L.T("Fiche"), muted ? L.T("Son") : L.T("Muet"), L.T("Inviter") };
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(1, 2, 3);
            TransparencyKey = BackColor;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            int size = Theme.S(190);
            Bounds = new Rectangle(center.X - size / 2, center.Y - size / 2, size, size);
            watch.Interval = 30;
            int life = 0;
            watch.Tick += delegate
            {
                life++;
                if (grow < 1f) { grow = Math.Min(1f, grow + 0.2f); Invalidate(); }
                bool inside = Bounds.Contains(Cursor.Position);
                if (inside) entered = true;
                if ((entered && !inside) || (!entered && life > 120)) Close();
            };
            watch.Start();
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW | Native.WS_EX_TOPMOST;
                return cp;
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_MOUSEACTIVATE) { m.Result = (IntPtr)Native.MA_NOACTIVATE; return; }
            base.WndProc(ref m);
        }

        RectangleF Item(int i)
        {
            float c = Width / 2f, d = Theme.S(54), r = Theme.S(62) * (0.55f + 0.45f * grow);
            double a = -Math.PI / 2 + i * Math.PI / 2;
            return new RectangleF(c + (float)Math.Cos(a) * r - d / 2, c + (float)Math.Sin(a) * r - d / 2, d, d);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            Theme.Hq(g);
            float c = Width / 2f, av = Theme.S(46);
            // disque central
            using (SolidBrush bb = new SolidBrush(Theme.Sidebar)) g.FillEllipse(bb, c - av / 2 - 4, c - av / 2 - 4, av + 8, av + 8);
            using (Pen gp = new Pen(Theme.Accent, 2f)) g.DrawEllipse(gp, c - av / 2 - 4, c - av / 2 - 4, av + 8, av + 8);
            Theme.Avatar(g, new RectangleF(c - av / 2, c - av / 2, av, av), win.Name, win.Class, false);
            for (int i = 0; i < labels.Length; i++)
            {
                RectangleF r = Item(i);
                bool h = i == hover;
                using (System.Drawing.Drawing2D.LinearGradientBrush lb = new System.Drawing.Drawing2D.LinearGradientBrush(r,
                    h ? ControlPaint.Light(Theme.Accent, 0.3f) : Theme.Panel2, h ? Theme.Accent : Theme.Sidebar, 90f))
                    g.FillEllipse(lb, r);
                using (Pen p = new Pen(h ? ControlPaint.Light(Theme.Accent, 0.4f) : Color.FromArgb(170, Theme.Accent), 1.5f)) g.DrawEllipse(p, r);
                Theme.DrawText(g, labels[i], Theme.SmallBold, h ? Theme.OnAccent : Theme.Text, Rectangle.Round(r),
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }

        int ItemAt(Point p)
        {
            for (int i = 0; i < labels.Length; i++)
            {
                RectangleF r = Item(i);
                float dx = p.X - (r.X + r.Width / 2), dy = p.Y - (r.Y + r.Height / 2);
                if (dx * dx + dy * dy <= r.Width * r.Width / 4) return i;
            }
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int h = ItemAt(e.Location);
            if (h != hover) { hover = h; Cursor = h >= 0 ? Cursors.Hand : Cursors.Default; Invalidate(); }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            int i = ItemAt(e.Location);
            if (i < 0) { Close(); return; }
            string n = win.Name;
            Close();
            if (i == 0) app.Activate(n);
            else if (i == 1) { app.ShowMain(); app.OpenSheet(n); }
            else if (i == 2) app.SetVolume(n, app.VolumeOf(n) == 0 ? 100 : 0);
            else app.CopyInvite(n);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            watch.Stop();
            if (current == this) current = null;
            base.OnFormClosed(e);
        }
    }
}

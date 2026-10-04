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
        int Grip { get { return Theme.S(14); } }
        int Pad { get { return Theme.S(6); } }
        int TileW { get { return Names ? (Vertical ? Theme.S(150) : Theme.S(118)) + (app.S.BarLarge ? Theme.S(12) : 0) : TileH; } }
        int TileH { get { return Theme.S(app.S.BarLarge ? 56 : 44); } }
        int Av { get { return Theme.S(app.S.BarLarge ? 40 : 30); } }

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
                ? new Size(TileW + 2 * Pad, Grip + Pad + n * (TileH + Pad))
                : new Size(Grip + Pad + n * (TileW + Pad), TileH + 2 * Pad);
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
            Theme.FillRound(g, Theme.Bg, all, Theme.S(10));
            Theme.StrokeRound(g, app.Paused ? Theme.Accent : Theme.Border, 1f, all, Theme.S(10));

            // poignée de déplacement
            using (SolidBrush b = new SolidBrush(Theme.Faint))
            {
                for (int k = 0; k < 3; k++) for (int j = 0; j < 2; j++)
                {
                    float x, y;
                    if (Vertical) { x = Width / 2f - Theme.S(8) + k * Theme.S(6); y = Theme.S(5) + j * Theme.S(4); }
                    else { x = Theme.S(4) + j * Theme.S(4); y = Height / 2f - Theme.S(8) + k * Theme.S(6); }
                    g.FillEllipse(b, x, y, Theme.S(2), Theme.S(2));
                }
            }

            if (items.Count == 0)
            {
                Rectangle r = TileRect(0);
                Theme.DrawText(g, "Aucun perso", Theme.Small, Theme.Faint, r, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            }

            IntPtr fg = app.ForegroundHandle;
            for (int i = 0; i < items.Count; i++)
            {
                GameWindow w = items[i];
                Rectangle r = TileRect(i);
                bool active = w.Handle == fg;
                bool flash = app.IsFlashing(w.Handle);
                Color bg = active ? Theme.AccentDim : (i == hover ? Theme.Hover : Theme.Panel);
                Theme.FillRound(g, bg, r, Theme.S(8));
                if (active) Theme.StrokeRound(g, Theme.Accent, 1.5f, new RectangleF(r.X + 0.75f, r.Y + 0.75f, r.Width - 1.5f, r.Height - 1.5f), Theme.S(8));
                if (flash && !active)
                {
                    // c'est son tour : la vignette pulse
                    int pa = 90 + (int)(120 * (0.5 + 0.5 * Math.Sin(pulse * 0.6)));
                    using (System.Drawing.Drawing2D.GraphicsPath gp = Theme.Round(r, Theme.S(8)))
                    using (SolidBrush pb = new SolidBrush(Color.FromArgb(Math.Min(255, pa / 2), Theme.Accent))) g.FillPath(pb, gp);
                    Theme.StrokeRound(g, Color.FromArgb(Math.Min(255, pa + 40), Theme.Accent), 2f, new RectangleF(r.X + 1, r.Y + 1, r.Width - 2, r.Height - 2), Theme.S(8));
                }

                int av = Av;
                RectangleF ar = Names
                    ? new RectangleF(r.X + Theme.S(7), r.Y + (r.Height - av) / 2f, av, av)
                    : new RectangleF(r.X + (r.Width - av) / 2f, r.Y + (r.Height - av) / 2f, av, av);
                Theme.Avatar(g, ar, w.Name, w.Class, app.Paused);

                // numéro d'ordre
                Rectangle nr = new Rectangle((int)ar.Right - Theme.S(10), (int)ar.Bottom - Theme.S(12), Theme.S(14), Theme.S(14));
                Theme.FillRound(g, Theme.Bg, nr, Theme.S(7));
                Theme.DrawText(g, (i + 1).ToString(), Theme.F(6.5f, FontStyle.Bold), active ? Theme.Accent : Theme.Muted, nr,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

                if (Names)
                {
                    Rectangle tr = new Rectangle((int)ar.Right + Theme.S(7), r.Y, r.Right - (int)ar.Right - Theme.S(10), r.Height);
                    Theme.DrawText(g, w.Name, active ? Theme.SmallBold : Theme.Small, active ? Theme.Text : Theme.Muted, tr,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                }
            }

            if (app.Paused)
            {
                using (SolidBrush b = new SolidBrush(Color.FromArgb(160, Theme.Bg))) g.FillRectangle(b, TileRect(0).X, 0, Width, Height);
                Theme.DrawText(g, "PAUSE", Theme.SmallBold, Theme.Accent, ClientRectangle, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
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
                    string t = w.Name + (w.Class.Length > 0 ? " (" + w.Class + ")" : "") + (hk != null ? "\nRaccourci : " + hk.Display() : "");
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
                    tip.SetToolTip(this, "Glisser pour déplacer · clic droit : options");
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
            if (e.Button == MouseButtons.Right) { ShowMenu(e.Location); return; }
            if (e.Button == MouseButtons.Left)
            {
                int i = TileAt(e.Location);
                if (i >= 0) app.Activate(items[i].Name);
            }
        }

        void ShowMenu(Point p)
        {
            ContextMenuStrip cm = new ContextMenuStrip();
            cm.Items.Add("Ouvrir Relais", null, delegate { app.ShowMain(); });
            cm.Items.Add(app.Paused ? "Reprendre" : "Mettre en pause", null, delegate { app.TogglePause(); });
            cm.Items.Add(Vertical ? "Barre horizontale" : "Barre verticale", null, delegate
            {
                app.S.BarVertical = !app.S.BarVertical; app.S.Save(); Relayout(); app.ProfileEdited();
            });
            cm.Items.Add(Names ? "Masquer les noms" : "Afficher les noms", null, delegate
            {
                app.S.BarShowNames = !app.S.BarShowNames; app.S.Save(); Relayout(); app.ProfileEdited();
            });
            cm.Items.Add(new ToolStripSeparator());
            cm.Items.Add("Masquer la barre", null, delegate { app.ToggleBar(); });
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
}

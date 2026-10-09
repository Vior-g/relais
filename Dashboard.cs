using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Relais
{
    /// <summary>
    /// Accueil : la team en grandes cartes, actions rapides, minuteurs en cours, bilan du jour et journal.
    /// Tout est dessiné à la main ; les zones cliquables sont recalculées à chaque dessin.
    /// </summary>
    public sealed class HomeView : Control
    {
        readonly App app;
        sealed class Hit { public Rectangle R; public Action Click; public Action RightClick; public string Tip; }
        readonly List<Hit> hits = new List<Hit>();
        int hover = -1;
        readonly ToolTip tip = new ToolTip();
        readonly Timer refresh = new Timer();
        int pulse;

        public HomeView(App app)
        {
            this.app = app;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Bg;
            app.StateChanged += delegate { if (Visible) Invalidate(); };
            app.ProfileChanged += delegate { if (Visible) Invalidate(); };
            app.EventsChanged += delegate { if (Visible) Invalidate(); };
            app.TimersChanged += delegate { if (Visible) Invalidate(); };
            Almanax.Loaded += delegate { if (!IsDisposed) Invalidate(); };
            refresh.Interval = 1000;
            refresh.Tick += delegate { pulse++; if (Visible) Invalidate(); };
            refresh.Start();
        }

        // ---------------- outils de dessin ----------------

        void Card(Graphics g, Rectangle r, bool highlight)
        {
            using (GraphicsPath p = Theme.Round(r, Theme.S(12)))
            {
                using (LinearGradientBrush lb = new LinearGradientBrush(r, Theme.Panel2, Theme.Panel, 90f)) g.FillPath(lb, p);
                using (Pen pen = new Pen(highlight ? Theme.Accent : Color.FromArgb(120, Theme.Border), highlight ? 1.6f : 1f)) g.DrawPath(pen, p);
            }
        }

        void Heading(Graphics g, string text, int x, int y, int w)
        {
            string t = text.ToUpperInvariant();
            Size ts = TextRenderer.MeasureText(t, Theme.Section);
            Brand.Diamond(g, Theme.Accent, x + Theme.S(4), y + ts.Height / 2f, Theme.S(3));
            TextRenderer.DrawText(g, t, Theme.Section, new Point(x + Theme.S(12), y), Theme.AccentSoft, TextFormatFlags.NoPrefix);
            float x0 = x + Theme.S(12) + ts.Width + Theme.S(4);
            if (w - (x0 - x) > 20)
                using (LinearGradientBrush lb = new LinearGradientBrush(new RectangleF(x0, y, x + w - x0, 2), Color.FromArgb(150, Theme.Border), Color.FromArgb(0, Theme.Border), 0f))
                    g.FillRectangle(lb, x0, y + ts.Height / 2f, x + w - x0, 1f);
        }

        void AddHit(Rectangle r, Action click, Action right, string t)
        {
            Hit h = new Hit(); h.R = r; h.Click = click; h.RightClick = right; h.Tip = t;
            hits.Add(h);
        }

        bool Hovered(Rectangle r)
        {
            return hover >= 0 && hover < hits.Count && hits[hover].R == r;
        }

        static string Greeting()
        {
            int h = DateTime.Now.Hour;
            if (h < 5) return L.T("Bonne nuit, aventurier");
            if (h < 12) return L.T("Bonjour, aventurier");
            if (h < 18) return L.T("Bon après-midi, aventurier");
            return L.T("Bonsoir, aventurier");
        }

        // ---------------- dessin ----------------

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Hq(g);
            g.Clear(Theme.Bg);
            hits.Clear();
            int W = Width, x0 = 0, y = Theme.S(6), gap = Theme.S(12);
            List<GameWindow> rot = app.Rotation();
            Profile prof = app.S.GetCurrent();

            // --- en-tête
            TextRenderer.DrawText(g, Greeting(), Theme.Serif(16f, FontStyle.Bold), new Point(x0, y), Theme.Text, TextFormatFlags.NoPrefix);
            y += Theme.S(32);
            string sub = rot.Count == 0
                ? L.T("Aucun perso connecté — lance Dofus, ta team apparaîtra ici.")
                : rot.Count + L.T(" perso") + (rot.Count > 1 ? "s" : "") + L.T(" prêt") + (rot.Count > 1 && !L.En ? "s" : "") + L.T(" · profil « ") + prof.Name + " »" + (app.Paused ? L.T(" · EN PAUSE") : "");
            TextRenderer.DrawText(g, sub, Theme.Normal, new Point(x0 + 1, y), app.Paused ? Theme.Accent : Theme.Muted, TextFormatFlags.NoPrefix);
            y += Theme.S(30);

            // --- mise à jour disponible
            if (app.AvailableUpdate != null)
            {
                Rectangle ub = new Rectangle(x0, y, W, Theme.S(44));
                Card(g, ub, true);
                Theme.DrawText(g, L.T("Relais ") + app.AvailableUpdate.Tag + L.T(" est disponible — clique pour mettre à jour"), Theme.Bold, Theme.Accent,
                    new Rectangle(ub.X + Theme.S(16), ub.Y, ub.Width - Theme.S(32), ub.Height), TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
                AddHit(ub, delegate { Updater.Offer(app, app.AvailableUpdate); }, null, null);
                y += ub.Height + gap;
            }

            // --- combat en cours
            if (app.Combat.Active)
            {
                Rectangle cb = new Rectangle(x0, y, W, Theme.S(48));
                Card(g, cb, true);
                float k = (float)(0.5 + 0.5 * Math.Sin(pulse * 1.3));
                using (GraphicsPath p = Theme.Round(cb, Theme.S(12)))
                using (SolidBrush b = new SolidBrush(Color.FromArgb((int)(15 + 25 * k), Theme.Accent))) g.FillPath(b, p);
                TimeSpan el = app.Combat.Elapsed;
                string who = app.Combat.Current != null ? " · " + app.Combat.Current + L.T(" joue") : "";
                Theme.DrawText(g, L.T("Combat en cours — tour ") + app.Combat.Round + " · " + ((int)el.TotalMinutes) + ":" + el.Seconds.ToString("00") + who, Theme.Bold, Theme.Accent,
                    new Rectangle(cb.X + Theme.S(16), cb.Y, cb.Width - Theme.S(150), cb.Height), TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                Rectangle endB = new Rectangle(cb.Right - Theme.S(130), cb.Y + Theme.S(9), Theme.S(116), Theme.S(30));
                Theme.FillRound(g, Hovered(endB) ? Theme.Hover : Theme.Panel2, endB, Theme.S(8));
                Theme.DrawText(g, L.T("Terminer"), Theme.Normal, Theme.Text, endB, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                AddHit(endB, delegate { app.Combat.EndNow(); }, null, L.T("Le combat se termine tout seul après un moment sans tour"));
                y += cb.Height + gap;
            }

            // --- cartes de la team
            Heading(g, L.T("Ta team"), x0, y, W);
            y += Theme.S(28);
            if (rot.Count == 0)
            {
                Rectangle er = new Rectangle(x0, y, W, Theme.S(90));
                Card(g, er, false);
                Theme.DrawText(g, L.T("Connecte tes comptes Dofus : chaque perso apparaîtra ici avec son avatar, sa classe et son état."),
                    Theme.Normal, Theme.Muted, new Rectangle(er.X + Theme.S(20), er.Y, er.Width - Theme.S(40), er.Height), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
                y += er.Height + gap;
            }
            else
            {
                int cols = W > Theme.S(760) ? 4 : (W > Theme.S(420) ? 2 : 1);
                if (rot.Count < cols) cols = Math.Max(1, rot.Count);
                int cw = (W - (cols - 1) * gap) / cols, ch = Theme.S(96);
                IntPtr active = app.LastActive;
                for (int i = 0; i < rot.Count; i++)
                {
                    GameWindow w = rot[i];
                    Rectangle r = new Rectangle(x0 + (i % cols) * (cw + gap), y + (i / cols) * (ch + gap), cw, ch);
                    bool isActive = w.Handle == active;
                    bool flash = app.IsFlashing(w.Handle);
                    Card(g, r, isActive || flash || Hovered(r));
                    if (flash)
                    {
                        float k = (float)(0.5 + 0.5 * Math.Sin(pulse * 1.3));
                        using (GraphicsPath p = Theme.Round(r, Theme.S(12)))
                        using (SolidBrush b = new SolidBrush(Color.FromArgb((int)(25 + 35 * k), Theme.Accent))) g.FillPath(b, p);
                    }
                    int av = Theme.S(56);
                    RectangleF ar = new RectangleF(r.X + Theme.S(16), r.Y + (r.Height - av) / 2f, av, av);
                    using (Pen rp = new Pen(isActive ? Theme.Accent : Color.FromArgb(170, Theme.Border), Theme.S(isActive ? 3 : 2)))
                        g.DrawEllipse(rp, ar.X - 3, ar.Y - 3, ar.Width + 6, ar.Height + 6);
                    Theme.Avatar(g, ar, w.Name, w.Class, false);
                    float cs = Theme.S(18);
                    RectangleF coin = new RectangleF(ar.Right - cs + Theme.S(4), ar.Bottom - cs + Theme.S(4), cs, cs);
                    using (LinearGradientBrush cb = new LinearGradientBrush(coin, ControlPaint.Light(Theme.Accent, 0.45f), ControlPaint.Dark(Theme.Accent, 0.15f), 90f)) g.FillEllipse(cb, coin);
                    Theme.DrawText(g, (i + 1).ToString(), Theme.F(7.5f, FontStyle.Bold), Theme.OnAccent, Rectangle.Round(coin), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

                    int tx = (int)ar.Right + Theme.S(18), tw = r.Right - tx - Theme.S(10);
                    TextRenderer.DrawText(g, w.Name, Theme.Serif(12f, FontStyle.Bold), new Rectangle(tx, r.Y + Theme.S(14), tw, Theme.S(24)), Theme.Text, TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                    CharSheet sh;
                    string line2 = (w.Class.Length > 0 ? w.Class : L.T("Personnage")) + (app.S.Sheets.TryGetValue(w.Name, out sh) && sh != null && sh.Level > 0 ? L.T(" · niv. ") + sh.Level : "");
                    Theme.DrawText(g, line2, Theme.Small, Theme.Muted, new Rectangle(tx, r.Y + Theme.S(40), tw, Theme.S(18)), TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
                    string state = flash ? L.T("À toi de jouer !") : isActive ? L.T("Au premier plan") : L.T("Connecté");
                    if (app.VolumeOf(w.Name) == 0) state += L.T(" · muet");
                    using (SolidBrush dot = new SolidBrush(flash ? Theme.Accent : Theme.Green))
                        g.FillEllipse(dot, tx, r.Y + Theme.S(66), Theme.S(7), Theme.S(7));
                    Theme.DrawText(g, state, flash ? Theme.SmallBold : Theme.Small, flash ? Theme.Accent : Theme.Faint,
                        new Rectangle(tx + Theme.S(12), r.Y + Theme.S(60), tw - Theme.S(12), Theme.S(18)), TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
                    string name = w.Name;
                    AddHit(r, delegate { app.Activate(name); }, delegate { Point p = PointToScreen(new Point(r.X + Theme.S(44), r.Y + r.Height / 2)); RadialMenu.Open(app, w, p); },
                        L.T("Clic : aller sur ") + name + L.T(" · clic droit : menu (fiche, son, invitation)"));
                }
                int rows = (rot.Count + cols - 1) / cols;
                y += rows * (ch + gap);
            }

            // --- actions rapides
            Heading(g, L.T("Actions rapides"), x0, y, W);
            y += Theme.S(28);
            string[] labels = { L.T("Superposer"), L.T("Mosaïque"), L.T("Aperçus"), L.T("Inviter"), L.T("Palette  Ctrl+K") };
            Action[] acts = {
                delegate { app.StackWindows(); }, delegate { app.MosaicWindows(); }, delegate { app.TogglePreviewWall(); },
                delegate { app.InviteNext(); }, delegate { app.OpenPalette(); } };
            int n = labels.Length, bw = (W - (n - 1) * Theme.S(8)) / n, bh = Theme.S(38);
            for (int i = 0; i < n; i++)
            {
                Rectangle b = new Rectangle(x0 + i * (bw + Theme.S(8)), y, bw, bh);
                bool hv = Hovered(b);
                bool primary = i == n - 1;
                using (GraphicsPath p = Theme.Round(b, Theme.S(8)))
                {
                    Color c1 = primary ? ControlPaint.Light(Theme.Accent, hv ? 0.5f : 0.3f) : (hv ? Theme.Hover : Theme.Panel2);
                    Color c2 = primary ? ControlPaint.Dark(Theme.Accent, 0.08f) : Theme.Panel;
                    using (LinearGradientBrush lb = new LinearGradientBrush(b, c1, c2, 90f)) g.FillPath(lb, p);
                    using (Pen pen = new Pen(Color.FromArgb(hv ? 220 : 110, primary ? ControlPaint.Dark(Theme.Accent, 0.3f) : Theme.Border))) g.DrawPath(pen, p);
                }
                Theme.DrawText(g, labels[i], Theme.Bold, primary ? Theme.OnAccent : Theme.Text, b, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                AddHit(b, acts[i], null, null);
            }
            y += bh + gap + Theme.S(4);

            // --- almanax du jour
            if (app.S.AlmanaxShow)
            {
                Heading(g, L.T("Almanax du jour"), x0, y, W);
                y += Theme.S(28);
                Almanax.Day al = Almanax.Get(app.Ui, app.S.Language);
                int chipsH = rot.Count > 0 ? Theme.S(40) : 0;
                Rectangle ac = new Rectangle(x0, y, W, Theme.S(84) + chipsH);
                Card(g, ac, false);
                if (al == null)
                {
                    Theme.DrawText(g, L.T("Chargement de l'Almanax… (clic pour réessayer)"), Theme.Small, Theme.Faint,
                        new Rectangle(ac.X + Theme.S(16), ac.Y, ac.Width - Theme.S(32), Theme.S(84)), TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
                    AddHit(new Rectangle(ac.X, ac.Y, ac.Width, Theme.S(84)), delegate { Almanax.Retry(); Invalidate(); }, null, null);
                }
                else
                {
                    int ix = ac.X + Theme.S(16), iy = ac.Y + Theme.S(16), isz = Theme.S(52);
                    Rectangle ir = new Rectangle(ix, iy, isz, isz);
                    Theme.FillRound(g, Theme.Panel, ir, Theme.S(10));
                    if (al.Icon != null) g.DrawImage(al.Icon, new Rectangle(ix + Theme.S(4), iy + Theme.S(4), isz - Theme.S(8), isz - Theme.S(8)));
                    int tx = ix + isz + Theme.S(14), tw = ac.Right - tx - Theme.S(16);
                    TextRenderer.DrawText(g, al.Quantity + " × " + al.ItemName, Theme.Serif(11.5f, FontStyle.Bold), new Rectangle(tx, ac.Y + Theme.S(12), tw - Theme.S(110), Theme.S(24)), Theme.Text, TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                    if (al.Kamas > 0)
                        Theme.DrawText(g, al.Kamas.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("fr-FR")) + L.T(" kamas"), Theme.SmallBold, Theme.AccentSoft,
                            new Rectangle(ac.Right - Theme.S(136), ac.Y + Theme.S(14), Theme.S(120), Theme.S(20)), TextFormatFlags.Right);
                    Theme.DrawText(g, al.BonusName, Theme.SmallBold, Theme.Accent, new Rectangle(tx, ac.Y + Theme.S(38), tw, Theme.S(18)), TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
                    Theme.DrawText(g, al.BonusText, Theme.Small, Theme.Muted, new Rectangle(tx, ac.Y + Theme.S(56), tw, Theme.S(18)), TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
                }
                if (rot.Count > 0)
                {
                    int cy = ac.Y + Theme.S(84);
                    using (Pen sep = new Pen(Color.FromArgb(90, Theme.Border))) g.DrawLine(sep, ac.X + Theme.S(16), cy, ac.Right - Theme.S(16), cy);
                    string today = Almanax.TodayKey;
                    int done = 0;
                    int cx = ac.X + Theme.S(16);
                    for (int i = 0; i < rot.Count; i++)
                    {
                        string nm = rot[i].Name;
                        string dd;
                        bool ok = app.S.AlmanaxDone.TryGetValue(nm, out dd) && dd == today;
                        if (ok) done++;
                        Size ns = TextRenderer.MeasureText(nm, Theme.Small);
                        Rectangle chip = new Rectangle(cx, cy + Theme.S(7), Theme.S(36) + ns.Width, Theme.S(26));
                        if (chip.Right > ac.Right - Theme.S(110)) break;
                        Theme.FillRound(g, ok ? Color.FromArgb(70, Theme.Green) : (Hovered(chip) ? Theme.Hover : Theme.Panel), chip, chip.Height / 2f);
                        RectangleF av = new RectangleF(chip.X + Theme.S(3), chip.Y + Theme.S(3), Theme.S(20), Theme.S(20));
                        Theme.Avatar(g, av, nm, rot[i].Class, false);
                        if (ok)
                            using (Pen ck = new Pen(Theme.Green, Theme.S(2)))
                                g.DrawEllipse(ck, av.X - 1, av.Y - 1, av.Width + 2, av.Height + 2);
                        Theme.DrawText(g, nm, Theme.Small, ok ? Theme.Text : Theme.Muted, new Rectangle(chip.X + Theme.S(28), chip.Y, ns.Width + Theme.S(4), chip.Height), TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
                        string n2 = nm;
                        AddHit(chip, delegate
                        {
                            string v;
                            if (app.S.AlmanaxDone.TryGetValue(n2, out v) && v == Almanax.TodayKey) app.S.AlmanaxDone.Remove(n2);
                            else app.S.AlmanaxDone[n2] = Almanax.TodayKey;
                            app.S.Save(); Invalidate();
                        }, null, ok ? n2 + L.T(" a fait l'Almanax (clic pour annuler)") : L.T("Clic quand ") + n2 + L.T(" a fait son offrande"));
                        cx = chip.Right + Theme.S(6);
                    }
                    Theme.DrawText(g, done + " / " + rot.Count + L.T(" fait") + (done > 1 ? "s" : ""), Theme.SmallBold, done == rot.Count ? Theme.Green : Theme.Faint,
                        new Rectangle(ac.Right - Theme.S(110), cy, Theme.S(94), Theme.S(40)), TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
                }
                y = ac.Bottom + gap + Theme.S(4);
            }

            // --- minuteurs + aujourd'hui (deux colonnes)
            int colW = W > Theme.S(520) ? (W - gap) / 2 : W;
            int leftY = y, rightX = colW == W ? x0 : x0 + colW + gap;
            Heading(g, L.T("Minuteurs en cours"), x0, leftY, colW);
            leftY += Theme.S(28);
            List<TimerItem> running = new List<TimerItem>();
            foreach (TimerItem t in app.S.Timers) if (t.EndUtc > 0) running.Add(t);
            running.Sort(delegate (TimerItem a, TimerItem b) { return a.EndUtc.CompareTo(b.EndUtc); });
            Rectangle tc = new Rectangle(x0, leftY, colW, Theme.S(24) + Math.Max(1, Math.Min(4, running.Count)) * Theme.S(40));
            Card(g, tc, false);
            if (running.Count == 0)
            {
                Theme.DrawText(g, L.T("Aucun minuteur. Astuce : Ctrl+K puis « 30 min »."), Theme.Small, Theme.Faint,
                    new Rectangle(tc.X + Theme.S(16), tc.Y, tc.Width - Theme.S(32), tc.Height), TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
            }
            for (int i = 0; i < running.Count && i < 4; i++)
            {
                TimerItem t = running[i];
                int ry = tc.Y + Theme.S(12) + i * Theme.S(40);
                double left = (new DateTime(t.EndUtc, DateTimeKind.Utc) - DateTime.UtcNow).TotalSeconds;
                float prog = t.Minutes > 0 ? (float)Math.Max(0, Math.Min(1, 1 - left / (t.Minutes * 60.0))) : 0;
                Theme.DrawText(g, t.Name, Theme.Bold, Theme.Text, new Rectangle(tc.X + Theme.S(16), ry, tc.Width - Theme.S(110), Theme.S(18)), TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
                Theme.DrawText(g, Fmt.Duration(left), Theme.SmallBold, Theme.Accent, new Rectangle(tc.Right - Theme.S(100), ry, Theme.S(84), Theme.S(18)), TextFormatFlags.Right);
                RectangleF bar = new RectangleF(tc.X + Theme.S(16), ry + Theme.S(23), tc.Width - Theme.S(32), Theme.S(4));
                Theme.FillRound(g, Theme.Border, bar, Theme.S(2));
                Theme.FillRound(g, Theme.Accent, new RectangleF(bar.X, bar.Y, bar.Width * prog, bar.Height), Theme.S(2));
            }
            AddHit(tc, delegate { app.Main.GoTo(3); }, null, L.T("Ouvrir les minuteurs"));
            leftY = tc.Bottom + gap;

            int rightY = colW == W ? leftY : y;
            Heading(g, L.T("Aujourd'hui"), rightX, rightY, colW);
            rightY += Theme.S(28);
            double act = 0, onl = 0; string top = null; double topAct = 0;
            Dictionary<string, StatEntry> day;
            if (app.S.Stats.TryGetValue(App.Today, out day))
                foreach (KeyValuePair<string, StatEntry> kv in day)
                {
                    if (kv.Key.StartsWith("@")) { onl = Math.Max(onl, kv.Value.Online); continue; }
                    act += kv.Value.Active;
                    if (kv.Value.Active > topAct) { topAct = kv.Value.Active; top = kv.Key; }
                }
            Rectangle sc = new Rectangle(rightX, rightY, colW, Theme.S(24) + 3 * Theme.S(32));
            Card(g, sc, false);
            CombatDay cd = app.Combat.Today();
            string[,] rowsTxt = {
                { L.T("Temps de jeu"), onl > 0 ? Fmt.Short(onl) : "—" },
                { L.T("Perso le plus joué"), top != null ? top + " (" + Fmt.Short(topAct) + ")" : "—" },
                { L.T("Combats"), cd.Fights > 0 ? cd.Fights + " · " + Fmt.Short(cd.Seconds) : "—" } };
            for (int i = 0; i < 3; i++)
            {
                int ry = sc.Y + Theme.S(14) + i * Theme.S(32);
                Theme.DrawText(g, rowsTxt[i, 0], Theme.Small, Theme.Muted, new Rectangle(sc.X + Theme.S(16), ry, sc.Width / 2, Theme.S(22)), TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
                Theme.DrawText(g, rowsTxt[i, 1], Theme.Bold, Theme.Text, new Rectangle(sc.X + sc.Width / 2, ry, sc.Width / 2 - Theme.S(16), Theme.S(22)), TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }
            AddHit(sc, delegate { app.Main.GoTo(5); }, null, L.T("Ouvrir les stats"));
            rightY = sc.Bottom + gap;
            y = Math.Max(leftY, rightY);

            // --- journal
            Heading(g, L.T("Derniers événements"), x0, y, W);
            y += Theme.S(28);
            int shownEv = Math.Min(6, app.Events.Count);
            Rectangle jc = new Rectangle(x0, y, W, Theme.S(16) + Math.Max(1, shownEv) * Theme.S(28));
            Card(g, jc, false);
            if (shownEv == 0)
                Theme.DrawText(g, L.T("Rien pour l'instant."), Theme.Small, Theme.Faint, new Rectangle(jc.X + Theme.S(16), jc.Y, jc.Width, jc.Height), TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            for (int i = 0; i < shownEv; i++)
            {
                App.LogEvent ev = app.Events[i];
                int ry = jc.Y + Theme.S(8) + i * Theme.S(28);
                Color dc = ev.Kind == "deco" ? Theme.Red : ev.Kind == "turn" || ev.Kind == "timer" || ev.Kind == "combat" ? Theme.Accent : Theme.Faint;
                using (SolidBrush b = new SolidBrush(dc)) g.FillEllipse(b, jc.X + Theme.S(16), ry + Theme.S(10), Theme.S(7), Theme.S(7));
                Theme.DrawText(g, ev.At.ToString("HH:mm"), Theme.SmallBold, Theme.Faint, new Rectangle(jc.X + Theme.S(30), ry, Theme.S(48), Theme.S(28)), TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
                Theme.DrawText(g, ev.Text, Theme.Normal, Theme.Text, new Rectangle(jc.X + Theme.S(80), ry, jc.Width - Theme.S(96), Theme.S(28)), TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }
            y = jc.Bottom + Theme.S(16);

            if (Height != y) Height = y;
        }

        // ---------------- souris ----------------

        int HitAt(Point p) { for (int i = hits.Count - 1; i >= 0; i--) if (hits[i].R.Contains(p)) return i; return -1; }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int h = HitAt(e.Location);
            if (h != hover)
            {
                hover = h;
                Cursor = h >= 0 ? Cursors.Hand : Cursors.Default;
                tip.SetToolTip(this, h >= 0 && hits[h].Tip != null ? hits[h].Tip : "");
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            int h = HitAt(e.Location);
            if (h < 0) return;
            Hit hit = hits[h];
            if (e.Button == MouseButtons.Left && hit.Click != null) hit.Click();
            else if (e.Button == MouseButtons.Right && hit.RightClick != null) hit.RightClick();
        }
    }

    /// <summary>Page d'accueil (défilante).</summary>
    public sealed class HomePage : Panel
    {
        public HomePage(App app)
        {
            Dock = DockStyle.Fill;
            BackColor = Theme.Bg;
            AutoScroll = true;
            Padding = new Padding(Theme.S(22), Theme.S(14), Theme.S(22), Theme.S(10));
            HomeView v = new HomeView(app);
            v.Dock = DockStyle.Top;
            v.Height = Theme.S(900);
            Controls.Add(v);
        }
    }

    /// <summary>« Quoi de neuf ? » : les nouveautés, affichées après une mise à jour.</summary>
    public sealed class WhatsNew : Form
    {
        public static readonly string[][] Notes = {
            new string[] { "2.3", L.T("Combats : la mini-barre affiche le tour et le chrono, un repère sous le perso qui joue, et le bilan arrive dans Stats."),
                L.T("Almanax du jour sur l'accueil, avec une case par perso pour ne plus en oublier."),
                L.T("Télécommande : change de perso depuis ton téléphone (QR code dans Options)."),
                L.T("Alt + molette pour passer d'un perso à l'autre, choix de l'écran pour la mosaïque et les aperçus."),
                L.T("Sauvegarde en ligne de tes réglages (Gist GitHub) pour les retrouver sur un autre PC."),
                L.T("Stats sur 7 jours, 30 jours, 90 jours ou 1 an avec graphique, Relais en anglais, bouton « Soutenir ».") },
            new string[] { "2.2", L.T("Accueil : ta team en grandes cartes, actions rapides, minuteurs, bilan du jour et journal des événements."),
                L.T("Palette de commandes (Ctrl+K) : tape « kae », « mosa », « 30 min »… et Entrée."),
                L.T("Sons d'interface (carillon de tour, cloche de minuteur…) générés par Relais, réglables dans Options."),
                L.T("Menus, listes déroulantes et champs aux couleurs de Relais."),
                L.T("Ctrl+1 à Ctrl+7 pour changer de page, infobulle de l'icône avec le perso actif.") },
            new string[] { "2.1", L.T("Identité « Aventure » : bois, parchemin et or, nouveau blason."),
                L.T("Fenêtre sur-mesure avec navigation à icônes."), L.T("Mini-barre repensée et menu radial (clic droit sur un perso).") }
        };

        public WhatsNew(App app)
        {
            Text = L.T("Quoi de neuf dans Relais ?");
            Chrome.Hook(this);
            Icon = app.AppIcon;
            BackColor = Theme.Bg; ForeColor = Theme.Text; Font = Theme.Normal;
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(Theme.S(520), Theme.S(420));
            Panel body = new Panel();
            body.SetBounds(Theme.S(22), Theme.S(16), Theme.S(476), Theme.S(340));
            body.AutoScroll = true;
            body.Paint += delegate (object s, PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                Theme.Hq(g);
                int y = body.AutoScrollPosition.Y;
                Brand.DrawShield(g, new RectangleF(0, y, Theme.S(40), Theme.S(40)));
                TextRenderer.DrawText(g, L.T("Quoi de neuf ?"), Theme.Title, new Point(Theme.S(52), y + Theme.S(6)), Theme.Text, TextFormatFlags.NoPrefix);
                y += Theme.S(56);
                foreach (string[] v in Notes)
                {
                    TextRenderer.DrawText(g, L.T("Version ") + v[0], Theme.Serif(10.5f, FontStyle.Bold), new Point(0, y), Theme.Accent, TextFormatFlags.NoPrefix);
                    y += Theme.S(24);
                    for (int i = 1; i < v.Length; i++)
                    {
                        Rectangle r = new Rectangle(Theme.S(16), y, body.Width - Theme.S(36), Theme.S(200));
                        Size sz = TextRenderer.MeasureText(g, v[i], Theme.Normal, new Size(r.Width, 1000), TextFormatFlags.WordBreak);
                        Brand.Diamond(g, Theme.AccentSoft, Theme.S(6), y + Theme.S(9), Theme.S(3));
                        TextRenderer.DrawText(g, v[i], Theme.Normal, new Rectangle(r.X, y, r.Width, sz.Height), Theme.Text, TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                        y += sz.Height + Theme.S(6);
                    }
                    y += Theme.S(10);
                }
            };
            FlatButton ok = new FlatButton(L.T("Super !")); ok.Primary = true;
            ok.SetBounds(Theme.S(378), Theme.S(370), Theme.S(120), Theme.S(34));
            ok.Click += delegate { Close(); };
            Controls.Add(body); Controls.Add(ok);
        }
    }
}

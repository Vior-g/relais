using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace Relais
{
    /// <summary>Liste dessinée générique : titre, sous-titre, barre de progression, boutons d'action.</summary>
    public sealed class RowList : Control
    {
        public Func<int> CountF;
        public Func<int, string> Title, Sub;
        public Func<int, float> Progress;      // < 0 = pas de barre
        public Func<int, bool> Highlight;
        public Func<int, string[]> Buttons;
        public Action<int, int> OnButton;
        public string Empty = "";
        int hoverRow = -1, hoverBtn = -1;

        public RowList()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Bg;
            Font = Theme.Normal;
        }

        int RowH { get { return Theme.S(50); } }
        int Gap { get { return Theme.S(6); } }
        int Count { get { return CountF == null ? 0 : CountF(); } }

        public int WantedHeight { get { int n = Count; return n == 0 ? Theme.S(40) : n * (RowH + Gap); } }

        Rectangle RowRect(int i) { return new Rectangle(0, i * (RowH + Gap), Width - 1, RowH); }

        List<Rectangle> ButtonRects(int i)
        {
            List<Rectangle> l = new List<Rectangle>();
            string[] b = Buttons == null ? new string[0] : Buttons(i);
            Rectangle r = RowRect(i);
            int x = r.Right - Theme.S(10);
            for (int k = b.Length - 1; k >= 0; k--)
            {
                int w = TextRenderer.MeasureText(b[k], Theme.SmallBold).Width + Theme.S(18);
                x -= w;
                l.Insert(0, new Rectangle(x, r.Y + (r.Height - Theme.S(28)) / 2, w, Theme.S(28)));
                x -= Theme.S(6);
            }
            return l;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Hq(g);
            g.Clear(Theme.Bg);
            int n = Count;
            if (n == 0)
            {
                Theme.DrawText(g, Empty, Theme.Small, Theme.Faint, ClientRectangle, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
                return;
            }
            for (int i = 0; i < n; i++)
            {
                Rectangle r = RowRect(i);
                bool hl = Highlight != null && Highlight(i);
                Theme.FillRound(g, i == hoverRow ? Theme.Hover : Theme.Panel, r, Theme.S(9));
                if (hl) Theme.StrokeRound(g, Theme.Accent, 1.4f, new RectangleF(r.X + 0.7f, r.Y + 0.7f, r.Width - 1.4f, r.Height - 1.4f), Theme.S(9));
                List<Rectangle> br = ButtonRects(i);
                int textRight = br.Count > 0 ? br[0].X - Theme.S(8) : r.Right - Theme.S(10);
                Rectangle tr = new Rectangle(r.X + Theme.S(14), r.Y + Theme.S(7), textRight - r.X - Theme.S(14), Theme.S(20));
                Theme.DrawText(g, Title(i), Theme.Bold, Theme.Text, tr, TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
                Rectangle sr = new Rectangle(tr.X, r.Y + Theme.S(26), tr.Width, Theme.S(18));
                Theme.DrawText(g, Sub == null ? "" : Sub(i), Theme.Small, hl ? Theme.Accent : Theme.Muted, sr, TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
                float p = Progress == null ? -1 : Progress(i);
                if (p >= 0)
                {
                    RectangleF pr = new RectangleF(r.X + Theme.S(14), r.Bottom - Theme.S(6), textRight - r.X - Theme.S(14), Theme.S(3));
                    Theme.FillRound(g, Theme.Border, pr, Theme.S(1));
                    Theme.FillRound(g, Theme.Accent, new RectangleF(pr.X, pr.Y, pr.Width * Math.Min(1, p), pr.Height), Theme.S(1));
                }
                string[] labels = Buttons == null ? new string[0] : Buttons(i);
                for (int k = 0; k < br.Count; k++)
                {
                    bool hv = i == hoverRow && k == hoverBtn;
                    Theme.FillRound(g, hv ? Theme.Border : Theme.Panel2, br[k], Theme.S(6));
                    bool danger = labels[k] == "✕";
                    Theme.DrawText(g, labels[k], Theme.SmallBold, danger ? Theme.Red : Theme.Text, br[k], TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }
            }
        }

        void HitTest(Point p, out int row, out int btn)
        {
            row = -1; btn = -1;
            int n = Count;
            for (int i = 0; i < n; i++)
            {
                if (!RowRect(i).Contains(p)) continue;
                row = i;
                List<Rectangle> br = ButtonRects(i);
                for (int k = 0; k < br.Count; k++) if (br[k].Contains(p)) btn = k;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int r, b;
            HitTest(e.Location, out r, out b);
            if (r != hoverRow || b != hoverBtn) { hoverRow = r; hoverBtn = b; Invalidate(); }
            Cursor = b >= 0 ? Cursors.Hand : Cursors.Default;
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) { hoverRow = -1; hoverBtn = -1; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;
            int r, b;
            HitTest(e.Location, out r, out b);
            if (r >= 0 && b >= 0 && OnButton != null) OnButton(r, b);
        }
    }

    public static class Fmt
    {
        public static string Duration(double seconds)
        {
            if (seconds < 0) seconds = 0;
            long s = (long)Math.Round(seconds);
            long h = s / 3600, m = (s % 3600) / 60, sec = s % 60;
            if (h >= 24) return (h / 24) + " j " + (h % 24) + " h";
            if (h > 0) return h + " h " + m.ToString("00");
            if (m > 0) return m + " min " + sec.ToString("00");
            return sec + " s";
        }

        public static string Short(double seconds)
        {
            long s = (long)Math.Round(Math.Max(0, seconds));
            long h = s / 3600, m = (s % 3600) / 60;
            if (h > 0) return h + " h " + m.ToString("00");
            return m + " min";
        }
    }

    /// <summary>Onglet « Minuteurs » : minuteurs (récoltes, élevage, boosts…) et rappels quotidiens.</summary>
    public sealed class TimersPage : Panel
    {
        readonly App app;
        readonly RowList timers = new RowList(), reminders = new RowList();
        readonly Timer refresh = new Timer();

        static TextBox Input(string cue)
        {
            TextBox t = new TextBox();
            t.BorderStyle = BorderStyle.FixedSingle;
            t.BackColor = Theme.Panel2;
            t.ForeColor = Theme.Text;
            t.Font = Theme.Normal;
            return t;
        }

        static NumericUpDown Num(int max, int val)
        {
            NumericUpDown n = new NumericUpDown();
            n.Minimum = 0; n.Maximum = max; n.Value = val;
            n.BackColor = Theme.Panel2; n.ForeColor = Theme.Text; n.BorderStyle = BorderStyle.FixedSingle;
            n.Font = Theme.Normal; n.TextAlign = HorizontalAlignment.Center;
            return n;
        }

        static Label Lbl(string t)
        {
            Label l = new Label();
            l.Text = t; l.ForeColor = Theme.Muted; l.Font = Theme.Small; l.AutoSize = false;
            l.TextAlign = ContentAlignment.MiddleLeft;
            return l;
        }

        public TimersPage(App app)
        {
            this.app = app;
            Dock = DockStyle.Fill;
            BackColor = Theme.Bg;
            AutoScroll = true;
            Padding = new Padding(Theme.S(18), Theme.S(4), Theme.S(18), Theme.S(10));

            // --- ajout de minuteur
            Panel addT = new Panel();
            addT.Height = Theme.S(40);
            TextBox tName = Input("Nom");
            tName.Text = "Récolte";
            NumericUpDown hh = Num(240, 1), mm = Num(59, 0);
            Label lh = Lbl("h"), lm = Lbl("min");
            FlatButton addTimer = new FlatButton("Ajouter"); addTimer.Primary = true;
            addT.Controls.Add(tName); addT.Controls.Add(hh); addT.Controls.Add(lh); addT.Controls.Add(mm); addT.Controls.Add(lm); addT.Controls.Add(addTimer);
            addT.Resize += delegate
            {
                int w = addT.Width, y = Theme.S(4);
                int bw = Theme.S(90), nw = Theme.S(56), lw = Theme.S(30), g = Theme.S(6);
                addTimer.SetBounds(w - bw, y, bw, Theme.S(30));
                int x = w - bw - g;
                lm.SetBounds(x - lw, y, lw, Theme.S(30)); x -= lw;
                mm.SetBounds(x - nw, y + Theme.S(3), nw, Theme.S(26)); x -= nw + g;
                lh.SetBounds(x - Theme.S(16), y, Theme.S(16), Theme.S(30)); x -= Theme.S(16);
                hh.SetBounds(x - nw, y + Theme.S(3), nw, Theme.S(26)); x -= nw + g;
                tName.SetBounds(0, y + Theme.S(3), Math.Max(Theme.S(60), x), Theme.S(26));
            };
            addTimer.Click += delegate
            {
                int minutes = (int)hh.Value * 60 + (int)mm.Value;
                if (minutes <= 0) { app.Toast.ShowMessage("Durée nulle", false); return; }
                TimerItem t = new TimerItem();
                t.Name = tName.Text.Trim().Length == 0 ? "Minuteur" : tName.Text.Trim();
                t.Minutes = minutes;
                t.EndUtc = DateTime.UtcNow.AddMinutes(minutes).Ticks;
                app.S.Timers.Add(t);
                app.TimersEdited();
                app.Toast.ShowMessage("Minuteur lancé : " + t.Name + " (" + Fmt.Duration(minutes * 60) + ")", false);
            };

            timers.Empty = "Aucun minuteur. Exemples : récolte de ressources, enclos (élevage), fin d'un parchemin ou d'un boost.";
            timers.CountF = delegate { return app.S.Timers.Count; };
            timers.Title = delegate (int i) { return app.S.Timers[i].Name; };
            timers.Sub = delegate (int i)
            {
                TimerItem t = app.S.Timers[i];
                if (t.EndUtc == 0) return "Arrêté · durée " + Fmt.Duration(t.Minutes * 60);
                double left = (new DateTime(t.EndUtc, DateTimeKind.Utc) - DateTime.UtcNow).TotalSeconds;
                return "Reste " + Fmt.Duration(left) + " · fin à " + new DateTime(t.EndUtc, DateTimeKind.Utc).ToLocalTime().ToString("HH:mm");
            };
            timers.Highlight = delegate (int i) { return app.S.Timers[i].EndUtc != 0; };
            timers.Progress = delegate (int i)
            {
                TimerItem t = app.S.Timers[i];
                if (t.EndUtc == 0 || t.Minutes <= 0) return -1;
                double left = (new DateTime(t.EndUtc, DateTimeKind.Utc) - DateTime.UtcNow).TotalSeconds;
                return (float)(1 - left / (t.Minutes * 60.0));
            };
            timers.Buttons = delegate (int i) { return app.S.Timers[i].EndUtc == 0 ? new string[] { "Lancer", "✕" } : new string[] { "Relancer", "Stop", "✕" }; };
            timers.OnButton = delegate (int i, int b)
            {
                TimerItem t = app.S.Timers[i];
                string[] labels = timers.Buttons(i);
                string what = labels[b];
                if (what == "✕") app.S.Timers.RemoveAt(i);
                else if (what == "Stop") t.EndUtc = 0;
                else t.EndUtc = DateTime.UtcNow.AddMinutes(t.Minutes).Ticks;
                app.TimersEdited();
            };

            // --- rappels quotidiens
            Panel addR = new Panel();
            addR.Height = Theme.S(40);
            TextBox rName = Input("Nom");
            rName.Text = "Almanax";
            TextBox rTime = Input("HH:mm");
            rTime.Text = "09:00";
            rTime.TextAlign = HorizontalAlignment.Center;
            Label la = Lbl("à");
            FlatButton addRem = new FlatButton("Ajouter"); addRem.Primary = true;
            addR.Controls.Add(rName); addR.Controls.Add(la); addR.Controls.Add(rTime); addR.Controls.Add(addRem);
            addR.Resize += delegate
            {
                int w = addR.Width, y = Theme.S(4), bw = Theme.S(90), tw = Theme.S(70), g = Theme.S(6);
                addRem.SetBounds(w - bw, y, bw, Theme.S(30));
                rTime.SetBounds(w - bw - g - tw, y + Theme.S(3), tw, Theme.S(26));
                la.SetBounds(w - bw - g - tw - Theme.S(22), y, Theme.S(18), Theme.S(30));
                rName.SetBounds(0, y + Theme.S(3), Math.Max(Theme.S(60), w - bw - g - tw - Theme.S(28)), Theme.S(26));
            };
            addRem.Click += delegate
            {
                DateTime parsed;
                string txt = rTime.Text.Trim().Replace('h', ':').Replace('H', ':');
                if (txt.EndsWith(":")) txt += "00";
                if (!DateTime.TryParseExact(txt, new string[] { "H:mm", "HH:mm", "H:m" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
                {
                    MessageBox.Show(FindForm(), "Heure invalide. Exemple : 09:00 ou 21h30.", "Relais");
                    return;
                }
                Reminder r = new Reminder();
                r.Name = rName.Text.Trim().Length == 0 ? "Rappel" : rName.Text.Trim();
                r.Time = parsed.ToString("HH:mm");
                // ne pas le déclencher tout de suite si l'heure est déjà passée aujourd'hui
                if (string.CompareOrdinal(DateTime.Now.ToString("HH:mm"), r.Time) >= 0) r.LastFired = App.Today;
                app.S.Reminders.Add(r);
                app.S.Reminders.Sort(delegate (Reminder a, Reminder b2) { return string.CompareOrdinal(a.Time, b2.Time); });
                app.TimersEdited();
            };

            reminders.Empty = "Aucun rappel. Exemples : Almanax du jour, quête quotidienne, Kolizéum.";
            reminders.CountF = delegate { return app.S.Reminders.Count; };
            reminders.Title = delegate (int i) { return app.S.Reminders[i].Time + " — " + app.S.Reminders[i].Name; };
            reminders.Sub = delegate (int i)
            {
                Reminder r = app.S.Reminders[i];
                if (!r.Enabled) return "Désactivé";
                return r.LastFired == App.Today ? "Fait aujourd'hui · prochain demain à " + r.Time : "Aujourd'hui à " + r.Time;
            };
            reminders.Highlight = delegate (int i) { Reminder r = app.S.Reminders[i]; return r.Enabled && r.LastFired != App.Today; };
            reminders.Buttons = delegate (int i) { return new string[] { app.S.Reminders[i].Enabled ? "Désactiver" : "Activer", "✕" }; };
            reminders.OnButton = delegate (int i, int b)
            {
                if (b == 1) app.S.Reminders.RemoveAt(i);
                else app.S.Reminders[i].Enabled = !app.S.Reminders[i].Enabled;
                app.TimersEdited();
            };

            Label hint = new Label();
            hint.Text = "À la fin d'un minuteur ou à l'heure d'un rappel : notification à l'écran, son, et bulle Windows. Les minuteurs continuent même si Relais est fermé puis relancé.";
            hint.ForeColor = Theme.Faint; hint.Font = Theme.Small; hint.AutoSize = false; hint.Height = Theme.S(40);

            Control[] items = {
                Title("Minuteurs"), addT, Space(6), timers,
                Title("Rappels quotidiens"), addR, Space(6), reminders, Space(6), hint
            };
            for (int i = items.Length - 1; i >= 0; i--) { items[i].Dock = DockStyle.Top; Controls.Add(items[i]); }

            refresh.Interval = 1000;
            refresh.Tick += delegate { if (Visible) Relayout(); };
            refresh.Start();
            app.TimersChanged += delegate { Relayout(); };
            Relayout();
        }

        static Label Title(string t)
        {
            return new OrnamentTitle(t);
        }

        static Panel Space(int h) { Panel p = new Panel(); p.Height = Theme.S(h); return p; }

        void Relayout()
        {
            if (timers.Height != timers.WantedHeight) timers.Height = timers.WantedHeight;
            if (reminders.Height != reminders.WantedHeight) reminders.Height = reminders.WantedHeight;
            timers.Invalidate();
            reminders.Invalidate();
        }
    }

    /// <summary>Graphique de temps de jeu (barres horizontales).</summary>
    public sealed class StatsView : Control
    {
        readonly App app;
        public int Days = 1;
        List<KeyValuePair<string, StatEntry>> chars = new List<KeyValuePair<string, StatEntry>>();
        List<KeyValuePair<string, StatEntry>> profiles = new List<KeyValuePair<string, StatEntry>>();

        public StatsView(App app)
        {
            this.app = app;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Bg;
        }

        int RowH { get { return Theme.S(46); } }
        int HeadH { get { return Theme.S(34); } }

        public int Compute()
        {
            Dictionary<string, StatEntry> c = new Dictionary<string, StatEntry>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, StatEntry> p = new Dictionary<string, StatEntry>(StringComparer.OrdinalIgnoreCase);
            for (int d = 0; d < Days; d++)
            {
                string key = DateTime.Now.AddDays(-d).ToString("yyyy-MM-dd");
                Dictionary<string, StatEntry> day;
                if (!app.S.Stats.TryGetValue(key, out day)) continue;
                foreach (KeyValuePair<string, StatEntry> kv in day)
                {
                    bool isProfile = kv.Key.StartsWith("@");
                    Dictionary<string, StatEntry> target = isProfile ? p : c;
                    string name = isProfile ? kv.Key.Substring(1) : kv.Key;
                    StatEntry e;
                    if (!target.TryGetValue(name, out e)) { e = new StatEntry(); target[name] = e; }
                    e.Active += kv.Value.Active;
                    e.Online += kv.Value.Online;
                }
            }
            chars = new List<KeyValuePair<string, StatEntry>>(c);
            profiles = new List<KeyValuePair<string, StatEntry>>(p);
            Comparison<KeyValuePair<string, StatEntry>> cmp = delegate (KeyValuePair<string, StatEntry> a, KeyValuePair<string, StatEntry> b) { return b.Value.Online.CompareTo(a.Value.Online); };
            chars.Sort(cmp);
            profiles.Sort(cmp);
            int h = HeadH + Math.Max(1, chars.Count) * RowH + Theme.S(10) + HeadH + Math.Max(1, profiles.Count) * RowH;
            Height = h;
            Invalidate();
            return h;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Hq(g);
            g.Clear(Theme.Bg);
            int y = 0;
            y = Block(g, y, "Par personnage", chars, true);
            y += Theme.S(10);
            Block(g, y, "Par profil", profiles, false);
        }

        int Block(Graphics g, int y, string title, List<KeyValuePair<string, StatEntry>> rows, bool avatars)
        {
            double total = 0;
            foreach (KeyValuePair<string, StatEntry> kv in rows) total += kv.Value.Active;
            Theme.DrawText(g, title.ToUpperInvariant(), Theme.Section, Theme.Faint, new Rectangle(0, y, Width / 2, HeadH), TextFormatFlags.Left | TextFormatFlags.Bottom);
            if (rows.Count > 0)
                Theme.DrawText(g, "total actif " + Fmt.Short(total), Theme.Small, Theme.Muted, new Rectangle(Width / 2, y, Width / 2, HeadH), TextFormatFlags.Right | TextFormatFlags.Bottom);
            y += HeadH;
            if (rows.Count == 0)
            {
                Theme.DrawText(g, "Pas encore de données pour cette période.", Theme.Small, Theme.Faint, new Rectangle(0, y, Width, RowH), TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
                return y + RowH;
            }
            double max = 1;
            foreach (KeyValuePair<string, StatEntry> kv in rows) max = Math.Max(max, kv.Value.Online);
            foreach (KeyValuePair<string, StatEntry> kv in rows)
            {
                int x = 0;
                if (avatars)
                {
                    int av = Theme.S(28);
                    GameWindow gw = app.WindowOf(kv.Key);
                    Theme.Avatar(g, new RectangleF(0, y + (RowH - av) / 2f, av, av), kv.Key, gw != null ? gw.Class : "", false);
                    x = av + Theme.S(10);
                }
                int labelW = Theme.S(130);
                Theme.DrawText(g, kv.Key, Theme.Bold, Theme.Text, new Rectangle(x, y + Theme.S(4), labelW, Theme.S(20)), TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
                Theme.DrawText(g, Fmt.Short(kv.Value.Active) + " actif", Theme.Small, Theme.Muted, new Rectangle(x, y + Theme.S(22), labelW, Theme.S(18)), TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
                int bx = x + labelW + Theme.S(8);
                int bw = Width - bx - Theme.S(90);
                if (bw > 20)
                {
                    float bh = Theme.S(10);
                    float by = y + (RowH - bh) / 2f;
                    float wOnline = (float)(bw * kv.Value.Online / max);
                    float wActive = Math.Min(wOnline, (float)(bw * kv.Value.Active / max));
                    Theme.FillRound(g, Theme.Panel2, new RectangleF(bx, by, bw, bh), bh / 2);
                    if (wOnline > 1) Theme.FillRound(g, Theme.Border, new RectangleF(bx, by, wOnline, bh), bh / 2);
                    if (wActive > 1) Theme.FillRound(g, Theme.Accent, new RectangleF(bx, by, wActive, bh), bh / 2);
                }
                Theme.DrawText(g, Fmt.Short(kv.Value.Online), Theme.SmallBold, Theme.Text, new Rectangle(Width - Theme.S(86), y, Theme.S(86), Theme.S(26)), TextFormatFlags.Right | TextFormatFlags.Bottom);
                Theme.DrawText(g, "connecté", Theme.Small, Theme.Faint, new Rectangle(Width - Theme.S(86), y + Theme.S(24), Theme.S(86), Theme.S(18)), TextFormatFlags.Right | TextFormatFlags.Top);
                y += RowH;
            }
            return y;
        }
    }

    /// <summary>Onglet « Stats ».</summary>
    public sealed class StatsPage : Panel
    {
        readonly App app;
        readonly StatsView view;
        readonly FlatButton[] periods;
        readonly Timer refresh = new Timer();

        public StatsPage(App app)
        {
            this.app = app;
            Dock = DockStyle.Fill;
            BackColor = Theme.Bg;
            AutoScroll = true;
            Padding = new Padding(Theme.S(18), Theme.S(10), Theme.S(18), Theme.S(10));

            Panel bar = new Panel();
            bar.Height = Theme.S(42);
            string[] names = { "Aujourd'hui", "7 jours", "30 jours" };
            int[] days = { 1, 7, 30 };
            periods = new FlatButton[3];
            for (int i = 0; i < 3; i++)
            {
                FlatButton b = new FlatButton(names[i]);
                int d = days[i], idx = i;
                b.Click += delegate { view.Days = d; Select(idx); view.Compute(); };
                periods[i] = b;
                bar.Controls.Add(b);
            }
            FlatButton reset = new FlatButton("Réinitialiser"); reset.Danger = true;
            reset.Click += delegate
            {
                if (MessageBox.Show(FindForm(), "Effacer toutes les statistiques ?", "Relais", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                { app.ResetStats(); view.Compute(); }
            };
            bar.Controls.Add(reset);
            bar.Resize += delegate
            {
                int x = 0, w = Theme.S(104), g = Theme.S(6);
                for (int i = 0; i < 3; i++) { periods[i].SetBounds(x, 0, w, Theme.S(32)); x += w + g; }
                reset.SetBounds(bar.Width - Theme.S(110), 0, Theme.S(110), Theme.S(32));
            };

            view = new StatsView(app);
            Label hint = new Label();
            hint.Text = "Barre colorée : temps où le perso était au premier plan. Barre grise : temps connecté. Les données restent sur ton PC (90 jours).";
            hint.ForeColor = Theme.Faint; hint.Font = Theme.Small; hint.AutoSize = false; hint.Height = Theme.S(44);

            Control[] items = { bar, view, hint };
            for (int i = items.Length - 1; i >= 0; i--) { items[i].Dock = DockStyle.Top; Controls.Add(items[i]); }

            Select(0);
            refresh.Interval = 5000;
            refresh.Tick += delegate { if (Visible) view.Compute(); };
            refresh.Start();
            VisibleChanged += delegate { if (Visible) view.Compute(); };
            view.Compute();
        }

        void Select(int idx)
        {
            for (int i = 0; i < periods.Length; i++) { periods[i].Primary = i == idx; periods[i].Invalidate(); }
        }
    }
}

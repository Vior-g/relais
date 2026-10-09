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
            if (h >= 24) return (h / 24) + L.T(" j ") + (h % 24) + L.T(" h");
            if (h > 0) return h + L.T(" h ") + m.ToString("00");
            if (m > 0) return m + L.T(" min ") + sec.ToString("00");
            return sec + L.T(" s");
        }

        public static string Short(double seconds)
        {
            long s = (long)Math.Round(Math.Max(0, seconds));
            long h = s / 3600, m = (s % 3600) / 60;
            if (h > 0) return h + L.T(" h ") + m.ToString("00");
            return m + L.T(" min");
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
            TextBox tName = Input(L.T("Nom"));
            tName.Text = L.T("Récolte");
            NumericUpDown hh = Num(240, 1), mm = Num(59, 0);
            Label lh = Lbl("h"), lm = Lbl("min");
            FlatButton addTimer = new FlatButton(L.T("Ajouter")); addTimer.Primary = true;
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
                if (minutes <= 0) { app.Toast.ShowMessage(L.T("Durée nulle"), false); return; }
                TimerItem t = new TimerItem();
                t.Name = tName.Text.Trim().Length == 0 ? L.T("Minuteur") : tName.Text.Trim();
                t.Minutes = minutes;
                t.EndUtc = DateTime.UtcNow.AddMinutes(minutes).Ticks;
                app.S.Timers.Add(t);
                app.TimersEdited();
                app.Toast.ShowMessage(L.T("Minuteur lancé : ") + t.Name + " (" + Fmt.Duration(minutes * 60) + ")", false);
            };

            timers.Empty = L.T("Aucun minuteur. Exemples : récolte de ressources, enclos (élevage), fin d'un parchemin ou d'un boost.");
            timers.CountF = delegate { return app.S.Timers.Count; };
            timers.Title = delegate (int i) { return app.S.Timers[i].Name; };
            timers.Sub = delegate (int i)
            {
                TimerItem t = app.S.Timers[i];
                if (t.EndUtc == 0) return L.T("Arrêté · durée ") + Fmt.Duration(t.Minutes * 60);
                double left = (new DateTime(t.EndUtc, DateTimeKind.Utc) - DateTime.UtcNow).TotalSeconds;
                return L.T("Reste ") + Fmt.Duration(left) + L.T(" · fin à ") + new DateTime(t.EndUtc, DateTimeKind.Utc).ToLocalTime().ToString("HH:mm");
            };
            timers.Highlight = delegate (int i) { return app.S.Timers[i].EndUtc != 0; };
            timers.Progress = delegate (int i)
            {
                TimerItem t = app.S.Timers[i];
                if (t.EndUtc == 0 || t.Minutes <= 0) return -1;
                double left = (new DateTime(t.EndUtc, DateTimeKind.Utc) - DateTime.UtcNow).TotalSeconds;
                return (float)(1 - left / (t.Minutes * 60.0));
            };
            timers.Buttons = delegate (int i) { return app.S.Timers[i].EndUtc == 0 ? new string[] { L.T("Lancer"), "✕" } : new string[] { L.T("Relancer"), "Stop", "✕" }; };
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
            TextBox rName = Input(L.T("Nom"));
            rName.Text = "Almanax";
            TextBox rTime = Input("HH:mm");
            rTime.Text = "09:00";
            rTime.TextAlign = HorizontalAlignment.Center;
            Label la = Lbl(L.T("à"));
            FlatButton addRem = new FlatButton(L.T("Ajouter")); addRem.Primary = true;
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
                    MessageBox.Show(FindForm(), L.T("Heure invalide. Exemple : 09:00 ou 21h30."), "Relais");
                    return;
                }
                Reminder r = new Reminder();
                r.Name = rName.Text.Trim().Length == 0 ? L.T("Rappel") : rName.Text.Trim();
                r.Time = parsed.ToString("HH:mm");
                // ne pas le déclencher tout de suite si l'heure est déjà passée aujourd'hui
                if (string.CompareOrdinal(DateTime.Now.ToString("HH:mm"), r.Time) >= 0) r.LastFired = App.Today;
                app.S.Reminders.Add(r);
                app.S.Reminders.Sort(delegate (Reminder a, Reminder b2) { return string.CompareOrdinal(a.Time, b2.Time); });
                app.TimersEdited();
            };

            reminders.Empty = L.T("Aucun rappel. Exemples : Almanax du jour, quête quotidienne, Kolizéum.");
            reminders.CountF = delegate { return app.S.Reminders.Count; };
            reminders.Title = delegate (int i) { return app.S.Reminders[i].Time + " — " + app.S.Reminders[i].Name; };
            reminders.Sub = delegate (int i)
            {
                Reminder r = app.S.Reminders[i];
                if (!r.Enabled) return L.T("Désactivé");
                return r.LastFired == App.Today ? L.T("Fait aujourd'hui · prochain demain à ") + r.Time : L.T("Aujourd'hui à ") + r.Time;
            };
            reminders.Highlight = delegate (int i) { Reminder r = app.S.Reminders[i]; return r.Enabled && r.LastFired != App.Today; };
            reminders.Buttons = delegate (int i) { return new string[] { app.S.Reminders[i].Enabled ? L.T("Désactiver") : L.T("Activer"), "✕" }; };
            reminders.OnButton = delegate (int i, int b)
            {
                if (b == 1) app.S.Reminders.RemoveAt(i);
                else app.S.Reminders[i].Enabled = !app.S.Reminders[i].Enabled;
                app.TimersEdited();
            };

            Label hint = new Label();
            hint.Text = L.T("À la fin d'un minuteur ou à l'heure d'un rappel : notification à l'écran, son, et bulle Windows. Les minuteurs continuent même si Relais est fermé puis relancé.");
            hint.ForeColor = Theme.Faint; hint.Font = Theme.Small; hint.AutoSize = false; hint.Height = Theme.S(40);

            Control[] items = {
                Title(L.T("Minuteurs")), addT, Space(6), timers,
                Title(L.T("Rappels quotidiens")), addR, Space(6), reminders, Space(6), hint
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
        sealed class Bucket { public string Label; public double Online, Active; public bool Current; }
        readonly List<Bucket> buckets = new List<Bucket>();
        CombatDay fights = new CombatDay();

        int ChartH { get { return Days > 1 ? HeadH + Theme.S(170) + Theme.S(10) : 0; } }
        int FightH { get { return HeadH + Theme.S(64) + Theme.S(10); } }

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
            // --- courbe : par jour (≤ 30 j), par semaine (90 j) ou par mois (1 an)
            buckets.Clear();
            string[] months = { "janv", L.T("févr"), "mars", "avr", "mai", "juin", "juil", L.T("août"), "sept", "oct", "nov", L.T("déc") };
            if (Days > 1)
            {
                int step = Days <= 30 ? 1 : Days <= 90 ? 7 : 0;
                if (step > 0)
                {
                    int n = Days / step;
                    for (int b = n - 1; b >= 0; b--)
                    {
                        Bucket bk = new Bucket();
                        DateTime last = DateTime.Now.Date.AddDays(-b * step);
                        bk.Label = step == 1 ? (Days <= 7 ? last.ToString("ddd", System.Globalization.CultureInfo.GetCultureInfo(L.En ? "en-GB" : "fr-FR")) : last.Day.ToString()) : last.AddDays(-6).ToString("dd/MM");
                        bk.Current = b == 0;
                        for (int k = 0; k < step; k++) AddDay(bk, last.AddDays(-k));
                        buckets.Add(bk);
                    }
                }
                else
                {
                    DateTime first = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1).AddMonths(-11);
                    for (int m = 0; m < 12; m++)
                    {
                        DateTime ms = first.AddMonths(m);
                        Bucket bk = new Bucket();
                        bk.Label = L.T(months[ms.Month - 1]);
                        bk.Current = m == 11;
                        for (DateTime d = ms; d < ms.AddMonths(1) && d <= DateTime.Now.Date; d = d.AddDays(1)) AddDay(bk, d);
                        buckets.Add(bk);
                    }
                }
            }
            // --- combats de la période
            fights = new CombatDay();
            for (int d = 0; d < Days; d++)
            {
                CombatDay cd;
                if (!app.S.Combat.TryGetValue(DateTime.Now.AddDays(-d).ToString("yyyy-MM-dd"), out cd)) continue;
                fights.Fights += cd.Fights; fights.Turns += cd.Turns; fights.Seconds += cd.Seconds;
                fights.Longest = Math.Max(fights.Longest, cd.Longest);
            }

            chars = new List<KeyValuePair<string, StatEntry>>(c);
            profiles = new List<KeyValuePair<string, StatEntry>>(p);
            Comparison<KeyValuePair<string, StatEntry>> cmp = delegate (KeyValuePair<string, StatEntry> a, KeyValuePair<string, StatEntry> b) { return b.Value.Online.CompareTo(a.Value.Online); };
            chars.Sort(cmp);
            profiles.Sort(cmp);
            int h = ChartH + FightH + HeadH + Math.Max(1, chars.Count) * RowH + Theme.S(10) + HeadH + Math.Max(1, profiles.Count) * RowH;
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
            if (Days > 1) y = Chart(g, y);
            y = Fights(g, y);
            y = Block(g, y, L.T("Par personnage"), chars, true);
            y += Theme.S(10);
            Block(g, y, L.T("Par profil"), profiles, false);
        }

        void AddDay(Bucket bk, DateTime day)
        {
            Dictionary<string, StatEntry> d;
            if (!app.S.Stats.TryGetValue(day.ToString("yyyy-MM-dd"), out d)) return;
            foreach (KeyValuePair<string, StatEntry> kv in d)
            {
                if (kv.Key.StartsWith("@")) bk.Online += kv.Value.Online;
                else bk.Active += kv.Value.Active;
            }
        }

        int Chart(Graphics g, int y)
        {
            double total = 0, max = 1;
            foreach (Bucket b in buckets) { total += b.Online; max = Math.Max(max, Math.Max(b.Online, b.Active)); }
            string unit = Days <= 30 ? L.T("par jour") : Days <= 90 ? L.T("par semaine") : L.T("par mois");
            Theme.DrawText(g, (L.T("Temps de jeu ") + unit).ToUpperInvariant(), Theme.Section, Theme.Faint, new Rectangle(0, y, Width / 2, HeadH), TextFormatFlags.Left | TextFormatFlags.Bottom);
            Theme.DrawText(g, L.T("total ") + Fmt.Short(total) + L.T(" · moyenne ") + Fmt.Short(buckets.Count > 0 ? total / buckets.Count : 0), Theme.Small, Theme.Muted,
                new Rectangle(Width / 2, y, Width / 2, HeadH), TextFormatFlags.Right | TextFormatFlags.Bottom);
            y += HeadH;
            Rectangle area = new Rectangle(0, y + Theme.S(8), Width, Theme.S(140));
            using (Pen grid = new Pen(Color.FromArgb(70, Theme.Border)))
                for (int i = 0; i <= 2; i++) { int gy = area.Y + area.Height * i / 2; g.DrawLine(grid, 0, gy, Width, gy); }
            Theme.DrawText(g, Fmt.Short(max), Theme.F(7f, FontStyle.Regular), Theme.Faint, new Rectangle(0, area.Y - Theme.S(2), Theme.S(80), Theme.S(14)), TextFormatFlags.Left);
            int n = Math.Max(1, buckets.Count);
            float slot = area.Width / (float)n, bw = Math.Max(2f, Math.Min(slot * 0.62f, Theme.S(34)));
            int labelEvery = n > 16 ? (int)Math.Ceiling(n / 10.0) : 1;
            for (int i = 0; i < buckets.Count; i++)
            {
                Bucket b = buckets[i];
                float cx = slot * i + slot / 2f;
                float ho = (float)(area.Height * b.Online / max), ha = (float)(area.Height * Math.Min(b.Active, Math.Max(b.Online, b.Active)) / max);
                RectangleF ro = new RectangleF(cx - bw / 2, area.Bottom - ho, bw, ho);
                RectangleF ra = new RectangleF(cx - bw / 2, area.Bottom - ha, bw, ha);
                if (ho > 1) Theme.FillRound(g, Color.FromArgb(150, Theme.Border), ro, Math.Min(bw / 2, Theme.S(4)));
                if (ha > 1) Theme.FillRound(g, b.Current ? ControlPaint.Light(Theme.Accent, 0.2f) : Theme.Accent, ra, Math.Min(bw / 2, Theme.S(4)));
                if (i % labelEvery == 0 || b.Current)
                    Theme.DrawText(g, b.Label, Theme.F(7f, b.Current ? FontStyle.Bold : FontStyle.Regular), b.Current ? Theme.Text : Theme.Faint,
                        new Rectangle((int)(cx - slot / 2 - Theme.S(10)), area.Bottom + Theme.S(3), (int)slot + Theme.S(20), Theme.S(16)), TextFormatFlags.HorizontalCenter);
            }
            return y + Theme.S(170) + Theme.S(10);
        }

        int Fights(Graphics g, int y)
        {
            Theme.DrawText(g, L.T("Combats").ToUpperInvariant(), Theme.Section, Theme.Faint, new Rectangle(0, y, Width / 2, HeadH), TextFormatFlags.Left | TextFormatFlags.Bottom);
            y += HeadH;
            string[,] cells = {
                { fights.Fights.ToString(), L.T("combats") },
                { fights.Fights > 0 ? Fmt.Short(fights.Seconds) : "—", L.T("en combat") },
                { fights.Fights > 0 ? Fmt.Short(fights.Seconds / fights.Fights) : "—", L.T("durée moyenne") },
                { fights.Fights > 0 ? (fights.Turns / (double)fights.Fights).ToString("0.#") : "—", L.T("tours en moyenne") },
                { fights.Longest > 0 ? Fmt.Short(fights.Longest) : "—", L.T("le plus long") } };
            int n = 5, gap = Theme.S(8), cw = (Width - (n - 1) * gap) / n, ch = Theme.S(58);
            for (int i = 0; i < n; i++)
            {
                Rectangle r = new Rectangle(i * (cw + gap), y + Theme.S(4), cw, ch);
                Theme.FillRound(g, Theme.Panel2, r, Theme.S(10));
                Theme.DrawText(g, cells[i, 0], Theme.Serif(13f, FontStyle.Bold), i == 0 ? Theme.Accent : Theme.Text, new Rectangle(r.X, r.Y + Theme.S(6), r.Width, Theme.S(26)), TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis);
                Theme.DrawText(g, cells[i, 1], Theme.Small, Theme.Faint, new Rectangle(r.X, r.Y + Theme.S(34), r.Width, Theme.S(18)), TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis);
            }
            return y + Theme.S(64) + Theme.S(10);
        }

        int Block(Graphics g, int y, string title, List<KeyValuePair<string, StatEntry>> rows, bool avatars)
        {
            double total = 0;
            foreach (KeyValuePair<string, StatEntry> kv in rows) total += kv.Value.Active;
            Theme.DrawText(g, title.ToUpperInvariant(), Theme.Section, Theme.Faint, new Rectangle(0, y, Width / 2, HeadH), TextFormatFlags.Left | TextFormatFlags.Bottom);
            if (rows.Count > 0)
                Theme.DrawText(g, L.T("total actif ") + Fmt.Short(total), Theme.Small, Theme.Muted, new Rectangle(Width / 2, y, Width / 2, HeadH), TextFormatFlags.Right | TextFormatFlags.Bottom);
            y += HeadH;
            if (rows.Count == 0)
            {
                Theme.DrawText(g, L.T("Pas encore de données pour cette période."), Theme.Small, Theme.Faint, new Rectangle(0, y, Width, RowH), TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
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
                Theme.DrawText(g, Fmt.Short(kv.Value.Active) + L.T(" actif"), Theme.Small, Theme.Muted, new Rectangle(x, y + Theme.S(22), labelW, Theme.S(18)), TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
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
                Theme.DrawText(g, L.T("connecté"), Theme.Small, Theme.Faint, new Rectangle(Width - Theme.S(86), y + Theme.S(24), Theme.S(86), Theme.S(18)), TextFormatFlags.Right | TextFormatFlags.Top);
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
            string[] names = { L.T("Aujourd'hui"), L.T("7 jours"), L.T("30 jours"), L.T("90 jours"), L.T("1 an") };
            int[] days = { 1, 7, 30, 91, 365 };
            periods = new FlatButton[5];
            for (int i = 0; i < 5; i++)
            {
                FlatButton b = new FlatButton(names[i]);
                int d = days[i], idx = i;
                b.Click += delegate { view.Days = d; Select(idx); view.Compute(); };
                periods[i] = b;
                bar.Controls.Add(b);
            }
            FlatButton reset = new FlatButton(L.T("Réinitialiser")); reset.Danger = true;
            reset.Click += delegate
            {
                if (MessageBox.Show(FindForm(), L.T("Effacer toutes les statistiques ?"), "Relais", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                { app.ResetStats(); view.Compute(); }
            };
            bar.Controls.Add(reset);
            bar.Resize += delegate
            {
                int x = 0, w = Math.Min(Theme.S(100), (bar.Width - Theme.S(120) - 4 * Theme.S(6)) / 5), g = Theme.S(6);
                for (int i = 0; i < 5; i++) { periods[i].SetBounds(x, 0, w, Theme.S(32)); x += w + g; }
                reset.SetBounds(bar.Width - Theme.S(110), 0, Theme.S(110), Theme.S(32));
            };

            view = new StatsView(app);
            Label hint = new Label();
            hint.Text = L.T("Barre colorée : temps où le perso était au premier plan. Barre grise : temps connecté. Les données restent sur ton PC (1 an). Les combats sont repérés grâce aux alertes de tour.");
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

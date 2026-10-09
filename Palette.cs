using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace Relais
{
    /// <summary>Une commande de la palette.</summary>
    public sealed class Cmd
    {
        public string Title, Sub, Group;
        public int Icon;          // icône de navigation (0..6) ou -1 = avatar
        public string Avatar;     // nom du perso pour l'avatar
        public string AvatarClass = "";
        public Action Run;
        public bool KeepFocus;    // l'action gère elle-même le premier plan (ex. aller sur un perso)
        internal int Score;
    }

    /// <summary>
    /// Palette de commandes (Ctrl+K) : tape quelques lettres — « mosa », « kae », « pause », « 30 min »…
    /// ↑ ↓ pour choisir, Entrée pour lancer, Échap pour fermer.
    /// </summary>
    public sealed class CommandPalette : Form
    {
        static CommandPalette current;
        readonly App app;
        readonly TextBox box = new TextBox();
        readonly List<Cmd> all;
        List<Cmd> shown = new List<Cmd>();
        int sel, scroll, hover = -1;
        readonly IntPtr previous;

        public static void Open(App app)
        {
            if (current != null && !current.IsDisposed) { current.Activate(); return; }
            current = new CommandPalette(app);
            current.Show();
            current.Activate();
            Native.SetForegroundWindow(current.Handle);
            current.box.Focus();
        }

        Point openCursor;
        bool mouseMoved;
        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, string l);

        int RowH { get { return Theme.S(44); } }
        int Visible8 { get { return 8; } }
        int HeadH { get { return Theme.S(58); } }

        CommandPalette(App app)
        {
            this.app = app;
            previous = Native.GetForegroundWindow();
            openCursor = Cursor.Position;
            all = app.BuildCommands();
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            KeyPreview = true;
            BackColor = Theme.Panel;
            DoubleBuffered = true;
            Font = Theme.Normal;
            int w = Theme.S(600), h = HeadH + Visible8 * RowH + Theme.S(40);
            Screen scr = app.Main.Visible ? Screen.FromControl(app.Main) : Screen.FromPoint(Cursor.Position);
            Rectangle wa = scr.WorkingArea;
            Bounds = new Rectangle(wa.X + (wa.Width - w) / 2, wa.Y + Math.Max(Theme.S(60), wa.Height / 6), w, h);

            box.BorderStyle = BorderStyle.None;
            box.BackColor = Theme.Panel;
            box.ForeColor = Theme.Text;
            box.Font = Theme.F(13f, FontStyle.Regular);
            box.SetBounds(Theme.S(54), Theme.S(17), w - Theme.S(80), Theme.S(28));
            box.TextChanged += delegate { Filter(); };
            Controls.Add(box);
            box.HandleCreated += delegate
            {
                try { SendMessage(box.Handle, 0x1501 /*EM_SETCUEBANNER*/, (IntPtr)1, L.T("Aller sur un perso, lancer une action, « 30 min »…")); } catch { }
            };
            Filter();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ClassStyle |= 0x20000; // ombre
                cp.ExStyle |= Native.WS_EX_TOOLWINDOW;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Chrome.Round(Handle);
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            Close();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (current == this) current = null;
            base.OnFormClosed(e);
        }

        // ---------------- recherche ----------------

        public static string Norm(string s)
        {
            string n = (s ?? "").ToLowerInvariant().Normalize(NormalizationForm.FormD);
            StringBuilder sb = new StringBuilder();
            foreach (char c in n) if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
            return sb.ToString();
        }

        /// <summary>Score : début de mot &gt; contenu &gt; lettres dans l'ordre. -1 = pas trouvé.</summary>
        static int Score(string query, Cmd c)
        {
            string t = Norm(c.Title), s = Norm(c.Sub + " " + c.Group);
            int best = -1;
            foreach (string word in query.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int sc;
                if (t.StartsWith(word)) sc = 100;
                else if (t.Contains(" " + word) || t.Contains("'" + word) || t.Contains(": " + word)) sc = 80;
                else if (t.Contains(word)) sc = 60;
                else if (s.Contains(word)) sc = 35;
                else if (Subseq(word, t)) sc = 20;
                else return -1;
                best = best < 0 ? sc : best + sc;
            }
            return best < 0 ? 1 : best;
        }

        static bool Subseq(string q, string t)
        {
            int i = 0;
            foreach (char c in t) { if (i < q.Length && c == q[i]) i++; }
            return i == q.Length;
        }

        void Filter()
        {
            string q = Norm(box.Text.Trim());
            List<Cmd> list = new List<Cmd>();

            // commande dynamique : « 30 min », « 2h », « 1 h 30 »
            Match m = Regex.Match(q, @"^(?:(\d{1,3})\s*h(?:\s*(\d{1,2}))?|(\d{1,4})\s*(?:min|mn|m)?)$");
            if (q.Length > 0 && m.Success)
            {
                int minutes = m.Groups[1].Success ? int.Parse(m.Groups[1].Value) * 60 + (m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : 0) : int.Parse(m.Groups[3].Value);
                if (minutes > 0 && minutes <= 100000)
                {
                    int mm = minutes;
                    Cmd c = new Cmd();
                    c.Title = L.T("Lancer un minuteur de ") + Fmt.Duration(mm * 60);
                    c.Sub = L.T("Notification, son et alerte téléphone à la fin");
                    c.Group = L.T("Minuteur");
                    c.Icon = 2;
                    c.Run = delegate { app.QuickTimer(mm); };
                    c.Score = 1000;
                    list.Add(c);
                }
            }

            foreach (Cmd c in all)
            {
                int sc = q.Length == 0 ? 1 : Score(q, c);
                if (sc < 0) continue;
                c.Score = sc;
                list.Add(c);
            }
            if (q.Length > 0)
            {
                // tri stable par score
                List<Cmd> sorted = new List<Cmd>(list);
                for (int i = 0; i < sorted.Count; i++) sorted[i].Score = sorted[i].Score * 1000 - i;
                sorted.Sort(delegate (Cmd a, Cmd b) { return b.Score.CompareTo(a.Score); });
                list = sorted;
            }
            shown = list;
            sel = 0; scroll = 0;
            Invalidate();
        }

        // ---------------- clavier & souris ----------------

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.Escape: Close(); return true;
                case Keys.Down: MoveSel(1); return true;
                case Keys.Up: MoveSel(-1); return true;
                case Keys.PageDown: MoveSel(Visible8); return true;
                case Keys.PageUp: MoveSel(-Visible8); return true;
                case Keys.Enter: RunSelected(); return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        void MoveSel(int d)
        {
            if (shown.Count == 0) return;
            sel = Math.Max(0, Math.Min(shown.Count - 1, sel + d));
            if (sel < scroll) scroll = sel;
            if (sel >= scroll + Visible8) scroll = sel - Visible8 + 1;
            Invalidate();
        }

        void RunSelected()
        {
            if (sel < 0 || sel >= shown.Count) return;
            Cmd c = shown[sel];
            IntPtr back = previous;
            Close();
            try { c.Run(); }
            catch (Exception ex) { Program.Log("Palette « " + c.Title + " » : " + ex.Message); }
            // rendre la main à la fenêtre d'avant si l'action ne gère pas le premier plan
            if (!c.KeepFocus && back != IntPtr.Zero && Native.IsWindow(back) && !app.Main.Visible)
                Switcher.Activate(back);
        }

        Rectangle RowRect(int visibleIndex)
        {
            return new Rectangle(Theme.S(10), HeadH + Theme.S(6) + visibleIndex * RowH, Width - Theme.S(20), RowH - Theme.S(2));
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            // la palette s'ouvre sous la souris : on ignore le survol tant que la souris n'a pas bougé
            if (!mouseMoved) { if (Cursor.Position == openCursor) return; mouseMoved = true; }
            int h = -1;
            for (int i = 0; i < Visible8 && scroll + i < shown.Count; i++) if (RowRect(i).Contains(e.Location)) h = scroll + i;
            if (h != hover) { hover = h; if (h >= 0) sel = h; Invalidate(); }
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (hover >= 0) { sel = hover; RunSelected(); }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            scroll = Math.Max(0, Math.Min(Math.Max(0, shown.Count - Visible8), scroll - Math.Sign(e.Delta) * 2));
            Invalidate();
        }

        // ---------------- dessin ----------------

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Hq(g);
            g.Clear(Theme.Panel);
            using (Pen p = new Pen(Color.FromArgb(160, Theme.Accent))) g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);

            // champ de recherche
            float ls = Theme.S(22);
            using (Pen lp = new Pen(Theme.AccentSoft, Theme.S(2)))
            {
                g.DrawEllipse(lp, Theme.S(20), Theme.S(19), ls * 0.62f, ls * 0.62f);
                g.DrawLine(lp, Theme.S(20) + ls * 0.55f, Theme.S(19) + ls * 0.55f, Theme.S(20) + ls * 0.85f, Theme.S(19) + ls * 0.85f);
            }
            using (Pen sep = new Pen(Theme.Border)) g.DrawLine(sep, 0, HeadH, Width, HeadH);

            if (shown.Count == 0)
            {
                Theme.DrawText(g, L.T("Rien ne correspond."), Theme.Normal, Theme.Muted, new Rectangle(0, HeadH, Width, Theme.S(80)),
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
            for (int i = 0; i < Visible8 && scroll + i < shown.Count; i++)
            {
                Cmd c = shown[scroll + i];
                Rectangle r = RowRect(i);
                bool s = scroll + i == sel;
                if (s)
                {
                    using (LinearGradientBrush lb = new LinearGradientBrush(r, Color.FromArgb(80, Theme.Accent), Color.FromArgb(14, Theme.Accent), 0f))
                    using (GraphicsPath gp = Theme.Round(r, Theme.S(8)))
                        g.FillPath(lb, gp);
                    Theme.FillRound(g, Theme.Accent, new RectangleF(r.X, r.Y + Theme.S(10), Theme.S(3), r.Height - Theme.S(20)), Theme.S(2));
                }
                float isz = Theme.S(26);
                RectangleF ir = new RectangleF(r.X + Theme.S(12), r.Y + (r.Height - isz) / 2f, isz, isz);
                if (c.Avatar != null) Theme.Avatar(g, ir, c.Avatar, c.AvatarClass ?? "", false);
                else Icons.Draw(g, c.Icon, new RectangleF(ir.X + 3, ir.Y + 3, isz - 6, isz - 6), s ? Theme.Accent : Theme.Muted);
                int tx = r.X + Theme.S(50);
                Size gsz = TextRenderer.MeasureText(c.Group ?? "", Theme.Small);
                Theme.DrawText(g, c.Title, Theme.Bold, Theme.Text, new Rectangle(tx, r.Y + Theme.S(4), r.Width - tx - gsz.Width - Theme.S(20), Theme.S(20)),
                    TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
                Theme.DrawText(g, c.Sub ?? "", Theme.Small, Theme.Muted, new Rectangle(tx, r.Y + Theme.S(22), r.Width - tx - Theme.S(10), Theme.S(17)),
                    TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
                Theme.DrawText(g, c.Group ?? "", Theme.Small, s ? Theme.Accent : Theme.Faint, new Rectangle(r.Right - gsz.Width - Theme.S(12), r.Y, gsz.Width + Theme.S(4), r.Height),
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
            }
            Theme.DrawText(g, L.T("↑ ↓ choisir   ·   Entrée lancer   ·   Échap fermer   ·   ") + shown.Count + L.T(" résultat(s)"), Theme.Small, Theme.Faint,
                new Rectangle(0, Height - Theme.S(32), Width, Theme.S(28)), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }
}

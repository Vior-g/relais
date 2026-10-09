using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Relais
{
    /// <summary>Habillage des fenêtres : coins arrondis (Windows 11), barre de titre sombre/colorée pour les dialogues.</summary>
    public static class Chrome
    {
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("user32.dll")] public static extern bool ReleaseCapture();
        [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);

        static int ColorRef(Color c) { return c.R | (c.G << 8) | (c.B << 16); }

        static void Set(IntPtr h, int attr, int v)
        {
            try { DwmSetWindowAttribute(h, attr, ref v, 4); } catch { }
        }

        public static void Round(IntPtr h) { Set(h, 33, 2); } // DWMWA_WINDOW_CORNER_PREFERENCE = ROUND

        /// <summary>Barre de titre Windows aux couleurs de Relais (dialogues).</summary>
        public static void Style(IntPtr h)
        {
            Set(h, 20, Theme.IsLight ? 0 : 1); // mode sombre (Windows 10 20H1+)
            Set(h, 19, Theme.IsLight ? 0 : 1); // anciennes versions
            Set(h, 35, ColorRef(Theme.Sidebar)); // couleur de la barre (Windows 11)
            Set(h, 36, ColorRef(Theme.Text));
            Set(h, 34, ColorRef(Theme.Border));
        }

        /// <summary>À appeler dans le constructeur d'un dialogue.</summary>
        public static void Hook(Form f)
        {
            f.HandleCreated += delegate { Style(f.Handle); };
            if (f.IsHandleCreated) Style(f.Handle);
        }
    }

    /// <summary>Icônes vectorielles de la navigation (dessinées, pas d'images).</summary>
    public static class Icons
    {
        public static void Draw(Graphics g, int kind, RectangleF r, Color c)
        {
            float u = r.Width / 20f, x = r.X, y = r.Y;
            using (Pen p = new Pen(c, Math.Max(1.4f, 1.6f * u)))
            using (SolidBrush b = new SolidBrush(c))
            {
                p.StartCap = LineCap.Round; p.EndCap = LineCap.Round; p.LineJoin = LineJoin.Round;
                switch (kind)
                {
                    case 0: // Team : trois têtes
                        g.DrawEllipse(p, x + 7 * u, y + 3 * u, 6 * u, 6 * u);
                        g.DrawArc(p, x + 4 * u, y + 11 * u, 12 * u, 10 * u, 200, 140);
                        g.DrawEllipse(p, x + 1 * u, y + 6 * u, 4 * u, 4 * u);
                        g.DrawEllipse(p, x + 15 * u, y + 6 * u, 4 * u, 4 * u);
                        break;
                    case 1: // Raccourcis : clavier
                        using (GraphicsPath kp = Theme.Round(new RectangleF(x + 1.5f * u, y + 5 * u, 17 * u, 11 * u), 2 * u)) g.DrawPath(p, kp);
                        for (int i = 0; i < 4; i++) g.FillRectangle(b, x + (4 + i * 3.6f) * u, y + 8 * u, 1.6f * u, 1.6f * u);
                        g.DrawLine(p, x + 6 * u, y + 12.6f * u, x + 14 * u, y + 12.6f * u);
                        break;
                    case 2: // Minuteurs : sablier
                        g.DrawLine(p, x + 5 * u, y + 2.5f * u, x + 15 * u, y + 2.5f * u);
                        g.DrawLine(p, x + 5 * u, y + 17.5f * u, x + 15 * u, y + 17.5f * u);
                        g.DrawLines(p, new PointF[] { new PointF(x + 6 * u, y + 2.5f * u), new PointF(x + 10 * u, y + 10 * u), new PointF(x + 6 * u, y + 17.5f * u) });
                        g.DrawLines(p, new PointF[] { new PointF(x + 14 * u, y + 2.5f * u), new PointF(x + 10 * u, y + 10 * u), new PointF(x + 14 * u, y + 17.5f * u) });
                        g.FillPolygon(b, new PointF[] { new PointF(x + 7.5f * u, y + 16.5f * u), new PointF(x + 10 * u, y + 13 * u), new PointF(x + 12.5f * u, y + 16.5f * u) });
                        break;
                    case 3: // Craft : marteau
                        g.DrawLine(p, x + 5 * u, y + 17 * u, x + 12 * u, y + 10 * u);
                        using (GraphicsPath hp = new GraphicsPath())
                        {
                            hp.AddPolygon(new PointF[] { new PointF(x + 9 * u, y + 6 * u), new PointF(x + 13 * u, y + 2 * u), new PointF(x + 18 * u, y + 7 * u), new PointF(x + 14 * u, y + 11 * u) });
                            g.FillPath(b, hp);
                        }
                        break;
                    case 4: // Stats : barres
                        g.FillRectangle(b, x + 3 * u, y + 11 * u, 3 * u, 6 * u);
                        g.FillRectangle(b, x + 8.5f * u, y + 6 * u, 3 * u, 11 * u);
                        g.FillRectangle(b, x + 14 * u, y + 3 * u, 3 * u, 14 * u);
                        g.DrawLine(p, x + 2 * u, y + 18.5f * u, x + 18 * u, y + 18.5f * u);
                        break;
                    case 6: // Accueil : maison / tour de garde
                        g.DrawLines(p, new PointF[] { new PointF(x + 2.5f * u, y + 9.5f * u), new PointF(x + 10 * u, y + 3 * u), new PointF(x + 17.5f * u, y + 9.5f * u) });
                        g.DrawLines(p, new PointF[] { new PointF(x + 4.5f * u, y + 8 * u), new PointF(x + 4.5f * u, y + 17 * u), new PointF(x + 15.5f * u, y + 17 * u), new PointF(x + 15.5f * u, y + 8 * u) });
                        g.DrawLines(p, new PointF[] { new PointF(x + 8.5f * u, y + 17 * u), new PointF(x + 8.5f * u, y + 12 * u), new PointF(x + 11.5f * u, y + 12 * u), new PointF(x + 11.5f * u, y + 17 * u) });
                        break;
                    case 7: // Action : éclair
                        g.FillPolygon(b, new PointF[] { new PointF(x + 11.5f * u, y + 1.5f * u), new PointF(x + 4.5f * u, y + 11 * u), new PointF(x + 9.5f * u, y + 11 * u),
                            new PointF(x + 8 * u, y + 18.5f * u), new PointF(x + 15.5f * u, y + 8.5f * u), new PointF(x + 10.5f * u, y + 8.5f * u) });
                        break;
                    default: // Options : engrenage
                        float cx = x + 10 * u, cy = y + 10 * u;
                        for (int i = 0; i < 8; i++)
                        {
                            double a = i * Math.PI / 4;
                            g.DrawLine(p, cx + (float)Math.Cos(a) * 5.5f * u, cy + (float)Math.Sin(a) * 5.5f * u, cx + (float)Math.Cos(a) * 8.5f * u, cy + (float)Math.Sin(a) * 8.5f * u);
                        }
                        g.DrawEllipse(p, cx - 5.5f * u, cy - 5.5f * u, 11 * u, 11 * u);
                        g.DrawEllipse(p, cx - 2 * u, cy - 2 * u, 4 * u, 4 * u);
                        break;
                }
            }
        }
    }

    /// <summary>Navigation latérale à icônes, avec indicateur animé.</summary>
    public sealed class SideNav : Control
    {
        public readonly List<string> Tabs = new List<string>();
        public readonly List<int> Kinds = new List<int>();
        public string Footer = "";
        int selected, hover = -1;
        float indicatorY = -1, targetY;
        readonly Timer anim = new Timer();
        public event EventHandler SelectedChanged;

        public SideNav()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Width = Theme.S(176);
            Cursor = Cursors.Hand;
            anim.Interval = 15;
            anim.Tick += delegate
            {
                float d = targetY - indicatorY;
                if (Math.Abs(d) < 0.6f) { indicatorY = targetY; anim.Stop(); }
                else indicatorY += d * 0.28f;
                Invalidate();
            };
        }

        int ItemH { get { return Theme.S(44); } }
        int Top0 { get { return Theme.S(14); } }

        Rectangle ItemRect(int i) { return new Rectangle(Theme.S(10), Top0 + i * (ItemH + Theme.S(4)), Width - Theme.S(20), ItemH); }

        public int Selected
        {
            get { return selected; }
            set
            {
                if (value < 0 || value >= Tabs.Count) value = 0;
                if (selected == value && indicatorY >= 0) return;
                selected = value;
                targetY = ItemRect(value).Y;
                if (indicatorY < 0) indicatorY = targetY; else anim.Start();
                Invalidate();
                if (SelectedChanged != null) SelectedChanged(this, EventArgs.Empty);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = -1;
            for (int i = 0; i < Tabs.Count; i++) if (ItemRect(i).Contains(e.Location)) h = i;
            if (h != hover) { hover = h; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            for (int i = 0; i < Tabs.Count; i++) if (ItemRect(i).Contains(e.Location)) Selected = i;
            base.OnMouseDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Hq(g);
            g.Clear(Theme.Sidebar);
            // filet à droite
            using (Pen p = new Pen(Theme.Border)) g.DrawLine(p, Width - 1, 0, Width - 1, Height);

            // indicateur animé
            if (indicatorY >= 0)
            {
                Rectangle sel = ItemRect(selected);
                RectangleF ir = new RectangleF(sel.X, indicatorY, sel.Width, sel.Height);
                using (LinearGradientBrush lb = new LinearGradientBrush(ir, Color.FromArgb(70, Theme.Accent), Color.FromArgb(8, Theme.Accent), 0f))
                using (GraphicsPath gp = Theme.Round(ir, Theme.S(9)))
                    g.FillPath(lb, gp);
                Theme.FillRound(g, Theme.Accent, new RectangleF(ir.X, ir.Y + Theme.S(10), Theme.S(3), ir.Height - Theme.S(20)), Theme.S(2));
            }

            for (int i = 0; i < Tabs.Count; i++)
            {
                Rectangle r = ItemRect(i);
                bool sel = i == selected;
                if (i == hover && !sel) Theme.FillRound(g, Color.FromArgb(Theme.IsLight ? 60 : 40, Theme.Hover), r, Theme.S(9));
                Color c = sel ? Theme.Accent : (i == hover ? Theme.Text : Theme.Muted);
                float isz = Theme.S(20);
                Icons.Draw(g, i < Kinds.Count ? Kinds[i] : i, new RectangleF(r.X + Theme.S(14), r.Y + (r.Height - isz) / 2f, isz, isz), c);
                Theme.DrawText(g, Tabs[i], Theme.Nav, sel ? Theme.Text : c,
                    new Rectangle(r.X + Theme.S(46), r.Y, r.Width - Theme.S(50), r.Height), TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }

            if (!string.IsNullOrEmpty(Footer))
            {
                Rectangle fr = new Rectangle(Theme.S(14), Height - Theme.S(70), Width - Theme.S(28), Theme.S(60));
                Brand.Diamond(g, Theme.Accent, fr.X + Theme.S(3), fr.Y + Theme.S(9), Theme.S(3));
                Theme.DrawText(g, Footer, Theme.Small, Theme.Faint, new Rectangle(fr.X + Theme.S(12), fr.Y, fr.Width - Theme.S(12), fr.Height),
                    TextFormatFlags.Left | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);
            }
        }
    }

    /// <summary>Barre de titre sur-mesure : blason, nom, actions, boutons de fenêtre.</summary>
    public sealed class TitleBar : Control
    {
        readonly Form form;
        int hoverBtn = -1;
        public readonly List<Control> Actions = new List<Control>();

        public TitleBar(Form form)
        {
            this.form = form;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Height = Theme.S(48);
            Dock = DockStyle.Top;
        }

        public void AddAction(Control c)
        {
            Actions.Add(c);
            Controls.Add(c);
            LayoutActions();
        }

        int BtnW { get { return Theme.S(42); } }

        Rectangle Btn(int i) { return new Rectangle(Width - (3 - i) * BtnW, 0, BtnW, Height - 1); }

        void LayoutActions()
        {
            int x = Width - 3 * BtnW - Theme.S(10);
            for (int i = Actions.Count - 1; i >= 0; i--)
            {
                int w = Actions[i].Width;
                x -= w;
                Actions[i].SetBounds(x, (Height - Theme.S(30)) / 2, w, Theme.S(30));
                x -= Theme.S(6);
            }
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); LayoutActions(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Hq(g);
            using (LinearGradientBrush lb = new LinearGradientBrush(ClientRectangle, Theme.Sidebar, Theme.Bg, 0f)) g.FillRectangle(lb, ClientRectangle);
            using (Pen p = new Pen(Theme.Border)) g.DrawLine(p, 0, Height - 1, Width, Height - 1);
            float s = Theme.S(28);
            Brand.DrawShield(g, new RectangleF(Theme.S(14), (Height - s) / 2f, s, s));
            int tx = Theme.S(50);
            Size ts = TextRenderer.MeasureText("Relais", Theme.Title);
            TextRenderer.DrawText(g, "Relais", Theme.Title, new Point(tx, (Height - ts.Height) / 2), Theme.Text, TextFormatFlags.NoPrefix);
            string sub = "v" + typeof(TitleBar).Assembly.GetName().Version.ToString(3);
            TextRenderer.DrawText(g, sub, Theme.Small, new Point(tx + ts.Width + Theme.S(2), (Height - ts.Height) / 2 + Theme.S(9)), Theme.Faint, TextFormatFlags.NoPrefix);

            // boutons de fenêtre
            for (int i = 0; i < 3; i++)
            {
                Rectangle r = Btn(i);
                if (i == hoverBtn)
                    using (SolidBrush b = new SolidBrush(i == 2 ? Color.FromArgb(196, 60, 45) : Theme.Hover)) g.FillRectangle(b, r);
                Color c = i == hoverBtn && i == 2 ? Color.White : Theme.Muted;
                float cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f, k = Theme.S(5);
                using (Pen p = new Pen(c, 1.2f))
                {
                    if (i == 0) g.DrawLine(p, cx - k, cy, cx + k, cy);
                    else if (i == 1)
                    {
                        if (form.WindowState == FormWindowState.Maximized)
                        {
                            g.DrawRectangle(p, cx - k + 2, cy - k, 2 * k - 2, 2 * k - 2);
                            g.DrawRectangle(p, cx - k, cy - k + 2, 2 * k - 2, 2 * k - 2);
                        }
                        else g.DrawRectangle(p, cx - k, cy - k, 2 * k, 2 * k);
                    }
                    else { g.DrawLine(p, cx - k, cy - k, cx + k, cy + k); g.DrawLine(p, cx + k, cy - k, cx - k, cy + k); }
                }
            }
        }

        int BtnAt(Point p) { for (int i = 0; i < 3; i++) if (Btn(i).Contains(p)) return i; return -1; }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int h = BtnAt(e.Location);
            if (h != hoverBtn) { hoverBtn = h; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e) { hoverBtn = -1; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left || BtnAt(e.Location) >= 0) return;
            if (e.Clicks == 2) { ToggleMax(); return; }
            try
            {
                Chrome.ReleaseCapture();
                Chrome.SendMessage(form.Handle, 0xA1 /*WM_NCLBUTTONDOWN*/, (IntPtr)2 /*HTCAPTION*/, IntPtr.Zero);
            }
            catch { }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            int b = BtnAt(e.Location);
            if (e.Button != MouseButtons.Left || b < 0) return;
            if (b == 0) form.WindowState = FormWindowState.Minimized;
            else if (b == 1) ToggleMax();
            else form.Close();
        }

        public Action OnToggleMax;

        void ToggleMax()
        {
            if (OnToggleMax != null) OnToggleMax();
            Invalidate();
        }
    }
}

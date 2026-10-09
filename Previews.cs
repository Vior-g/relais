using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Relais
{
    /// <summary>
    /// Miniatures en direct des fenêtres (API DWM de Windows, comme l'aperçu de la barre des tâches).
    /// Windows dessine lui-même la miniature : coût quasi nul, aucune capture d'écran.
    /// </summary>
    internal static class Dwm
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct PSIZE { public int x, y; }

        [StructLayout(LayoutKind.Sequential)]
        internal struct THUMB_PROPS
        {
            public int dwFlags;
            public Native.RECT rcDestination;
            public Native.RECT rcSource;
            public byte opacity;
            [MarshalAs(UnmanagedType.Bool)] public bool fVisible;
            [MarshalAs(UnmanagedType.Bool)] public bool fSourceClientAreaOnly;
        }

        const int DWM_TNP_RECTDESTINATION = 0x1, DWM_TNP_OPACITY = 0x4, DWM_TNP_VISIBLE = 0x8, DWM_TNP_SOURCECLIENTAREAONLY = 0x10;

        [DllImport("dwmapi.dll")] static extern int DwmRegisterThumbnail(IntPtr dest, IntPtr src, out IntPtr thumb);
        [DllImport("dwmapi.dll")] static extern int DwmUnregisterThumbnail(IntPtr thumb);
        [DllImport("dwmapi.dll")] static extern int DwmUpdateThumbnailProperties(IntPtr thumb, ref THUMB_PROPS props);
        [DllImport("dwmapi.dll")] static extern int DwmQueryThumbnailSourceSize(IntPtr thumb, out PSIZE size);

        public static IntPtr Register(IntPtr dest, IntPtr src)
        {
            try
            {
                IntPtr t;
                return DwmRegisterThumbnail(dest, src, out t) == 0 ? t : IntPtr.Zero;
            }
            catch { return IntPtr.Zero; }
        }

        public static void Unregister(IntPtr thumb)
        {
            if (thumb == IntPtr.Zero) return;
            try { DwmUnregisterThumbnail(thumb); } catch { }
        }

        public static Size SourceSize(IntPtr thumb)
        {
            try
            {
                PSIZE s;
                if (DwmQueryThumbnailSourceSize(thumb, out s) == 0) return new Size(s.x, s.y);
            }
            catch { }
            return Size.Empty;
        }

        /// <summary>Place la miniature dans le rectangle donné en gardant les proportions.</summary>
        public static void Place(IntPtr thumb, Rectangle box)
        {
            if (thumb == IntPtr.Zero) return;
            Size src = SourceSize(thumb);
            Rectangle r = box;
            if (src.Width > 0 && src.Height > 0)
            {
                double k = Math.Min(box.Width / (double)src.Width, box.Height / (double)src.Height);
                int w = (int)(src.Width * k), h = (int)(src.Height * k);
                r = new Rectangle(box.X + (box.Width - w) / 2, box.Y + (box.Height - h) / 2, w, h);
            }
            THUMB_PROPS p = new THUMB_PROPS();
            p.dwFlags = DWM_TNP_RECTDESTINATION | DWM_TNP_VISIBLE | DWM_TNP_OPACITY | DWM_TNP_SOURCECLIENTAREAONLY;
            p.rcDestination.Left = r.Left; p.rcDestination.Top = r.Top; p.rcDestination.Right = r.Right; p.rcDestination.Bottom = r.Bottom;
            p.opacity = 255;
            p.fVisible = true;
            p.fSourceClientAreaOnly = true;
            try { DwmUpdateThumbnailProperties(thumb, ref p); } catch { }
        }

        public static void Hide(IntPtr thumb)
        {
            if (thumb == IntPtr.Zero) return;
            THUMB_PROPS p = new THUMB_PROPS();
            p.dwFlags = DWM_TNP_VISIBLE;
            p.fVisible = false;
            try { DwmUpdateThumbnailProperties(thumb, ref p); } catch { }
        }
    }

    /// <summary>
    /// « Mur » d'aperçus : toutes les fenêtres de la team en direct. Clic = aller sur le perso.
    /// Idéal sur un 2e écran.
    /// </summary>
    public sealed class PreviewForm : Form
    {
        readonly App app;
        readonly Dictionary<IntPtr, IntPtr> thumbs = new Dictionary<IntPtr, IntPtr>(); // fenêtre jeu -> miniature
        List<GameWindow> items = new List<GameWindow>();
        int hover = -1;
        readonly Timer pulse = new Timer();
        int pulsePhase;

        public PreviewForm(App app)
        {
            this.app = app;
            Text = L.T("Relais — aperçus en direct");
            Chrome.Hook(this);
            Icon = app.AppIcon;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.Normal;
            DoubleBuffered = true;
            StartPosition = FormStartPosition.Manual;
            MinimumSize = new Size(Theme.S(360), Theme.S(240));
            Screen chosen = App.ScreenByName(app.S.PreviewScreen);
            if (chosen != null)
            {
                // écran dédié choisi dans les options : les aperçus le remplissent
                Bounds = chosen.WorkingArea;
            }
            else if (app.S.PreviewX >= 0 && app.S.PreviewW > 200)
                Bounds = new Rectangle(app.S.PreviewX, app.S.PreviewY, app.S.PreviewW, app.S.PreviewH);
            else
            {
                // 2e écran s'il existe, sinon centré
                Screen target = Screen.PrimaryScreen;
                foreach (Screen s in Screen.AllScreens) if (!s.Primary) { target = s; break; }
                Rectangle wa = target.WorkingArea;
                Size = new Size(Math.Min(wa.Width - 80, Theme.S(1100)), Math.Min(wa.Height - 80, Theme.S(680)));
                Location = new Point(wa.X + (wa.Width - Width) / 2, wa.Y + (wa.Height - Height) / 2);
            }
            app.StateChanged += OnState;
            pulse.Interval = 120;
            pulse.Tick += delegate { pulsePhase++; if (app.FlashingCount > 0) Invalidate(); };
            pulse.Start();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x02000000; // WS_EX_COMPOSITED : pas de scintillement
                return cp;
            }
        }

        void OnState(object s, EventArgs e) { if (Visible) Sync(); }

        protected override void OnShown(EventArgs e) { base.OnShown(e); Sync(); }

        protected override void OnResize(EventArgs e) { base.OnResize(e); Layout2(); Invalidate(); }

        /// <summary>Crée/supprime les miniatures selon les fenêtres présentes.</summary>
        void Sync()
        {
            items = app.Rotation();
            HashSet<IntPtr> alive = new HashSet<IntPtr>();
            foreach (GameWindow w in items)
            {
                alive.Add(w.Handle);
                if (!thumbs.ContainsKey(w.Handle)) thumbs[w.Handle] = Dwm.Register(Handle, w.Handle);
            }
            List<IntPtr> dead = new List<IntPtr>();
            foreach (IntPtr h in thumbs.Keys) if (!alive.Contains(h)) dead.Add(h);
            foreach (IntPtr h in dead) { Dwm.Unregister(thumbs[h]); thumbs.Remove(h); }
            Layout2();
            Invalidate();
        }

        Rectangle Cell(int i, out Rectangle thumbBox)
        {
            int n = Math.Max(1, items.Count);
            int cols = (int)Math.Ceiling(Math.Sqrt(n));
            if (ClientSize.Width > ClientSize.Height * 2 && n <= 4) cols = n;
            int rows = (int)Math.Ceiling(n / (double)cols);
            int pad = Theme.S(10);
            int cw = (ClientSize.Width - pad) / cols - pad, ch = (ClientSize.Height - pad) / rows - pad;
            Rectangle cell = new Rectangle(pad + (i % cols) * (cw + pad), pad + (i / cols) * (ch + pad), cw, ch);
            int head = Theme.S(30);
            thumbBox = new Rectangle(cell.X + Theme.S(4), cell.Y + head, cell.Width - Theme.S(8), cell.Height - head - Theme.S(4));
            return cell;
        }

        void Layout2()
        {
            for (int i = 0; i < items.Count; i++)
            {
                IntPtr t;
                if (!thumbs.TryGetValue(items[i].Handle, out t)) continue;
                Rectangle box;
                Cell(i, out box);
                if (Native.IsIconic(items[i].Handle)) Dwm.Hide(t); else Dwm.Place(t, box);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Hq(g);
            g.Clear(Theme.Bg);
            if (items.Count == 0)
            {
                Theme.DrawText(g, L.T("Aucun perso connecté dans la rotation."), Theme.Normal, Theme.Muted, ClientRectangle,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            }
            IntPtr fg = app.ForegroundHandle;
            for (int i = 0; i < items.Count; i++)
            {
                GameWindow w = items[i];
                Rectangle box;
                Rectangle c = Cell(i, out box);
                bool active = w.Handle == fg || w.Handle == app.LastActive;
                bool flashing = app.IsFlashing(w.Handle);
                Theme.FillRound(g, i == hover ? Theme.Hover : Theme.Panel, c, Theme.S(10));
                Color border = Color.Empty;
                if (flashing) border = (pulsePhase / 3) % 2 == 0 ? Theme.Accent : Theme.AccentDim;
                else if (active) border = Theme.Accent;
                if (border != Color.Empty) Theme.StrokeRound(g, border, Theme.S(2), new RectangleF(c.X + 1, c.Y + 1, c.Width - 2, c.Height - 2), Theme.S(10));
                int av = Theme.S(20);
                Theme.Avatar(g, new RectangleF(c.X + Theme.S(8), c.Y + Theme.S(5), av, av), w.Name, w.Class, false);
                string title = (i + 1) + ". " + w.Name + (w.Class.Length > 0 ? " · " + w.Class : "") + (flashing ? L.T("  — à toi de jouer !") : "");
                Theme.DrawText(g, title, Theme.Bold, flashing ? Theme.Accent : Theme.Text,
                    new Rectangle(c.X + Theme.S(34), c.Y + Theme.S(3), c.Width - Theme.S(40), Theme.S(24)), TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                if (Native.IsIconic(w.Handle))
                    Theme.DrawText(g, L.T("Fenêtre réduite"), Theme.Small, Theme.Faint, box, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }

        int CellAt(Point p)
        {
            for (int i = 0; i < items.Count; i++) { Rectangle b; if (Cell(i, out b).Contains(p)) return i; }
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int h = CellAt(e.Location);
            if (h != hover) { hover = h; Invalidate(); }
            Cursor = h >= 0 ? Cursors.Hand : Cursors.Default;
        }

        protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            int i = CellAt(e.Location);
            if (i >= 0 && e.Button == MouseButtons.Left) app.Activate(items[i].Name);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (WindowState == FormWindowState.Normal)
            {
                app.S.PreviewX = Left; app.S.PreviewY = Top; app.S.PreviewW = Width; app.S.PreviewH = Height;
                app.S.Save();
            }
            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            app.StateChanged -= OnState;
            pulse.Stop();
            foreach (IntPtr t in thumbs.Values) Dwm.Unregister(t);
            thumbs.Clear();
            base.OnFormClosed(e);
        }
    }

    /// <summary>Aperçu flottant affiché au survol d'une vignette de la mini-barre.</summary>
    public sealed class HoverPreview : Form
    {
        IntPtr thumb = IntPtr.Zero, source = IntPtr.Zero;

        public HoverPreview()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Theme.Panel;
            Size = new Size(Theme.S(320), Theme.S(190));
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW | Native.WS_EX_TOPMOST | 0x20;
                return cp;
            }
        }

        public void ShowFor(IntPtr game, Rectangle anchor, bool below)
        {
            if (Native.IsIconic(game)) { HidePreview(); return; }
            if (source != game)
            {
                Dwm.Unregister(thumb);
                source = game;
                thumb = Dwm.Register(Handle, game);
            }
            if (thumb == IntPtr.Zero) { HidePreview(); return; }
            Size src = Dwm.SourceSize(thumb);
            int w = Theme.S(320);
            int h = src.Width > 0 ? Math.Max(Theme.S(120), (int)(w * src.Height / (double)src.Width)) : Theme.S(190);
            Rectangle wa = Screen.FromRectangle(anchor).WorkingArea;
            int x = Math.Max(wa.Left, Math.Min(wa.Right - w, anchor.X + anchor.Width / 2 - w / 2));
            int y = below ? anchor.Bottom + Theme.S(6) : anchor.Top - h - Theme.S(6);
            if (y + h > wa.Bottom) y = anchor.Top - h - Theme.S(6);
            if (y < wa.Top) y = anchor.Bottom + Theme.S(6);
            Bounds = new Rectangle(x, y, w, h);
            Dwm.Place(thumb, new Rectangle(Theme.S(3), Theme.S(3), w - Theme.S(6), h - Theme.S(6)));
            if (!Visible) Show();
        }

        public void HidePreview()
        {
            if (Visible) Hide();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Theme.Accent);
            using (SolidBrush b = new SolidBrush(Theme.Panel)) e.Graphics.FillRectangle(b, 2, 2, Width - 4, Height - 4);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            Dwm.Unregister(thumb);
            base.OnFormClosed(e);
        }
    }
}

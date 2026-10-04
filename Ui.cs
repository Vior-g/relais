using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace Relais
{
    public static class Theme
    {
        // Identité « Aventure » : bois sombre, parchemin et or.
        public static Color Bg = Color.FromArgb(24, 19, 15);
        public static Color Panel = Color.FromArgb(35, 28, 22);
        public static Color Panel2 = Color.FromArgb(46, 37, 29);
        public static Color Hover = Color.FromArgb(57, 46, 36);
        public static Color Border = Color.FromArgb(78, 62, 47);
        public static Color Text = Color.FromArgb(243, 233, 218);
        public static Color Muted = Color.FromArgb(193, 176, 153);
        public static Color Faint = Color.FromArgb(138, 121, 102);
        public static Color Sidebar = Color.FromArgb(19, 15, 12);
        public static Color Accent = Color.FromArgb(227, 165, 60);
        public static Color AccentDim = Color.FromArgb(94, 70, 36);
        public static Color AccentSoft = Color.FromArgb(200, 160, 100);
        public static bool IsLight;
        public static Color OnAccent = Color.FromArgb(30, 26, 20);

        public static Func<string, Color?> ColorOverride;
        public static Func<string, Image> ImageProvider;

        public static readonly Color[] AccentPresets = {
            Color.FromArgb(227, 165, 60), Color.FromArgb(104, 182, 120), Color.FromArgb(88, 152, 214),
            Color.FromArgb(162, 116, 214), Color.FromArgb(214, 110, 142), Color.FromArgb(206, 82, 62)
        };

        /// <summary>Thème sombre (défaut) ou clair. À appeler au démarrage, avant la création des fenêtres.</summary>
        public static void ApplyBase(bool light)
        {
            IsLight = light;
            if (!light) return;
            // parchemin
            Bg = Color.FromArgb(242, 233, 215);
            Panel = Color.FromArgb(251, 246, 236);
            Panel2 = Color.FromArgb(234, 222, 200);
            Hover = Color.FromArgb(226, 211, 185);
            Border = Color.FromArgb(207, 187, 156);
            Text = Color.FromArgb(52, 38, 26);
            Muted = Color.FromArgb(108, 88, 66);
            Faint = Color.FromArgb(150, 130, 104);
            Sidebar = Color.FromArgb(232, 219, 195);
        }

        /// <summary>Applique la couleur d'accent (#RRGGBB) ; recalcule les teintes dérivées.</summary>
        public static void ApplyAccent(string hex)
        {
            Color c = Color.FromArgb(227, 165, 60);
            if (!string.IsNullOrEmpty(hex) && hex.ToUpperInvariant() != "#F2A93B") { try { c = ColorTranslator.FromHtml(hex); } catch { } }
            Accent = c;
            AccentDim = Color.FromArgb((int)(c.R * 0.35 + Bg.R * 0.65), (int)(c.G * 0.35 + Bg.G * 0.65), (int)(c.B * 0.35 + Bg.B * 0.65));
            double lum = 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;
            OnAccent = lum > 140 ? Color.FromArgb(38, 26, 14) : Color.White;
            AccentSoft = Color.FromArgb((c.R + Muted.R) / 2, (c.G + Muted.G) / 2, (c.B + Muted.B) / 2);
            iconCache.Clear();
        }
        public static readonly Color Green = Color.FromArgb(122, 190, 104);
        public static readonly Color Red = Color.FromArgb(218, 88, 64);

        static float scale = -1;
        public static float Scale
        {
            get
            {
                if (scale < 0)
                {
                    using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) scale = g.DpiX / 96f;
                    if (scale < 1) scale = 1;
                }
                return scale;
            }
        }
        public static int S(int v) { return (int)Math.Round(v * Scale); }

        public static Font F(float size, FontStyle st)
        {
            return new Font("Segoe UI", size, st, GraphicsUnit.Point);
        }
        public static readonly Font Normal = F(9.5f, FontStyle.Regular);
        public static readonly Font Bold = F(9.5f, FontStyle.Bold);
        public static readonly Font Small = F(8.25f, FontStyle.Regular);
        public static readonly Font SmallBold = F(8.25f, FontStyle.Bold);
        public static Font Serif(float size, FontStyle st)
        {
            return new Font("Georgia", size, st, GraphicsUnit.Point);
        }
        public static readonly Font Title = Serif(15f, FontStyle.Bold);
        public static readonly Font Section = Serif(8.5f, FontStyle.Bold);
        public static readonly Font Nav = F(9f, FontStyle.Bold);

        static readonly Dictionary<string, Color> classColors = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase)
        {
            { "Feca", Color.FromArgb(214, 176, 92) }, { "Osamodas", Color.FromArgb(125, 96, 214) },
            { "Enutrof", Color.FromArgb(184, 148, 72) }, { "Sram", Color.FromArgb(150, 160, 175) },
            { "Xélor", Color.FromArgb(80, 140, 230) }, { "Xelor", Color.FromArgb(80, 140, 230) },
            { "Ecaflip", Color.FromArgb(235, 190, 60) }, { "Eniripsa", Color.FromArgb(236, 110, 170) },
            { "Iop", Color.FromArgb(230, 80, 60) }, { "Crâ", Color.FromArgb(110, 190, 90) }, { "Cra", Color.FromArgb(110, 190, 90) },
            { "Sadida", Color.FromArgb(70, 170, 110) }, { "Sacrieur", Color.FromArgb(200, 50, 60) },
            { "Pandawa", Color.FromArgb(120, 200, 200) }, { "Roublard", Color.FromArgb(230, 130, 60) },
            { "Zobal", Color.FromArgb(170, 120, 200) }, { "Steamer", Color.FromArgb(60, 170, 190) },
            { "Eliotrope", Color.FromArgb(90, 200, 230) }, { "Huppermage", Color.FromArgb(190, 110, 240) },
            { "Ouginak", Color.FromArgb(160, 110, 80) }, { "Forgelance", Color.FromArgb(200, 170, 120) }
        };

        public static Color ForCharacter(string name, string cls)
        {
            if (ColorOverride != null)
            {
                Color? o = ColorOverride(name);
                if (o.HasValue) return o.Value;
            }
            Color c;
            if (!string.IsNullOrEmpty(cls) && classColors.TryGetValue(cls, out c)) return c;
            int h = 0;
            foreach (char ch in (name ?? "").ToLowerInvariant()) h = h * 31 + ch;
            float hue = (Math.Abs(h) % 360);
            return FromHsv(hue, 0.55f, 0.85f);
        }

        public static Color FromHsv(float h, float s, float v)
        {
            float c = v * s, x = c * (1 - Math.Abs((h / 60f) % 2 - 1)), m = v - c;
            float r = 0, g = 0, b = 0;
            if (h < 60) { r = c; g = x; } else if (h < 120) { r = x; g = c; } else if (h < 180) { g = c; b = x; }
            else if (h < 240) { g = x; b = c; } else if (h < 300) { r = x; b = c; } else { r = c; b = x; }
            return Color.FromArgb((int)((r + m) * 255), (int)((g + m) * 255), (int)((b + m) * 255));
        }

        public static GraphicsPath Round(RectangleF r, float rad)
        {
            GraphicsPath p = new GraphicsPath();
            float d = rad * 2;
            if (d > r.Height) d = r.Height;
            if (d > r.Width) d = r.Width;
            if (d <= 0) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void FillRound(Graphics g, Color c, RectangleF r, float rad)
        {
            using (GraphicsPath p = Round(r, rad)) using (SolidBrush b = new SolidBrush(c)) g.FillPath(b, p);
        }

        public static void StrokeRound(Graphics g, Color c, float w, RectangleF r, float rad)
        {
            using (GraphicsPath p = Round(r, rad)) using (Pen pen = new Pen(c, w)) g.DrawPath(pen, p);
        }

        public static void Hq(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        }

        public static void DrawText(Graphics g, string s, Font f, Color c, Rectangle r, TextFormatFlags flags)
        {
            TextRenderer.DrawText(g, s, f, r, c, flags | TextFormatFlags.NoPrefix);
        }

        /// <summary>Pastille ronde avec l'initiale du perso.</summary>
        public static void Avatar(Graphics g, RectangleF r, string name, string cls, bool dim)
        {
            Image img = ImageProvider != null ? ImageProvider(name) : null;
            if (img != null)
            {
                GraphicsState st = g.Save();
                using (GraphicsPath clip = new GraphicsPath())
                {
                    clip.AddEllipse(r);
                    g.SetClip(clip);
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    if (dim)
                    {
                        System.Drawing.Imaging.ColorMatrix cm = new System.Drawing.Imaging.ColorMatrix();
                        cm.Matrix33 = 0.3f;
                        using (System.Drawing.Imaging.ImageAttributes ia = new System.Drawing.Imaging.ImageAttributes())
                        {
                            ia.SetColorMatrix(cm);
                            g.DrawImage(img, Rectangle.Round(r), 0, 0, img.Width, img.Height, GraphicsUnit.Pixel, ia);
                        }
                    }
                    else g.DrawImage(img, r);
                }
                g.Restore(st);
                using (Pen p = new Pen(Color.FromArgb(dim ? 60 : 160, ForCharacter(name, cls)), 1.5f)) g.DrawEllipse(p, r);
                return;
            }
            Color c = ForCharacter(name, cls);
            if (dim) c = Color.FromArgb(70, c);
            using (SolidBrush b = new SolidBrush(c)) g.FillEllipse(b, r);
            string init = string.IsNullOrEmpty(name) ? "?" : name.Substring(0, 1).ToUpperInvariant();
            using (Font f = new Font("Segoe UI", r.Height * 0.42f, FontStyle.Bold, GraphicsUnit.Pixel))
            using (StringFormat sf = new StringFormat())
            using (SolidBrush tb = new SolidBrush(dim ? Color.FromArgb(120, 20, 22, 26) : Color.FromArgb(235, 20, 22, 26)))
            {
                sf.Alignment = StringAlignment.Center;
                sf.LineAlignment = StringAlignment.Center;
                g.DrawString(init, f, tb, r, sf);
            }
        }

        static readonly Dictionary<int, Icon> iconCache = new Dictionary<int, Icon>();
        public static Icon MakeIcon(int size)
        {
            Icon cached;
            if (iconCache.TryGetValue(size, out cached)) return cached;
            cached = BuildIcon(size);
            iconCache[size] = cached;
            return cached;
        }

        static Icon BuildIcon(int size)
        {
            using (Bitmap bmp = new Bitmap(size, size))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    Hq(g);
                    g.Clear(Color.Transparent);
                    Brand.DrawShield(g, new RectangleF(0, 0, size, size));
                }
                // Icône créée une seule fois par taille ; la poignée reste valide pour toute la durée du programme.
                return Icon.FromHandle(bmp.GetHicon());
            }
        }
    }

    /// <summary>Bouton plat arrondi.</summary>
    public class FlatButton : Control
    {
        bool hover, down;
        public bool Primary;
        public bool Danger;

        public FlatButton(string text)
        {
            Text = text;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            Font = Theme.Normal;
            Cursor = Cursors.Hand;
            BackColor = Color.Transparent;
            Height = Theme.S(32);
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Hq(g);
            Color bg = Primary ? Theme.Accent : Theme.Panel2;
            if (!Enabled) bg = Theme.Panel;
            else if (down) bg = Primary ? ControlPaint.Dark(Theme.Accent, 0.05f) : Theme.Border;
            else if (hover) bg = Primary ? ControlPaint.Light(Theme.Accent, 0.25f) : Theme.Hover;
            RectangleF r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            if (Primary && Enabled && Height > 2)
            {
                // or poli : dégradé vertical + liseré clair
                using (GraphicsPath p = Theme.Round(r, Theme.S(7)))
                using (LinearGradientBrush lg = new LinearGradientBrush(new RectangleF(0, 0, Width, Height), ControlPaint.Light(bg, 0.35f), ControlPaint.Dark(bg, 0.08f), 90f))
                    g.FillPath(lg, p);
                Theme.StrokeRound(g, Color.FromArgb(120, ControlPaint.Dark(bg, 0.3f)), 1f, r, Theme.S(7));
                using (Pen hl = new Pen(Color.FromArgb(90, Color.White)))
                    g.DrawLine(hl, Theme.S(7), 1.5f, Width - Theme.S(7), 1.5f);
            }
            else
            {
                Theme.FillRound(g, bg, r, Theme.S(7));
                if (Enabled) Theme.StrokeRound(g, Color.FromArgb(hover ? 160 : 90, Theme.Border), 1f, r, Theme.S(7));
            }
            Color fg = Primary ? Theme.OnAccent : (Danger ? Theme.Red : Theme.Text);
            if (!Enabled) fg = Theme.Faint;
            Theme.DrawText(g, Text, Font, fg, ClientRectangle, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    /// <summary>Interrupteur on/off.</summary>
    public class Toggle : Control
    {
        bool on;
        public event EventHandler CheckedChanged;
        public bool Checked
        {
            get { return on; }
            set { if (on != value) { on = value; Invalidate(); if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty); } }
        }

        public Toggle(string text)
        {
            Text = text;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Font = Theme.Normal;
            Cursor = Cursors.Hand;
            Height = Theme.S(28);
        }

        protected override void OnClick(EventArgs e) { Checked = !Checked; base.OnClick(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Hq(g);
            int w = Theme.S(34), h = Theme.S(18);
            RectangleF tr = new RectangleF(0.5f, (Height - h) / 2f, w, h);
            Theme.FillRound(g, on ? Theme.Accent : Theme.Border, tr, h / 2f);
            float d = h - Theme.S(4);
            float x = on ? tr.Right - d - Theme.S(2) : tr.X + Theme.S(2);
            using (SolidBrush b = new SolidBrush(on ? Theme.OnAccent : Theme.Muted))
                g.FillEllipse(b, x, tr.Y + Theme.S(2), d, d);
            Rectangle txt = new Rectangle(w + Theme.S(10), 0, Width - w - Theme.S(10), Height);
            Theme.DrawText(g, Text, Font, Theme.Text, txt, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    /// <summary>Champ de raccourci : clic = capturer, clic droit = effacer.</summary>
    public class HotkeyBox : Control
    {
        string value;
        bool capturing, hover;
        public event EventHandler ValueChanged;
        public event EventHandler Cancelled;
        public Action<HotkeyBox> BeginCapture; // fourni par l'application

        public HotkeyBox()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Font = Theme.Normal;
            Cursor = Cursors.Hand;
            Height = Theme.S(30);
        }

        public string Value
        {
            get { return value; }
            set { this.value = value; Invalidate(); }
        }

        public bool Capturing
        {
            get { return capturing; }
            set { capturing = value; Invalidate(); }
        }

        public void Cancel()
        {
            if (!capturing) return;
            capturing = false;
            Invalidate();
            if (Cancelled != null) Cancelled(this, EventArgs.Empty);
        }

        public void SetFromCapture(string v)
        {
            capturing = false;
            value = v;
            Invalidate();
            if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Right) { SetFromCapture(null); return; }
            if (e.Button == MouseButtons.Left && BeginCapture != null) { Capturing = true; BeginCapture(this); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Hq(g);
            RectangleF r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            Theme.FillRound(g, capturing ? Theme.AccentDim : (hover ? Theme.Hover : Theme.Panel2), r, Theme.S(6));
            if (capturing) Theme.StrokeRound(g, Theme.Accent, 1.2f, r, Theme.S(6));
            string t;
            Color c;
            if (capturing) { t = "Appuie sur une touche…"; c = Theme.Accent; }
            else if (string.IsNullOrEmpty(value)) { t = "Aucun"; c = Theme.Faint; }
            else { Hotkey hk = Hotkey.Parse(value); t = hk == null ? value : hk.Display(); c = Theme.Text; }
            Theme.DrawText(g, t, capturing ? Theme.Small : Theme.Bold, c, ClientRectangle,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    /// <summary>Petit dialogue de saisie de texte.</summary>
    public static class Prompt
    {
        public static string AskMultiline(IWin32Window owner, string title, string placeholder, string initial)
        {
            using (Form f = new Form())
            {
                f.Text = title;
                Chrome.Hook(f);
                f.FormBorderStyle = FormBorderStyle.Sizable;
                f.MaximizeBox = false; f.MinimizeBox = false; f.ShowInTaskbar = false;
                f.StartPosition = FormStartPosition.CenterParent;
                f.BackColor = Theme.Bg; f.ForeColor = Theme.Text; f.Font = Theme.Normal;
                f.ClientSize = new Size(Theme.S(420), Theme.S(300));
                f.MinimumSize = new Size(Theme.S(300), Theme.S(220));
                TextBox tb = new TextBox();
                tb.Multiline = true; tb.ScrollBars = ScrollBars.Vertical; tb.AcceptsReturn = true;
                tb.Text = (initial ?? "").Replace("\r\n", "\n").Replace("\n", "\r\n");
                tb.BorderStyle = BorderStyle.FixedSingle;
                tb.BackColor = Theme.Panel2; tb.ForeColor = Theme.Text;
                tb.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
                tb.SetBounds(Theme.S(14), Theme.S(14), Theme.S(392), Theme.S(222));
                Label ph = new Label();
                ph.Text = placeholder; ph.ForeColor = Theme.Faint; ph.Font = Theme.Small; ph.AutoSize = false;
                ph.Anchor = AnchorStyles.Left | AnchorStyles.Bottom | AnchorStyles.Right;
                ph.SetBounds(Theme.S(14), Theme.S(250), Theme.S(180), Theme.S(36));
                FlatButton ok = new FlatButton("Enregistrer"); ok.Primary = true;
                ok.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
                ok.SetBounds(Theme.S(306), Theme.S(252), Theme.S(100), Theme.S(32));
                FlatButton cancel = new FlatButton("Annuler");
                cancel.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
                cancel.SetBounds(Theme.S(198), Theme.S(252), Theme.S(100), Theme.S(32));
                ok.Click += delegate { f.DialogResult = DialogResult.OK; };
                cancel.Click += delegate { f.DialogResult = DialogResult.Cancel; };
                f.Controls.Add(tb); f.Controls.Add(ph); f.Controls.Add(ok); f.Controls.Add(cancel);
                f.Shown += delegate { tb.Focus(); tb.SelectionStart = tb.TextLength; };
                if (f.ShowDialog(owner) != DialogResult.OK) return null;
                return tb.Text;
            }
        }

        public static string Ask(IWin32Window owner, string title, string label, string initial)
        {
            using (Form f = new Form())
            {
                f.Text = title;
                Chrome.Hook(f);
                f.FormBorderStyle = FormBorderStyle.FixedDialog;
                f.MaximizeBox = false; f.MinimizeBox = false; f.ShowInTaskbar = false;
                f.StartPosition = FormStartPosition.CenterParent;
                f.BackColor = Theme.Bg; f.ForeColor = Theme.Text; f.Font = Theme.Normal;
                f.ClientSize = new Size(Theme.S(340), Theme.S(130));
                Label l = new Label();
                l.Text = label; l.AutoSize = false; l.ForeColor = Theme.Muted;
                l.SetBounds(Theme.S(16), Theme.S(12), Theme.S(308), Theme.S(20));
                TextBox tb = new TextBox();
                tb.Text = initial ?? ""; tb.BorderStyle = BorderStyle.FixedSingle;
                tb.BackColor = Theme.Panel2; tb.ForeColor = Theme.Text;
                tb.SetBounds(Theme.S(16), Theme.S(36), Theme.S(308), Theme.S(26));
                FlatButton ok = new FlatButton("Valider"); ok.Primary = true;
                ok.SetBounds(Theme.S(224), Theme.S(84), Theme.S(100), Theme.S(32));
                FlatButton cancel = new FlatButton("Annuler");
                cancel.SetBounds(Theme.S(116), Theme.S(84), Theme.S(100), Theme.S(32));
                ok.Click += delegate { f.DialogResult = DialogResult.OK; };
                cancel.Click += delegate { f.DialogResult = DialogResult.Cancel; };
                tb.KeyDown += delegate (object s, KeyEventArgs e)
                {
                    if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; f.DialogResult = DialogResult.OK; }
                    if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; f.DialogResult = DialogResult.Cancel; }
                };
                f.Controls.Add(l); f.Controls.Add(tb); f.Controls.Add(ok); f.Controls.Add(cancel);
                f.Shown += delegate { tb.Focus(); tb.SelectAll(); };
                if (f.ShowDialog(owner) != DialogResult.OK) return null;
                string v = tb.Text.Trim();
                return v.Length == 0 ? null : v;
            }
        }
    }

    /// <summary>Barre d'onglets.</summary>
    public sealed class TabStrip : Control
    {
        public readonly List<string> Tabs = new List<string>();
        int selected, hover = -1;
        public event EventHandler SelectedChanged;

        public TabStrip()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Bg;
            Font = Theme.Bold;
            Height = Theme.S(42);
            Cursor = Cursors.Hand;
        }

        public int Selected
        {
            get { return selected; }
            set
            {
                if (value < 0 || value >= Tabs.Count) value = 0;
                if (selected == value) return;
                selected = value; Invalidate();
                if (SelectedChanged != null) SelectedChanged(this, EventArgs.Empty);
            }
        }

        Rectangle TabRect(int i)
        {
            int x = Theme.S(18);
            for (int k = 0; k < i; k++) x += TextRenderer.MeasureText(Tabs[k], Font).Width + Theme.S(28);
            int w = TextRenderer.MeasureText(Tabs[i], Font).Width + Theme.S(20);
            return new Rectangle(x, 0, w, Height);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = -1;
            for (int i = 0; i < Tabs.Count; i++) if (TabRect(i).Contains(e.Location)) h = i;
            if (h != hover) { hover = h; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            for (int i = 0; i < Tabs.Count; i++) if (TabRect(i).Contains(e.Location)) Selected = i;
            base.OnMouseDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Hq(g);
            using (Pen p = new Pen(Theme.Border)) g.DrawLine(p, Theme.S(18), Height - 1, Width - Theme.S(18), Height - 1);
            for (int i = 0; i < Tabs.Count; i++)
            {
                Rectangle r = TabRect(i);
                bool sel = i == selected;
                Theme.DrawText(g, Tabs[i], Font, sel ? Theme.Text : (i == hover ? Theme.Muted : Theme.Faint), r,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                if (sel)
                    Theme.FillRound(g, Theme.Accent, new RectangleF(r.X + Theme.S(6), Height - Theme.S(3), r.Width - Theme.S(12), Theme.S(3)), Theme.S(1));
            }
        }
    }

    /// <summary>Curseur horizontal (ex. opacité).</summary>
    public sealed class Slider : Control
    {
        public int Minimum = 0, Maximum = 100;
        int value;
        bool drag;
        public string Suffix = "%";
        public event EventHandler ValueChanged;

        public Slider(string text)
        {
            Text = text;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Font = Theme.Normal;
            Cursor = Cursors.Hand;
            Height = Theme.S(28);
        }

        public int Value
        {
            get { return value; }
            set { int v = Math.Max(Minimum, Math.Min(Maximum, value)); if (v != this.value) { this.value = v; Invalidate(); } }
        }

        Rectangle Track
        {
            get
            {
                int lw = Theme.S(150), vw = Theme.S(46);
                return new Rectangle(lw, Height / 2 - Theme.S(2), Math.Max(10, Width - lw - vw), Theme.S(4));
            }
        }

        void SetFromX(int x)
        {
            Rectangle t = Track;
            int v = Minimum + (int)Math.Round((x - t.X) * (Maximum - Minimum) / (double)t.Width);
            int old = value;
            Value = v;
            if (value != old && ValueChanged != null) ValueChanged(this, EventArgs.Empty);
        }

        protected override void OnMouseDown(MouseEventArgs e) { drag = true; SetFromX(e.X); base.OnMouseDown(e); }
        protected override void OnMouseMove(MouseEventArgs e) { if (drag) SetFromX(e.X); base.OnMouseMove(e); }
        protected override void OnMouseUp(MouseEventArgs e) { drag = false; base.OnMouseUp(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Hq(g);
            Theme.DrawText(g, Text, Font, Theme.Text, new Rectangle(0, 0, Theme.S(146), Height), TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            Rectangle t = Track;
            Theme.FillRound(g, Theme.Border, t, t.Height / 2f);
            float f = (value - Minimum) / (float)Math.Max(1, Maximum - Minimum);
            Theme.FillRound(g, Theme.Accent, new RectangleF(t.X, t.Y, t.Width * f, t.Height), t.Height / 2f);
            float d = Theme.S(14);
            using (SolidBrush b = new SolidBrush(Theme.Text)) g.FillEllipse(b, t.X + t.Width * f - d / 2, Height / 2f - d / 2, d, d);
            Theme.DrawText(g, value + Suffix, Theme.SmallBold, Theme.Muted, new Rectangle(t.Right + Theme.S(8), 0, Theme.S(40), Height),
                TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
        }
    }

    /// <summary>Identité visuelle : le blason de Relais.</summary>
    public static class Brand
    {
        public static GraphicsPath ShieldPath(RectangleF r)
        {
            float x = r.X, y = r.Y, w = r.Width, h = r.Height;
            GraphicsPath p = new GraphicsPath();
            PointF tl = new PointF(x + w * 0.10f, y + h * 0.12f), tr = new PointF(x + w * 0.90f, y + h * 0.12f);
            p.AddBezier(tl, new PointF(x + w * 0.30f, y + h * 0.10f), new PointF(x + w * 0.42f, y + h * 0.02f), new PointF(x + w * 0.50f, y + h * 0.02f));
            p.AddBezier(new PointF(x + w * 0.50f, y + h * 0.02f), new PointF(x + w * 0.58f, y + h * 0.02f), new PointF(x + w * 0.70f, y + h * 0.10f), tr);
            p.AddBezier(tr, new PointF(x + w * 0.92f, y + h * 0.45f), new PointF(x + w * 0.82f, y + h * 0.78f), new PointF(x + w * 0.50f, y + h * 0.98f));
            p.AddBezier(new PointF(x + w * 0.50f, y + h * 0.98f), new PointF(x + w * 0.18f, y + h * 0.78f), new PointF(x + w * 0.08f, y + h * 0.45f), tl);
            p.CloseFigure();
            return p;
        }

        /// <summary>Blason : bois sombre, bordure dorée, double chevron (le « relais » entre persos).</summary>
        public static void DrawShield(Graphics g, RectangleF r)
        {
            Color gold = Theme.Accent;
            Color goldHi = ControlPaint.Light(gold, 0.55f), goldLo = ControlPaint.Dark(gold, 0.25f);
            using (GraphicsPath outer = ShieldPath(r))
            {
                using (LinearGradientBrush b = new LinearGradientBrush(r, goldHi, goldLo, 90f)) g.FillPath(b, outer);
            }
            float inset = Math.Max(1.2f, r.Width * 0.085f);
            RectangleF inner = new RectangleF(r.X + inset, r.Y + inset, r.Width - 2 * inset, r.Height - 2 * inset * 0.9f);
            using (GraphicsPath ip = ShieldPath(inner))
            using (LinearGradientBrush wood = new LinearGradientBrush(inner, Color.FromArgb(70, 50, 34), Color.FromArgb(28, 20, 14), 90f))
                g.FillPath(wood, ip);
            float u = r.Width / 16f;
            float t = Math.Max(1.2f, 1.7f * u);
            using (LinearGradientBrush gb = new LinearGradientBrush(r, goldHi, gold, 90f))
            using (Pen p = new Pen(gb, t))
            {
                p.StartCap = LineCap.Round; p.EndCap = LineCap.Round; p.LineJoin = LineJoin.Round;
                float cy = r.Y + r.Height * 0.47f, dy = 2.6f * u;
                g.DrawLines(p, new PointF[] { new PointF(r.X + 4.6f * u, cy - dy), new PointF(r.X + 7.2f * u, cy), new PointF(r.X + 4.6f * u, cy + dy) });
                g.DrawLines(p, new PointF[] { new PointF(r.X + 8.6f * u, cy - dy), new PointF(r.X + 11.2f * u, cy), new PointF(r.X + 8.6f * u, cy + dy) });
            }
        }

        /// <summary>Petit losange décoratif.</summary>
        public static void Diamond(Graphics g, Color c, float cx, float cy, float s)
        {
            using (SolidBrush b = new SolidBrush(c))
                g.FillPolygon(b, new PointF[] { new PointF(cx, cy - s), new PointF(cx + s, cy), new PointF(cx, cy + s), new PointF(cx - s, cy) });
        }
    }

    /// <summary>Titre de section à ornement (texte serif + filet doré terminé par un losange).</summary>
    public sealed class OrnamentTitle : Label
    {
        public OrnamentTitle(string text)
        {
            UseMnemonic = false;
            Text = text;
            AutoSize = false;
            Height = Theme.S(32);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Bg);
            Theme.Hq(g);
            string t = Text.ToUpperInvariant();
            int baseY = Height - Theme.S(8);
            Size ts = TextRenderer.MeasureText(t, Theme.Section);
            Brand.Diamond(g, Theme.Accent, Theme.S(4), baseY - ts.Height / 2f, Theme.S(3));
            TextRenderer.DrawText(g, t, Theme.Section, new Point(Theme.S(12), baseY - ts.Height), Theme.AccentSoft, TextFormatFlags.NoPrefix);
            float x0 = Theme.S(12) + ts.Width + Theme.S(4), x1 = Width - Theme.S(6), y = baseY - ts.Height / 2f;
            if (x1 - x0 > 20)
            {
                using (LinearGradientBrush lb = new LinearGradientBrush(new RectangleF(x0, y - 1, x1 - x0, 2), Color.FromArgb(150, Theme.Border), Color.FromArgb(0, Theme.Border), 0f))
                    g.FillRectangle(lb, x0, y - 0.5f, x1 - x0, 1f);
            }
        }
    }
}

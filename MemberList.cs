using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Relais
{
    /// <summary>Liste dessinée des persos du profil : glisser-déposer, interrupteur, raccourci.</summary>
    public sealed class MemberList : Control
    {
        readonly App app;
        int hover = -1, dragFrom = -1, dragTo = -1, scroll;
        Point downPt;
        bool dragging;
        int capturingRow = -1;
        readonly HotkeyBox proxy = new HotkeyBox(); // réutilise la logique de capture

        readonly ToolTip tip = new ToolTip();

        public MemberList(App app)
        {
            tip.AutoPopDelay = 20000;
            this.app = app;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            BackColor = Theme.Bg;
            Font = Theme.Normal;
            proxy.ValueChanged += delegate { OnCaptured(); };
            proxy.Cancelled += delegate { capturingRow = -1; Invalidate(); };
        }

        int RowH { get { return Theme.S(54); } }
        int Gap { get { return Theme.S(6); } }
        List<Member> Members { get { return app.S.GetCurrent().Members; } }

        Rectangle RowRect(int i)
        {
            return new Rectangle(0, i * (RowH + Gap) - scroll, Width - 1, RowH);
        }

        int RowAt(int y)
        {
            int i = (y + scroll) / (RowH + Gap);
            if (i < 0 || i >= Members.Count) return -1;
            if ((y + scroll) - i * (RowH + Gap) > RowH) return -1;
            return i;
        }

        Rectangle AvatarRect(Rectangle r) { return new Rectangle(r.X + Theme.S(46), r.Y + (r.Height - Theme.S(38)) / 2, Theme.S(38), Theme.S(38)); }
        Rectangle ToggleRect(Rectangle r) { return new Rectangle(r.Right - Theme.S(50), r.Y + (r.Height - Theme.S(20)) / 2, Theme.S(36), Theme.S(20)); }
        Rectangle KeyRect(Rectangle r) { return new Rectangle(r.Right - Theme.S(196), r.Y + (r.Height - Theme.S(30)) / 2, Theme.S(134), Theme.S(30)); }

        int MaxScroll { get { return Math.Max(0, Members.Count * (RowH + Gap) - Gap - Height); } }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            scroll = Math.Max(0, Math.Min(MaxScroll, scroll - e.Delta / 2));
            Invalidate();
            base.OnMouseWheel(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Hq(g);
            g.Clear(Theme.Bg);
            List<Member> ms = Members;
            if (scroll > MaxScroll) scroll = MaxScroll;

            if (ms.Count == 0)
            {
                Theme.DrawText(g, L.T("Aucun perso dans ce profil.\nConnecte tes comptes Dofus : ils seront ajoutés automatiquement."),
                    Theme.Normal, Theme.Muted, ClientRectangle, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
                return;
            }

            IntPtr fg = app.ForegroundHandle;
            List<GameWindow> rot = app.Rotation();
            for (int i = 0; i < ms.Count; i++)
            {
                Member m = ms[i];
                Rectangle r = RowRect(i);
                if (r.Bottom < 0 || r.Top > Height) continue;
                GameWindow w = app.WindowOf(m.Name);
                bool online = w != null;
                bool active = online && w.Handle == fg;
                bool dim = !online || !m.Enabled;

                Color bg = i == hover && !dragging ? Theme.Hover : Theme.Panel;
                if (dragging && i == dragFrom) bg = Theme.Panel2;
                Theme.FillRound(g, bg, r, Theme.S(10));
                Theme.StrokeRound(g, Color.FromArgb(110, Theme.Border), 1f, new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1), Theme.S(10));
                if (active) Theme.StrokeRound(g, Theme.Accent, 1.6f, new RectangleF(r.X + 0.8f, r.Y + 0.8f, r.Width - 1.6f, r.Height - 1.6f), Theme.S(10));

                // poignée
                int gx = r.X + Theme.S(12), gy = r.Y + r.Height / 2;
                using (SolidBrush b = new SolidBrush(Theme.Faint))
                    for (int yy = -1; yy <= 1; yy++) for (int xx = 0; xx <= 1; xx++)
                        g.FillEllipse(b, gx + xx * Theme.S(5), gy + yy * Theme.S(5) - Theme.S(2), Theme.S(3), Theme.S(3));

                // numéro d'ordre dans la rotation
                int pos = online && m.Enabled ? rot.FindIndex(delegate (GameWindow x) { return x == w; }) + 1 : 0;
                Theme.DrawText(g, pos > 0 ? pos.ToString() : "–", Theme.SmallBold, pos > 0 ? Theme.Accent : Theme.Faint,
                    new Rectangle(r.X + Theme.S(26), r.Y, Theme.S(18), r.Height), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

                // avatar
                int av = Theme.S(34);
                Theme.Avatar(g, new RectangleF(r.X + Theme.S(48), r.Y + (r.Height - av) / 2f, av, av), m.Name, w != null ? w.Class : "", dim);

                // nom + état
                int tx = r.X + Theme.S(92);
                int tw = KeyRect(r).X - tx - Theme.S(8);
                Theme.DrawText(g, m.Name, Theme.Bold, dim ? Theme.Faint : Theme.Text,
                    new Rectangle(tx, r.Y + Theme.S(8), tw, Theme.S(20)), TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
                string sub = online ? ((w.Class.Length > 0 ? w.Class + " · " : "") + (active ? L.T("au premier plan") : L.T("connecté"))) : L.T("hors ligne");
                if (!m.Enabled) sub = L.T("désactivé · ") + sub;
                int vol = app.VolumeOf(m.Name);
                if (vol < 100) sub += L.T(" · son ") + vol + " %";
                if (app.NoteOf(m.Name) != null) sub += L.T(" · note");
                CharSheet sh;
                if (app.S.Sheets.TryGetValue(m.Name, out sh) && sh != null && sh.Level > 0) sub = L.T("niv. ") + sh.Level + " · " + sub;
                using (SolidBrush dot = new SolidBrush(online ? Theme.Green : Theme.Faint))
                    g.FillEllipse(dot, tx, r.Y + Theme.S(34), Theme.S(7), Theme.S(7));
                Theme.DrawText(g, sub, Theme.Small, Theme.Muted,
                    new Rectangle(tx + Theme.S(11), r.Y + Theme.S(28), tw - Theme.S(11), Theme.S(18)), TextFormatFlags.Left | TextFormatFlags.EndEllipsis);

                // raccourci
                Rectangle kr = KeyRect(r);
                bool cap = i == capturingRow;
                Theme.FillRound(g, cap ? Theme.AccentDim : Theme.Panel2, kr, Theme.S(6));
                if (cap) Theme.StrokeRound(g, Theme.Accent, 1.2f, kr, Theme.S(6));
                Hotkey hk = Hotkey.Parse(m.Hotkey);
                string kt = cap ? L.T("Appuie sur une touche…") : (hk == null ? L.T("+ raccourci") : hk.Display());
                Theme.DrawText(g, kt, cap || hk == null ? Theme.Small : Theme.SmallBold, cap ? Theme.Accent : (hk == null ? Theme.Faint : Theme.Text),
                    kr, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

                // interrupteur
                Rectangle tr = ToggleRect(r);
                Theme.FillRound(g, m.Enabled ? Theme.Accent : Theme.Border, tr, tr.Height / 2f);
                float d = tr.Height - Theme.S(4);
                float kx = m.Enabled ? tr.Right - d - Theme.S(2) : tr.X + Theme.S(2);
                using (SolidBrush b = new SolidBrush(m.Enabled ? Theme.OnAccent : Theme.Muted))
                    g.FillEllipse(b, kx, tr.Y + Theme.S(2), d, d);
            }

            // indicateur de dépôt
            if (dragging && dragTo >= 0)
            {
                int y = dragTo * (RowH + Gap) - Gap / 2 - scroll;
                using (Pen p = new Pen(Theme.Accent, Theme.S(2)))
                    g.DrawLine(p, Theme.S(8), y, Width - Theme.S(8), y);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (e.Button == MouseButtons.Left && dragFrom >= 0)
            {
                if (!dragging && Math.Abs(e.Y - downPt.Y) > Theme.S(5)) dragging = true;
                if (dragging)
                {
                    int slot = (e.Y + scroll + (RowH + Gap) / 2) / (RowH + Gap);
                    dragTo = Math.Max(0, Math.Min(Members.Count, slot));
                    if (e.Y < Theme.S(10)) scroll = Math.Max(0, scroll - Theme.S(8));
                    if (e.Y > Height - Theme.S(10)) scroll = Math.Min(MaxScroll, scroll + Theme.S(8));
                    Cursor = Cursors.SizeNS;
                    Invalidate();
                    return;
                }
            }
            int h = RowAt(e.Y);
            if (h != hover)
            {
                hover = h; Invalidate();
            }
            Cursor = h >= 0 ? Cursors.Hand : Cursors.Default;
            string want = h < 0 ? "" : AvatarRect(RowRect(h)).Contains(e.Location) ? L.T("Clic : fiche du perso, image, couleur") : (app.NoteOf(Members[h].Name) ?? "");
            if (tip.GetToolTip(this) != want) tip.SetToolTip(this, want);
        }

        protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            int i = RowAt(e.Y);
            if (e.Button != MouseButtons.Left || i < 0) return;
            Rectangle r = RowRect(i);
            if (ToggleRect(r).Contains(e.Location) || KeyRect(r).Contains(e.Location) || AvatarRect(r).Contains(e.Location)) return;
            dragFrom = i; downPt = e.Location; dragging = false; dragTo = -1;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (dragging)
            {
                int from = dragFrom, to = dragTo;
                dragging = false; dragFrom = -1; dragTo = -1; Cursor = Cursors.Default;
                if (to >= 0 && to != from && to != from + 1)
                {
                    Member m = Members[from];
                    Members.RemoveAt(from);
                    if (to > from) to--;
                    Members.Insert(to, m);
                    app.ProfileEdited();
                }
                Invalidate();
                return;
            }
            dragFrom = -1;
            int i = RowAt(e.Y);
            if (i < 0) return;
            Rectangle r = RowRect(i);
            Member mem = Members[i];

            if (e.Button == MouseButtons.Right) { ShowMenu(i, e.Location); return; }
            if (e.Button != MouseButtons.Left) return;

            if (ToggleRect(r).Contains(e.Location)) { mem.Enabled = !mem.Enabled; app.ProfileEdited(); return; }
            if (KeyRect(r).Contains(e.Location)) { StartCapture(i); return; }
            if (AvatarRect(r).Contains(e.Location)) { AvatarMenu(mem.Name, e.Location); return; }
        }

        /// <summary>Clic sur l'avatar : fiche + apparence.</summary>
        void AvatarMenu(string name, Point pt)
        {
            ContextMenuStrip cm = new ContextMenuStrip();
            cm.Items.Add(L.T("Fiche du perso…"), null, delegate { app.OpenSheet(name); });
            cm.Items.Add(new ToolStripSeparator());
            cm.Items.Add(L.T("Capturer l'avatar depuis le jeu…"), null, delegate { app.CaptureAvatar(name); }).Enabled = app.WindowOf(name) != null;
            cm.Items.Add(L.T("Choisir une image…"), null, delegate { PickImage(name); });
            cm.Items.Add(L.T("Choisir une couleur…"), null, delegate
            {
                using (ColorDialog cd = new ColorDialog())
                {
                    cd.FullOpen = true;
                    cd.Color = Theme.ForCharacter(name, "");
                    if (cd.ShowDialog(FindForm()) == DialogResult.OK) app.SetCharColor(name, cd.Color);
                }
            });
            cm.Items.Add(L.T("Réinitialiser l'apparence"), null, delegate { app.SetCharColor(name, null); app.SetCharImage(name, null); });
            cm.Show(this, pt);
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            int i = RowAt(e.Y);
            if (i < 0) return;
            Rectangle r = RowRect(i);
            if (ToggleRect(r).Contains(e.Location) || KeyRect(r).Contains(e.Location)) return;
            app.Activate(Members[i].Name);
        }

        void StartCapture(int i)
        {
            app.CancelCapture();
            capturingRow = i;
            proxy.Value = Members[i].Hotkey;
            proxy.Capturing = true;
            app.Capture(proxy);
            Invalidate();
        }

        void OnCaptured()
        {
            int i = capturingRow;
            capturingRow = -1;
            if (i < 0 || i >= Members.Count) { Invalidate(); return; }
            Member m = Members[i];
            string owner = app.Owner(proxy.Value, m);
            if (owner != null)
            {
                MessageBox.Show(FindForm(), L.T("Ce raccourci est déjà utilisé par « ") + owner + " ».", "Relais", MessageBoxButtons.OK, MessageBoxIcon.Information);
                Invalidate();
                return;
            }
            m.Hotkey = proxy.Value;
            app.ProfileEdited();
        }

        void ShowMenu(int i, Point pt)
        {
            Member m = Members[i];
            ContextMenuStrip cm = new ContextMenuStrip();
            cm.Items.Add(L.T("Aller sur ce perso"), null, delegate { app.Activate(m.Name); }).Enabled = app.WindowOf(m.Name) != null;
            cm.Items.Add(L.T("Définir le raccourci…"), null, delegate { StartCapture(i); });
            cm.Items.Add(L.T("Effacer le raccourci"), null, delegate { m.Hotkey = null; app.ProfileEdited(); }).Enabled = m.Hotkey != null;
            cm.Items.Add(L.T("Copier /invite ") + m.Name, null, delegate { app.CopyInvite(m.Name); });
            cm.Items.Add(new ToolStripSeparator());
            cm.Items.Add(app.NoteOf(m.Name) == null ? L.T("Ajouter une note…") : L.T("Modifier la note…"), null, delegate
            {
                string n = Prompt.AskMultiline(FindForm(), L.T("Note — ") + m.Name, L.T("Quêtes en cours, objectifs, stuff à acheter…"), app.NoteOf(m.Name));
                if (n != null) app.SetNote(m.Name, n);
            });
            ToolStripMenuItem vol = new ToolStripMenuItem(L.T("Volume du son"));
            int cur = app.VolumeOf(m.Name);
            foreach (int v in new int[] { 100, 75, 50, 25, 10, 0 })
            {
                int val = v;
                ToolStripMenuItem it = new ToolStripMenuItem(v == 0 ? L.T("Muet") : v + " %", null, delegate { app.SetVolume(m.Name, val); });
                it.Checked = cur == v;
                vol.DropDownItems.Add(it);
            }
            cm.Items.Add(vol);
            cm.Items.Add(L.T("Fiche du perso…"), null, delegate { app.OpenSheet(m.Name); });
            ToolStripMenuItem look = new ToolStripMenuItem(L.T("Apparence"));
            look.DropDownItems.Add(L.T("Capturer depuis le jeu…"), null, delegate { app.CaptureAvatar(m.Name); }).Enabled = app.WindowOf(m.Name) != null;
            look.DropDownItems.Add(L.T("Choisir une image…"), null, delegate { PickImage(m.Name); });
            look.DropDownItems.Add(L.T("Choisir une couleur…"), null, delegate
            {
                using (ColorDialog cd = new ColorDialog())
                {
                    cd.FullOpen = true;
                    cd.Color = Theme.ForCharacter(m.Name, "");
                    if (cd.ShowDialog(FindForm()) == DialogResult.OK) app.SetCharColor(m.Name, cd.Color);
                }
            });
            look.DropDownItems.Add(L.T("Réinitialiser l'apparence"), null, delegate { app.SetCharColor(m.Name, null); app.SetCharImage(m.Name, null); });
            cm.Items.Add(look);
            cm.Items.Add(new ToolStripSeparator());
            cm.Items.Add(L.T("Monter"), null, delegate { MoveRow(i, -1); }).Enabled = i > 0;
            cm.Items.Add(L.T("Descendre"), null, delegate { MoveRow(i, +1); }).Enabled = i < Members.Count - 1;
            cm.Items.Add(L.T("Mettre en chef de team (1er)"), null, delegate { Members.RemoveAt(i); Members.Insert(0, m); app.ProfileEdited(); }).Enabled = i > 0;
            cm.Items.Add(new ToolStripSeparator());
            cm.Items.Add(L.T("Retirer du profil"), null, delegate { Members.RemoveAt(i); app.ProfileEdited(); });
            cm.Show(this, pt);
        }

        void PickImage(string name)
        {
            using (OpenFileDialog d = new OpenFileDialog())
            {
                d.Title = L.T("Image pour ") + name + L.T(" (une capture de ton perso, par exemple)");
                d.Filter = L.T("Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif|Tous les fichiers|*.*");
                if (d.ShowDialog(FindForm()) != DialogResult.OK) return;
                try { app.SetCharImage(name, d.FileName); }
                catch (Exception ex) { MessageBox.Show(FindForm(), L.T("Image illisible : ") + ex.Message, "Relais"); }
            }
        }

        void MoveRow(int i, int d)
        {
            int j = i + d;
            if (j < 0 || j >= Members.Count) return;
            Member m = Members[i];
            Members[i] = Members[j];
            Members[j] = m;
            app.ProfileEdited();
        }

        protected override bool IsInputKey(Keys k)
        {
            return k == Keys.Up || k == Keys.Down || base.IsInputKey(k);
        }
    }
}

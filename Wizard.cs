using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Relais
{
    /// <summary>Assistant du premier lancement (et présentation des nouveautés de la 2.0).</summary>
    public sealed class Onboarding : Form
    {
        readonly App app;
        int page;
        readonly Panel body = new Panel();
        readonly FlatButton next = new FlatButton("Suivant"), back = new FlatButton("Retour");
        readonly Label step = new Label();
        Toggle tF, tMouse, tBar, tOverlay, tTurn, tPrio;

        public Onboarding(App app)
        {
            this.app = app;
            Text = "Bienvenue dans Relais 2.0";
            Chrome.Hook(this);
            Icon = app.AppIcon;
            BackColor = Theme.Bg; ForeColor = Theme.Text; Font = Theme.Normal;
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(Theme.S(560), Theme.S(440));
            ShowInTaskbar = false;

            body.SetBounds(Theme.S(24), Theme.S(20), Theme.S(512), Theme.S(350));
            next.Primary = true;
            next.SetBounds(Theme.S(416), Theme.S(390), Theme.S(120), Theme.S(34));
            back.SetBounds(Theme.S(288), Theme.S(390), Theme.S(120), Theme.S(34));
            step.SetBounds(Theme.S(24), Theme.S(390), Theme.S(200), Theme.S(34));
            step.ForeColor = Theme.Faint; step.TextAlign = ContentAlignment.MiddleLeft; step.Font = Theme.Small;
            Controls.Add(body); Controls.Add(next); Controls.Add(back); Controls.Add(step);
            next.Click += delegate { if (page == 3) Finish(); else { page++; Show2(); } };
            back.Click += delegate { if (page > 0) { page--; Show2(); } };

            tF = new Toggle("Raccourcis F1, F2, F3… pour mes persos, dans l'ordre");
            tMouse = new Toggle("Souris 5 / Souris 4 = perso suivant / précédent");
            tBar = new Toggle("Mini-barre avec mes persos en haut de l'écran");
            tOverlay = new Toggle("Petite vignette (nom + ordre) dans chaque fenêtre de jeu");
            tTurn = new Toggle("Aller automatiquement au perso dont c'est le tour");
            tPrio = new Toggle("Priorité CPU au perso que je joue");
            tF.Checked = true; tMouse.Checked = true; tBar.Checked = app.S.ShowBar; tOverlay.Checked = app.S.OverlayEnabled;
            tTurn.Checked = app.S.AutoSwitchOnTurn; tPrio.Checked = app.S.PriorityBoost;
            Show2();
        }

        Label Para(string t, Font f, Color c, int h)
        {
            Label l = new Label();
            l.UseMnemonic = false;
            l.Text = t; l.Font = f; l.ForeColor = c; l.AutoSize = false; l.Height = Theme.S(h); l.Dock = DockStyle.Top;
            return l;
        }

        void Show2()
        {
            body.Controls.Clear();
            List<Control> c = new List<Control>();
            if (page == 0)
            {
                c.Add(Para("Bienvenue dans Relais 2.0", Theme.Title, Theme.Text, 40));
                c.Add(Para("L'organizer multicompte pour Dofus : une touche = une bascule de fenêtre, rien de plus côté jeu.", Theme.Normal, Theme.Muted, 44));
                c.Add(Para("Nouveautés de la 2.0", Theme.Bold, Theme.Accent, 28));
                c.Add(Para("• Aperçus en direct de tous tes comptes (idéal sur un 2e écran)\n• Vignette discrète dans chaque fenêtre + cadre sur le perso actif\n• Alertes sur ton téléphone (Discord ou ntfy) quand tu es absent\n• Carnet de craft relié à DofusDB, fiche de chaque perso\n• Codes de partage de team, thème clair, mises à jour automatiques", Theme.Normal, Theme.Text, 130));
                c.Add(Para("Relais n'envoie jamais de clic ni de touche au jeu : chaque action en jeu, tu la fais toi-même, compte par compte (règle d'Ankama).", Theme.Small, Theme.Faint, 40));
            }
            else if (page == 1)
            {
                c.Add(Para("Tes persos", Theme.Title, Theme.Text, 40));
                List<GameWindow> rot = app.Rotation();
                if (rot.Count == 0)
                    c.Add(Para("Aucun perso connecté pour l'instant. Lance Dofus et connecte tes comptes : ils apparaîtront tout seuls dans l'onglet Team.", Theme.Normal, Theme.Muted, 60));
                else
                {
                    string names = "";
                    for (int i = 0; i < rot.Count; i++) names += (i + 1) + ". " + rot[i].Name + (rot[i].Class.Length > 0 ? " (" + rot[i].Class + ")" : "") + "\n";
                    c.Add(Para("Détectés :\n" + names, Theme.Normal, Theme.Text, 30 + 22 * rot.Count));
                }
                c.Add(Para("Astuce : dans l'onglet Team, glisse les lignes pour mettre l'ordre d'initiative. Le n°1 est ton chef de team. Clique sur un avatar pour sa fiche ou son image.", Theme.Small, Theme.Faint, 50));
                c.Add(Para("Pour Superposer / Mosaïque, mets Dofus en mode fenêtré (pas plein écran).", Theme.Small, Theme.Faint, 30));
            }
            else if (page == 2)
            {
                c.Add(Para("Raccourcis", Theme.Title, Theme.Text, 40));
                c.Add(Para("Choisis un départ simple, tu pourras tout changer dans l'onglet Raccourcis.", Theme.Normal, Theme.Muted, 30));
                tF.Height = Theme.S(32); tMouse.Height = Theme.S(32);
                tF.Dock = DockStyle.Top; tMouse.Dock = DockStyle.Top;
                c.Add(tF); c.Add(tMouse);
                c.Add(Para("Les raccourcis ne marchent que quand une fenêtre Dofus est au premier plan : ailleurs, tes touches restent normales. Touche Pause = mettre Relais en pause (pour écrire dans le chat).", Theme.Small, Theme.Faint, 60));
            }
            else
            {
                c.Add(Para("Affichage & confort", Theme.Title, Theme.Text, 40));
                foreach (Toggle t in new Toggle[] { tBar, tOverlay, tTurn, tPrio }) { t.Height = Theme.S(32); t.Dock = DockStyle.Top; c.Add(t); }
                c.Add(Para("Pour « tour automatique » : active dans Dofus la notification de début de tour avec clignotement de la fenêtre. Alertes téléphone et mises à jour : onglet Options.", Theme.Small, Theme.Faint, 50));
            }
            for (int i = c.Count - 1; i >= 0; i--) body.Controls.Add(c[i]);
            back.Visible = page > 0;
            next.Text = page == 3 ? "C'est parti !" : "Suivant";
            step.Text = "Étape " + (page + 1) + " / 4";
        }

        void Finish()
        {
            Settings s = app.S;
            if (tMouse.Checked)
            {
                if (app.Owner("Souris5", "next") == null) s.KeyNext = "Souris5";
                if (app.Owner("Souris4", "prev") == null) s.KeyPrev = "Souris4";
            }
            if (tF.Checked)
            {
                if (s.KeyLeader == "F1") s.KeyLeader = null; // F1 sert au perso n°1
                List<Member> ms = s.GetCurrent().Members;
                for (int i = 0; i < ms.Count && i < 8; i++)
                {
                    string k = "F" + (i + 1);
                    if (app.Owner(k, ms[i]) == null) ms[i].Hotkey = k;
                }
            }
            s.ShowBar = tBar.Checked;
            s.OverlayEnabled = tOverlay.Checked;
            s.AutoSwitchOnTurn = tTurn.Checked;
            s.PriorityBoost = tPrio.Checked;
            s.OnboardingDone = true;
            app.ProfileEdited();
            if (s.ShowBar) app.Bar.ShowBar(); else app.Bar.Hide();
            DialogResult = DialogResult.OK;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!app.S.OnboardingDone) { app.S.OnboardingDone = true; app.S.Save(); }
            base.OnFormClosing(e);
        }
    }

    /// <summary>Recadrage d'une capture de la fenêtre de jeu pour en faire l'avatar d'un perso.</summary>
    public sealed class CropForm : Form
    {
        readonly Bitmap shot;
        Rectangle sel;       // en coordonnées image
        Point dragStart;
        Rectangle selStart;
        bool dragging, resizing;
        public Bitmap Result;

        public CropForm(Bitmap shot, string name)
        {
            this.shot = shot;
            Text = "Avatar de " + name + " — encadre la tête de ton perso";
            Chrome.Hook(this);
            BackColor = Theme.Bg; ForeColor = Theme.Text; Font = Theme.Normal;
            StartPosition = FormStartPosition.CenterScreen;
            DoubleBuffered = true;
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            double k = Math.Min(1.0, Math.Min((wa.Width * 0.8) / shot.Width, (wa.Height * 0.75) / shot.Height));
            ClientSize = new Size((int)(shot.Width * k) + Theme.S(40), (int)(shot.Height * k) + Theme.S(100));
            int side = Math.Min(shot.Width, shot.Height) / 4;
            sel = new Rectangle(shot.Width / 2 - side / 2, shot.Height / 2 - side / 2, side, side);
            FlatButton ok = new FlatButton("Utiliser"); ok.Primary = true;
            FlatButton cancel = new FlatButton("Annuler");
            ok.Anchor = cancel.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
            ok.SetBounds(ClientSize.Width - Theme.S(140), ClientSize.Height - Theme.S(50), Theme.S(120), Theme.S(34));
            cancel.SetBounds(ClientSize.Width - Theme.S(270), ClientSize.Height - Theme.S(50), Theme.S(120), Theme.S(34));
            Label hint = new Label();
            hint.Text = "Glisse le carré pour le déplacer, la poignée en bas à droite pour l'agrandir. Molette = taille.";
            hint.ForeColor = Theme.Faint; hint.Font = Theme.Small; hint.AutoSize = false;
            hint.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
            hint.SetBounds(Theme.S(20), ClientSize.Height - Theme.S(52), ClientSize.Width - Theme.S(300), Theme.S(40));
            ok.Click += delegate
            {
                Result = new Bitmap(128, 128);
                using (Graphics g = Graphics.FromImage(Result))
                {
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.DrawImage(shot, new Rectangle(0, 0, 128, 128), sel, GraphicsUnit.Pixel);
                }
                DialogResult = DialogResult.OK;
            };
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; };
            Controls.Add(ok); Controls.Add(cancel); Controls.Add(hint);
        }

        Rectangle ImageRect
        {
            get
            {
                int availW = ClientSize.Width - Theme.S(40), availH = ClientSize.Height - Theme.S(100);
                double k = Math.Min(availW / (double)shot.Width, availH / (double)shot.Height);
                int w = (int)(shot.Width * k), h = (int)(shot.Height * k);
                return new Rectangle(Theme.S(20) + (availW - w) / 2, Theme.S(20) + (availH - h) / 2, w, h);
            }
        }

        double K { get { return ImageRect.Width / (double)shot.Width; } }

        Rectangle SelScreen
        {
            get
            {
                Rectangle ir = ImageRect; double k = K;
                return new Rectangle(ir.X + (int)(sel.X * k), ir.Y + (int)(sel.Y * k), (int)(sel.Width * k), (int)(sel.Height * k));
            }
        }

        Rectangle Handle2 { get { Rectangle s = SelScreen; return new Rectangle(s.Right - Theme.S(10), s.Bottom - Theme.S(10), Theme.S(20), Theme.S(20)); } }

        void Clamp()
        {
            int min = Math.Max(16, Math.Min(shot.Width, shot.Height) / 30);
            int side = Math.Max(min, Math.Min(Math.Min(shot.Width, shot.Height), sel.Width));
            int x = Math.Max(0, Math.Min(shot.Width - side, sel.X));
            int y = Math.Max(0, Math.Min(shot.Height - side, sel.Y));
            sel = new Rectangle(x, y, side, side);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Bg);
            Rectangle ir = ImageRect;
            g.DrawImage(shot, ir);
            Rectangle s = SelScreen;
            using (Region outside = new Region(ir))
            {
                outside.Exclude(s);
                using (SolidBrush dim = new SolidBrush(Color.FromArgb(150, 0, 0, 0))) g.FillRegion(dim, outside);
            }
            Theme.Hq(g);
            using (Pen p = new Pen(Theme.Accent, 2)) { g.DrawRectangle(p, s); g.DrawEllipse(p, s); }
            using (SolidBrush b = new SolidBrush(Theme.Accent)) g.FillEllipse(b, Handle2);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            dragStart = e.Location; selStart = sel;
            resizing = Handle2.Contains(e.Location);
            dragging = !resizing && SelScreen.Contains(e.Location);
            if (!dragging && !resizing && ImageRect.Contains(e.Location))
            {
                // clic ailleurs : centre le carré ici
                double k = K; Rectangle ir = ImageRect;
                sel.X = (int)((e.X - ir.X) / k) - sel.Width / 2;
                sel.Y = (int)((e.Y - ir.Y) / k) - sel.Height / 2;
                Clamp(); selStart = sel; dragging = true; Invalidate();
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Cursor = Handle2.Contains(e.Location) ? Cursors.SizeNWSE : SelScreen.Contains(e.Location) ? Cursors.SizeAll : Cursors.Cross;
            if (!dragging && !resizing) return;
            double k = K;
            int dx = (int)((e.X - dragStart.X) / k), dy = (int)((e.Y - dragStart.Y) / k);
            if (dragging) sel = new Rectangle(selStart.X + dx, selStart.Y + dy, selStart.Width, selStart.Height);
            else { int d = Math.Max(dx, dy); sel = new Rectangle(selStart.X, selStart.Y, selStart.Width + d, selStart.Width + d); }
            Clamp(); Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); dragging = resizing = false; }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            int d = Math.Sign(e.Delta) * Math.Max(4, sel.Width / 10);
            sel = new Rectangle(sel.X - d / 2, sel.Y - d / 2, sel.Width + d, sel.Width + d);
            Clamp(); Invalidate();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            shot.Dispose();
            base.OnFormClosed(e);
        }
    }
}

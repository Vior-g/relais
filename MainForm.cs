using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace Relais
{
    public sealed class MainForm : Form
    {
        readonly App app;
        public bool AllowClose;

        SideNav tabs;
        TitleBar titleBar;
        Panel[] pages;
        ComboBox profiles;
        MemberList list;
        Label status;
        FlatButton pauseBtn;
        readonly Dictionary<string, HotkeyBox> globalBoxes = new Dictionary<string, HotkeyBox>();
        TableLayoutPanel profileKeys;
        string profileKeysSig = "";
        Toggle tOnly, tAuto, tTurn, tAlert, tSound, tBar, tVertical, tNames, tLarge, tAutoHide, tTray, tStartup;
        Toggle tPrio, tMute, tLeaderSound, tAutoLayout;
        Toggle tAutoProfile, tHoverPrev, tOverlay, tFrame, tOvTimer, tAway, tRDisc, tRTimers, tRTurn, tLight, tUpdates;
        TextBox tbDiscord, tbNtfy, tbRepo;
        NumericUpDown nAway;
        FlatButton bCorner;
        Slider sOpacity;
        bool loading;
        int shellMsg;

        public MainForm(App app)
        {
            this.app = app;
            Text = "Relais — organizer multicompte";
            Icon = app.AppIcon;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.Normal;
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.None;
            Padding = new Padding(Theme.S(4));
            ClientSize = new Size(Theme.S(800), Theme.S(720));
            MinimumSize = new Size(Theme.S(700), Theme.S(560));
            DoubleBuffered = true;
            Build();
            app.StateChanged += delegate { if (Visible) { list.Invalidate(); UpdateStatus(); } };
            app.ProfileChanged += delegate { LoadAll(); };
            LoadAll();
        }

        // ------------------------------------------------------------ Windows : shell hook (clignotement = début de tour)

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                Chrome.Round(Handle);
                shellMsg = Native.RegisterWindowMessage("SHELLHOOK");
                Native.RegisterShellHookWindow(Handle);
            }
            catch (Exception ex) { Program.Log("Shell hook : " + ex.Message); }
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            try { Native.DeregisterShellHookWindow(Handle); } catch { }
            base.OnHandleDestroyed(e);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ClassStyle |= 0x20000;                     // CS_DROPSHADOW : ombre portée
                cp.Style |= 0x00020000 | 0x00010000 | 0x00080000; // réduire / agrandir / menu système (barre des tâches)
                return cp;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (Pen p = new Pen(Theme.Border)) e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
        }

        void FixMaxBounds()
        {
            Screen scr = Screen.FromHandle(Handle);
            Rectangle wa = scr.WorkingArea;
            MaximizedBounds = new Rectangle(wa.X - scr.Bounds.X, wa.Y - scr.Bounds.Y, wa.Width, wa.Height);
        }

        protected override void OnMove(EventArgs e)
        {
            base.OnMove(e);
            if (IsHandleCreated && WindowState == FormWindowState.Normal) FixMaxBounds();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Padding = WindowState == FormWindowState.Maximized ? new Padding(0) : new Padding(Theme.S(4));
            Invalidate();
            if (titleBar != null) titleBar.Invalidate();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0084 /*WM_NCHITTEST*/ && WindowState == FormWindowState.Normal)
            {
                base.WndProc(ref m);
                Point p = PointToClient(new Point((short)(m.LParam.ToInt64() & 0xFFFF), (short)((m.LParam.ToInt64() >> 16) & 0xFFFF)));
                int b = Theme.S(6);
                bool l = p.X < b, r = p.X >= ClientSize.Width - b, t = p.Y < b, bo = p.Y >= ClientSize.Height - b;
                if (t && l) m.Result = (IntPtr)13; else if (t && r) m.Result = (IntPtr)14;
                else if (bo && l) m.Result = (IntPtr)16; else if (bo && r) m.Result = (IntPtr)17;
                else if (l) m.Result = (IntPtr)10; else if (r) m.Result = (IntPtr)11;
                else if (t) m.Result = (IntPtr)12; else if (bo) m.Result = (IntPtr)15;
                return;
            }
            if (shellMsg != 0 && m.Msg == shellMsg)
            {
                try { app.OnShellMessage((int)(m.WParam.ToInt64() & 0xFFFF), m.LParam); } catch (Exception ex) { Program.Log("Shell : " + ex.Message); }
                return;
            }
            base.WndProc(ref m);
        }

        // ------------------------------------------------------------ aides de mise en page

        static Label SectionTitle(string t)
        {
            return new OrnamentTitle(t);
        }

        static Label Hint(string t)
        {
            Label l = new Label();
            l.UseMnemonic = false;
            l.Text = t;
            l.Font = Theme.Small;
            l.ForeColor = Theme.Faint;
            l.AutoSize = false;
            l.Height = Theme.S(36);
            l.TextAlign = ContentAlignment.MiddleLeft;
            return l;
        }

        static Panel Spacer(int h)
        {
            Panel p = new Panel();
            p.Height = Theme.S(h);
            return p;
        }

        /// <summary>Empile des contrôles de haut en bas dans un panneau (Dock = Top).</summary>
        static void StackTop(Control parent, params Control[] items)
        {
            for (int i = items.Length - 1; i >= 0; i--)
            {
                items[i].Dock = DockStyle.Top;
                parent.Controls.Add(items[i]);
            }
        }

        static Panel Page()
        {
            Panel p = new Panel();
            p.Dock = DockStyle.Fill;
            p.BackColor = Theme.Bg;
            p.Padding = new Padding(Theme.S(18), Theme.S(4), Theme.S(18), Theme.S(10));
            p.Visible = false;
            return p;
        }

        static TableLayoutPanel Grid(int cols, int rows, int rowH)
        {
            TableLayoutPanel t = new TableLayoutPanel();
            t.ColumnCount = cols;
            t.RowCount = rows;
            for (int c = 0; c < cols; c++) t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / cols));
            for (int r = 0; r < rows; r++) t.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.S(rowH)));
            t.Height = Theme.S(rowH) * rows;
            return t;
        }

        // ------------------------------------------------------------ construction

        void Build()
        {
            // --- Barre de titre sur-mesure
            titleBar = new TitleBar(this);
            titleBar.OnToggleMax = delegate
            {
                FixMaxBounds();
                WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
            };
            pauseBtn = new FlatButton("Pause");
            pauseBtn.Width = Theme.S(96);
            pauseBtn.Click += delegate { app.TogglePause(); };
            FlatButton prevBtn = new FlatButton("Aperçus");
            prevBtn.Width = Theme.S(96);
            prevBtn.Click += delegate { app.TogglePreviewWall(); };
            new ToolTip().SetToolTip(prevBtn, "Aperçus en direct de tous tes comptes (idéal sur un 2e écran)");
            titleBar.AddAction(prevBtn);
            titleBar.AddAction(pauseBtn);

            // --- Navigation latérale
            tabs = new SideNav();
            tabs.Dock = DockStyle.Left;
            tabs.Tabs.Add("Team");
            tabs.Tabs.Add("Raccourcis");
            tabs.Tabs.Add("Minuteurs");
            tabs.Tabs.Add("Craft");
            tabs.Tabs.Add("Stats");
            tabs.Tabs.Add("Options");
            tabs.Footer = "Une touche = une fenêtre. Relais ne joue jamais à ta place.";
            tabs.SelectedChanged += delegate { ShowPage(tabs.Selected); app.CancelCapture(); app.S.LastTab = tabs.Selected; };

            Panel host = new Panel();
            host.Dock = DockStyle.Fill;
            pages = new Panel[] { BuildTeamPage(), BuildKeysPage(), new TimersPage(app), new CraftPage(app), new StatsPage(app), BuildOptionsPage() };
            foreach (Panel p in pages) p.Visible = false;
            foreach (Panel p in pages) host.Controls.Add(p);

            // --- Barre d'état
            Panel foot = new Panel();
            foot.Dock = DockStyle.Bottom;
            foot.Height = Theme.S(34);
            foot.BackColor = Theme.Panel;
            status = new Label();
            status.Dock = DockStyle.Fill;
            status.Padding = new Padding(Theme.S(18), 0, 0, 0);
            status.TextAlign = ContentAlignment.MiddleLeft;
            status.ForeColor = Theme.Muted;
            status.Font = Theme.Small;
            foot.Controls.Add(status);

            // Ordre d'ajout = inverse de l'ordre d'ancrage
            Controls.Add(host);
            Controls.Add(foot);
            Controls.Add(tabs);
            Controls.Add(titleBar);

            int t = app.S.LastTab;
            tabs.Selected = t;
            ShowPage(tabs.Selected);
        }

        void ShowPage(int i)
        {
            for (int k = 0; k < pages.Length; k++) pages[k].Visible = k == i;
        }

        Panel BuildTeamPage()
        {
            Panel page = Page();

            // profil
            Panel profRow = new Panel();
            profRow.Height = Theme.S(34);
            profiles = new ComboBox();
            profiles.DropDownStyle = ComboBoxStyle.DropDownList;
            profiles.FlatStyle = FlatStyle.Flat;
            profiles.BackColor = Theme.Panel2;
            profiles.ForeColor = Theme.Text;
            profiles.Font = Theme.Bold;
            profiles.SelectedIndexChanged += delegate
            {
                if (loading || profiles.SelectedItem == null) return;
                app.SwitchProfile((string)profiles.SelectedItem);
            };
            FlatButton bNew = new FlatButton("Nouveau");
            FlatButton bRen = new FlatButton("Renommer");
            FlatButton bDel = new FlatButton("Supprimer"); bDel.Danger = true;
            bNew.Click += delegate { NewProfile(); };
            bRen.Click += delegate { RenameProfile(); };
            bDel.Click += delegate { DeleteProfile(); };
            FlatButton bShare = new FlatButton("Partager");
            bShare.Click += delegate { ShareMenu(bShare); };
            profRow.Controls.Add(profiles);
            profRow.Controls.Add(bNew); profRow.Controls.Add(bRen); profRow.Controls.Add(bShare); profRow.Controls.Add(bDel);
            profRow.Resize += delegate
            {
                int bw = Theme.S(86), gap = Theme.S(6), w = profRow.Width;
                bDel.SetBounds(w - bw, 0, bw, Theme.S(32));
                bShare.SetBounds(w - 2 * bw - gap, 0, bw, Theme.S(32));
                bRen.SetBounds(w - 3 * bw - 2 * gap, 0, bw, Theme.S(32));
                bNew.SetBounds(w - 4 * bw - 3 * gap, 0, bw, Theme.S(32));
                profiles.SetBounds(0, Theme.S(3), Math.Max(Theme.S(80), w - 4 * bw - 4 * gap - Theme.S(4)), Theme.S(28));
            };

            // fenêtres
            Panel win = new Panel();
            win.Dock = DockStyle.Bottom;
            win.Height = Theme.S(96);
            FlatButton bStack = new FlatButton("Superposer");
            FlatButton bMosaic = new FlatButton("Mosaïque");
            FlatButton bInvite = new FlatButton("Inviter la team"); bInvite.Primary = true;
            FlatButton bSave = new FlatButton("Enregistrer la disposition");
            FlatButton bRestore = new FlatButton("Restaurer");
            bStack.Click += delegate { app.StackWindows(); };
            bMosaic.Click += delegate { app.MosaicWindows(); };
            bInvite.Click += delegate { app.InviteNext(); };
            bSave.Click += delegate { app.SaveLayout(); };
            bRestore.Click += delegate { app.ApplyLayout(false); };
            ToolTip tt = new ToolTip();
            tt.SetToolTip(bStack, "Toutes les fenêtres prennent la taille et la place de celle au premier plan : un PNJ est au même endroit sur chaque compte.");
            tt.SetToolTip(bInvite, "Copie « /invite Perso » du perso suivant. Colle dans le chat du chef, puis reclique pour le suivant.");
            tt.SetToolTip(bSave, "Mémorise la position de chaque fenêtre pour ce profil.");
            win.Controls.Add(bStack); win.Controls.Add(bMosaic); win.Controls.Add(bInvite);
            win.Controls.Add(bSave); win.Controls.Add(bRestore);
            win.Resize += delegate
            {
                int w = win.Width, gap = Theme.S(8), h = Theme.S(34);
                int c3 = (w - 2 * gap) / 3;
                bStack.SetBounds(0, Theme.S(10), c3, h);
                bMosaic.SetBounds(c3 + gap, Theme.S(10), c3, h);
                bInvite.SetBounds(2 * (c3 + gap), Theme.S(10), w - 2 * (c3 + gap), h);
                int r2 = Theme.S(52), half = (w - gap) * 6 / 10;
                bSave.SetBounds(0, r2, half, h);
                bRestore.SetBounds(half + gap, r2, w - half - gap, h);
            };

            Panel listWrap = new Panel();
            listWrap.Dock = DockStyle.Fill;
            list = new MemberList(app);
            list.Dock = DockStyle.Fill;
            listWrap.Controls.Add(list);

            page.Controls.Add(listWrap);
            page.Controls.Add(win);
            StackTop(page, SectionTitle("Profil de team"), profRow, SectionTitle("Personnages · glisse pour l'ordre d'initiative · double-clic pour y aller"));
            return page;
        }

        Panel BuildKeysPage()
        {
            Panel page = Page();
            page.AutoScroll = true;

            TableLayoutPanel kt = Grid(4, 6, 42);
            kt.ColumnStyles.Clear();
            kt.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22));
            kt.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28));
            kt.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22));
            kt.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28));
            AddKey(kt, 0, 0, "Perso suivant", "next");
            AddKey(kt, 2, 0, "Perso précédent", "prev");
            AddKey(kt, 0, 1, "Chef de team", "leader");
            AddKey(kt, 2, 1, "Pause", "pause");
            AddKey(kt, 0, 2, "Superposer", "stack");
            AddKey(kt, 2, 2, "Mosaïque", "mosaic");
            AddKey(kt, 0, 3, "Invitation suivante", "invite");
            AddKey(kt, 2, 3, "Restaurer disposition", "layout");
            AddKey(kt, 0, 4, "Afficher la barre", "bar");
            AddKey(kt, 2, 4, "Aperçus en direct", "preview");
            AddKey(kt, 0, 5, "Perso d'avant", "lastchar");

            profileKeys = new TableLayoutPanel();
            profileKeys.ColumnCount = 2;
            profileKeys.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            profileKeys.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

            StackTop(page,
                SectionTitle("Raccourcis généraux"), kt,
                SectionTitle("Changer de profil"), profileKeys,
                Hint("Clic : définir · Clic droit : effacer · Échap : annuler.  Les raccourcis des persos se règlent dans l'onglet Team."));
            return page;
        }

        Panel BuildOptionsPage()
        {
            Panel page = Page();
            page.AutoScroll = true;

            TableLayoutPanel behav = Grid(2, 3, 32);
            tOnly = AddToggle(behav, 0, 0, "Raccourcis seulement en jeu");
            tAuto = AddToggle(behav, 1, 0, "Ajouter les nouveaux persos");
            tTurn = AddToggle(behav, 0, 1, "Aller au perso dont c'est le tour");
            tAlert = AddToggle(behav, 1, 1, "Alerte si un perso se déconnecte");
            tSound = AddToggle(behav, 1, 2, "Son pour l'alerte");
            tAutoProfile = AddToggle(behav, 0, 2, "Choisir le profil automatiquement");
            Label turnHint = Hint("« Tour auto » : active dans Dofus la notification de début de tour avec clignotement de la fenêtre. Relais bascule dès que la fenêtre clignote.");
            turnHint.Height = Theme.S(40);

            TableLayoutPanel bar = Grid(2, 3, 32);
            tBar = AddToggle(bar, 0, 0, "Afficher la mini-barre");
            tVertical = AddToggle(bar, 1, 0, "Barre verticale");
            tNames = AddToggle(bar, 0, 1, "Noms dans la barre");
            tLarge = AddToggle(bar, 1, 1, "Grandes vignettes");
            tAutoHide = AddToggle(bar, 0, 2, "Masquer hors du jeu");
            tHoverPrev = AddToggle(bar, 1, 2, "Aperçu au survol");
            sOpacity = new Slider("Opacité de la barre");
            sOpacity.Minimum = 30; sOpacity.Maximum = 100;
            sOpacity.Height = Theme.S(34);

            TableLayoutPanel perf = Grid(2, 2, 32);
            tPrio = AddToggle(perf, 0, 0, "Priorité CPU au perso actif");
            tMute = AddToggle(perf, 1, 0, "Couper le son en arrière-plan");
            tLeaderSound = AddToggle(perf, 1, 1, "Garder le son du chef");
            tAutoLayout = AddToggle(perf, 0, 1, "Disposition auto par profil");
            Label perfHint = Hint("Priorité : le perso au premier plan passe « au-dessus de la normale », les autres « en dessous ». Disposition auto : la disposition enregistrée du profil est remise en place quand tu changes de profil. Volume par perso : clic droit sur un perso.");
            perfHint.Height = Theme.S(52);

            Panel accent = new Panel();
            accent.Height = Theme.S(42);
            Label al = new Label();
            al.Text = "Couleur d'accent"; al.ForeColor = Theme.Text; al.AutoSize = false; al.TextAlign = ContentAlignment.MiddleLeft;
            accent.Controls.Add(al);
            List<Control> sw = new List<Control>();
            foreach (Color c in Theme.AccentPresets)
            {
                Color col = c;
                Panel p = new Panel();
                p.Cursor = Cursors.Hand;
                p.Paint += delegate (object s2, PaintEventArgs pe)
                {
                    Theme.Hq(pe.Graphics);
                    pe.Graphics.Clear(Theme.Bg);
                    RectangleF rr = new RectangleF(Theme.S(3), Theme.S(3), p.Width - Theme.S(7), p.Height - Theme.S(7));
                    using (SolidBrush b = new SolidBrush(col)) pe.Graphics.FillEllipse(b, rr);
                    if (Theme.Accent.ToArgb() == col.ToArgb())
                        using (Pen pen = new Pen(Theme.Text, 2f)) pe.Graphics.DrawEllipse(pen, rr.X - 1.5f, rr.Y - 1.5f, rr.Width + 3, rr.Height + 3);
                };
                p.Click += delegate { app.SetAccent(col); };
                accent.Controls.Add(p);
                sw.Add(p);
            }
            FlatButton other = new FlatButton("Autre…");
            other.Click += delegate
            {
                using (ColorDialog cd = new ColorDialog())
                {
                    cd.FullOpen = true; cd.Color = Theme.Accent;
                    if (cd.ShowDialog(this) == DialogResult.OK) app.SetAccent(cd.Color);
                }
            };
            accent.Controls.Add(other);
            accent.Resize += delegate
            {
                al.SetBounds(0, 0, Theme.S(150), accent.Height);
                int x = Theme.S(150), d = Theme.S(30);
                foreach (Control c in sw) { c.SetBounds(x, (accent.Height - d) / 2, d, d); x += d + Theme.S(6); }
                other.SetBounds(x + Theme.S(6), (accent.Height - Theme.S(30)) / 2, Theme.S(80), Theme.S(30));
            };

            TableLayoutPanel appg = Grid(2, 1, 32);
            tStartup = AddToggle(appg, 0, 0, "Lancer avec Windows");
            tTray = AddToggle(appg, 1, 0, "Fermer = réduire en icône");

            Panel btns = new Panel();
            btns.Height = Theme.S(46);
            FlatButton bExp = new FlatButton("Exporter les profils");
            FlatButton bImp = new FlatButton("Importer");
            FlatButton bDir = new FlatButton("Dossier de config");
            bExp.Click += delegate { ExportProfiles(); };
            bImp.Click += delegate { ImportProfiles(); };
            bDir.Click += delegate { try { Directory.CreateDirectory(Settings.Dir); Process.Start("explorer.exe", Settings.Dir); } catch { } };
            btns.Controls.Add(bExp); btns.Controls.Add(bImp); btns.Controls.Add(bDir);
            btns.Resize += delegate
            {
                int w = btns.Width, gap = Theme.S(8), bw = (w - 2 * gap) / 3;
                bExp.SetBounds(0, Theme.S(8), bw, Theme.S(32));
                bImp.SetBounds(bw + gap, Theme.S(8), bw, Theme.S(32));
                bDir.SetBounds(2 * (bw + gap), Theme.S(8), w - 2 * (bw + gap), Theme.S(32));
            };

            Panel btns2 = new Panel();
            btns2.Height = Theme.S(46);
            FlatButton bDiag = new FlatButton("Diagnostic des fenêtres");
            FlatButton bAdmin = new FlatButton("Relancer Relais en admin");
            bDiag.Click += delegate { ShowDiagnostic(); };
            bAdmin.Click += delegate { app.RelaunchAsAdmin(); };
            btns2.Controls.Add(bDiag); btns2.Controls.Add(bAdmin);
            btns2.Resize += delegate
            {
                int w = btns2.Width, gap = Theme.S(8), bw = (w - gap) / 2;
                bDiag.SetBounds(0, Theme.S(6), bw, Theme.S(32));
                bAdmin.SetBounds(bw + gap, Theme.S(6), w - bw - gap, Theme.S(32));
            };

            tOnly.CheckedChanged += delegate { if (!loading) { app.S.OnlyWhenDofusFocused = tOnly.Checked; app.S.Save(); } };
            tAuto.CheckedChanged += delegate { if (!loading) { app.S.AutoAddNewCharacters = tAuto.Checked; app.S.Save(); } };
            tTurn.CheckedChanged += delegate { if (!loading) { app.S.AutoSwitchOnTurn = tTurn.Checked; app.S.Save(); } };
            tAlert.CheckedChanged += delegate { if (!loading) { app.S.AlertOnDisconnect = tAlert.Checked; app.S.Save(); } };
            tSound.CheckedChanged += delegate { if (!loading) { app.S.AlertSound = tSound.Checked; app.S.Save(); } };
            tBar.CheckedChanged += delegate { if (!loading && tBar.Checked != app.S.ShowBar) app.ToggleBar(); };
            tVertical.CheckedChanged += delegate { if (!loading) { app.S.BarVertical = tVertical.Checked; app.ProfileEdited(); } };
            tNames.CheckedChanged += delegate { if (!loading) { app.S.BarShowNames = tNames.Checked; app.ProfileEdited(); } };
            tLarge.CheckedChanged += delegate { if (!loading) { app.S.BarLarge = tLarge.Checked; app.ProfileEdited(); } };
            tAutoHide.CheckedChanged += delegate { if (!loading) { app.S.BarAutoHide = tAutoHide.Checked; app.ProfileEdited(); } };
            sOpacity.ValueChanged += delegate { if (!loading) { app.S.BarOpacity = sOpacity.Value; app.Bar.ApplyLook(); } };
            sOpacity.MouseUp += delegate { app.S.Save(); };
            tPrio.CheckedChanged += delegate { if (!loading) { app.S.PriorityBoost = tPrio.Checked; app.S.Save(); app.ApplyPriority(false); } };
            tMute.CheckedChanged += delegate { if (!loading) { app.S.AudioMuteBackground = tMute.Checked; app.S.Save(); app.ApplyAudio(true); } };
            tLeaderSound.CheckedChanged += delegate { if (!loading) { app.S.AudioKeepLeader = tLeaderSound.Checked; app.S.Save(); app.ApplyAudio(true); } };
            tAutoLayout.CheckedChanged += delegate { if (!loading) { app.S.AutoApplyLayout = tAutoLayout.Checked; app.S.Save(); } };
            tStartup.CheckedChanged += delegate { if (!loading) Autostart.Enabled = tStartup.Checked; };
            tTray.CheckedChanged += delegate { if (!loading) { app.S.MinimizeToTray = tTray.Checked; app.S.Save(); } };

            // --- vignettes en jeu
            TableLayoutPanel ov = Grid(2, 2, 32);
            tOverlay = AddToggle(ov, 0, 0, "Vignette dans le jeu");
            tFrame = AddToggle(ov, 1, 0, "Cadre coloré sur le perso actif");
            tOvTimer = AddToggle(ov, 0, 1, "Afficher le prochain minuteur");
            bCorner = new FlatButton("Coin : haut gauche");
            bCorner.Dock = DockStyle.Fill; bCorner.Margin = new Padding(0, Theme.S(2), Theme.S(12), Theme.S(2));
            ov.Controls.Add(bCorner, 1, 1);
            bCorner.Click += delegate { app.S.OverlayCorner = (app.S.OverlayCorner + 1) % 4; app.S.Save(); CornerText(); app.NotifyProfileChanged(); };
            tOverlay.CheckedChanged += delegate { if (!loading) { app.S.OverlayEnabled = tOverlay.Checked; app.S.Save(); app.Overlays.Sync(); } };
            tFrame.CheckedChanged += delegate { if (!loading) { app.S.OverlayFrame = tFrame.Checked; app.S.Save(); app.NotifyProfileChanged(); } };
            tOvTimer.CheckedChanged += delegate { if (!loading) { app.S.OverlayShowTimer = tOvTimer.Checked; app.S.Save(); app.NotifyProfileChanged(); } };
            Label ovHint = Hint("La vignette laisse passer tous les clics et suit la fenêtre. Le cadre clignote quand c'est le tour d'un perso.");
            ovHint.Height = Theme.S(36);

            // --- alertes téléphone
            tbDiscord = UiKit.Input(); tbNtfy = UiKit.Input();
            Panel rowD = LabeledInput("Webhook Discord", tbDiscord);
            Panel rowN = LabeledInput("Sujet ntfy", tbNtfy);
            tbDiscord.Leave += delegate { app.S.DiscordWebhook = tbDiscord.Text.Trim(); app.S.Save(); };
            tbNtfy.Leave += delegate { app.S.NtfyTopic = tbNtfy.Text.Trim(); app.S.Save(); };
            TableLayoutPanel rem = Grid(2, 2, 32);
            tRDisc = AddToggle(rem, 0, 0, "Déconnexions");
            tRTimers = AddToggle(rem, 1, 0, "Minuteurs et rappels");
            tRTurn = AddToggle(rem, 0, 1, "Début de tour / notifications");
            Panel awayRow = new Panel(); awayRow.Height = Theme.S(36);
            tAway = new Toggle("Seulement si je suis absent depuis");
            nAway = new NumericUpDown(); nAway.Minimum = 1; nAway.Maximum = 120;
            nAway.BackColor = Theme.Panel2; nAway.ForeColor = Theme.Text; nAway.BorderStyle = BorderStyle.FixedSingle;
            Label lmin = new Label(); lmin.Text = "min"; lmin.ForeColor = Theme.Muted; lmin.TextAlign = ContentAlignment.MiddleLeft;
            FlatButton bTest = new FlatButton("Envoyer un test");
            awayRow.Controls.Add(tAway); awayRow.Controls.Add(nAway); awayRow.Controls.Add(lmin); awayRow.Controls.Add(bTest);
            awayRow.Resize += delegate
            {
                tAway.SetBounds(0, Theme.S(4), Theme.S(270), Theme.S(28));
                nAway.SetBounds(Theme.S(274), Theme.S(6), Theme.S(56), Theme.S(26));
                lmin.SetBounds(Theme.S(334), Theme.S(4), Theme.S(40), Theme.S(28));
                bTest.SetBounds(awayRow.Width - Theme.S(150), Theme.S(2), Theme.S(150), Theme.S(32));
            };
            tRDisc.CheckedChanged += delegate { if (!loading) { app.S.RemoteDisconnect = tRDisc.Checked; app.S.Save(); } };
            tRTimers.CheckedChanged += delegate { if (!loading) { app.S.RemoteTimers = tRTimers.Checked; app.S.Save(); } };
            tRTurn.CheckedChanged += delegate { if (!loading) { app.S.RemoteTurn = tRTurn.Checked; app.S.Save(); } };
            tAway.CheckedChanged += delegate { if (!loading) { app.S.RemoteOnlyWhenAway = tAway.Checked; app.S.Save(); } };
            nAway.ValueChanged += delegate { if (!loading) { app.S.AwayMinutes = (int)nAway.Value; app.S.Save(); } };
            bTest.Click += delegate
            {
                app.S.DiscordWebhook = tbDiscord.Text.Trim(); app.S.NtfyTopic = tbNtfy.Text.Trim(); app.S.Save();
                bTest.Enabled = false; bTest.Text = "Envoi…";
                Remote.Test(app.S, delegate (string r) { bTest.Enabled = true; bTest.Text = "Envoyer un test"; MessageBox.Show(this, r, "Relais — alertes"); });
            };
            Label remHint = Hint("Discord : sur ton serveur, Paramètres du salon > Intégrations > Webhooks > Nouveau, puis copie l'URL. ntfy : installe l'appli ntfy, abonne-toi à un sujet unique (ex. relais-loic-8k2f) et écris-le ici.");
            remHint.Height = Theme.S(52);

            // --- thème
            TableLayoutPanel th = Grid(2, 1, 32);
            tLight = AddToggle(th, 0, 0, "Thème clair");
            tLight.CheckedChanged += delegate
            {
                if (loading) return;
                app.S.LightTheme = tLight.Checked; app.S.Save();
                if (MessageBox.Show(this, "Redémarrer Relais maintenant pour appliquer le thème ?", "Relais", MessageBoxButtons.YesNo) == DialogResult.Yes) app.Restart();
            };

            // --- mises à jour
            tbRepo = UiKit.Input();
            FlatButton bCheck = new FlatButton("Vérifier");
            Panel repoRow = LabeledInput("Dépôt GitHub", tbRepo, bCheck);
            tbRepo.Leave += delegate { app.S.UpdateRepo = tbRepo.Text.Trim(); app.S.Save(); };
            bCheck.Click += delegate { app.S.UpdateRepo = tbRepo.Text.Trim(); app.S.Save(); Updater.CheckInBackground(app, true); };
            TableLayoutPanel upd = Grid(2, 1, 32);
            tUpdates = AddToggle(upd, 0, 0, "Vérifier chaque jour");
            tUpdates.CheckedChanged += delegate { if (!loading) { app.S.CheckUpdates = tUpdates.Checked; app.S.Save(); } };
            Label updHint = Hint("Ex. : tonpseudo/relais. Relais regarde la dernière « Release » du dépôt et se met à jour en un clic (voir COMPILER.md).");
            updHint.Height = Theme.S(36);
            tAutoProfile.CheckedChanged += delegate { if (!loading) { app.S.AutoProfile = tAutoProfile.Checked; app.S.Save(); } };
            tHoverPrev.CheckedChanged += delegate { if (!loading) { app.S.BarHoverPreview = tHoverPrev.Checked; app.S.Save(); } };

            StackTop(page,
                SectionTitle("Comportement"), behav, turnHint,
                SectionTitle("Performance & son"), perf, perfHint,
                SectionTitle("Mini-barre"), bar, sOpacity,
                SectionTitle("Vignettes en jeu"), ov, ovHint,
                SectionTitle("Alertes sur le téléphone"), rowD, rowN, rem, awayRow, remHint,
                SectionTitle("Apparence"), accent, th,
                SectionTitle("Mises à jour"), repoRow, upd, updHint,
                SectionTitle("Application"), appg, btns,
                SectionTitle("Dépannage"), btns2);
            return page;
        }

        void AddKey(TableLayoutPanel t, int col, int row, string label, string id)
        {
            Label l = new Label();
            l.Text = label; l.ForeColor = Theme.Muted; l.AutoSize = false;
            l.Dock = DockStyle.Fill; l.TextAlign = ContentAlignment.MiddleLeft;
            HotkeyBox b = new HotkeyBox();
            b.Dock = DockStyle.Fill;
            b.Margin = new Padding(Theme.S(2), Theme.S(5), Theme.S(12), Theme.S(5));
            b.BeginCapture = delegate (HotkeyBox box) { app.Capture(box); };
            b.ValueChanged += delegate
            {
                string owner = app.Owner(b.Value, id);
                if (owner != null)
                {
                    MessageBox.Show(this, "Ce raccourci est déjà utilisé par « " + owner + " ».", "Relais", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    LoadAll();
                    return;
                }
                SetGlobal(id, b.Value);
                app.ProfileEdited();
            };
            t.Controls.Add(l, col, row);
            t.Controls.Add(b, col + 1, row);
            globalBoxes[id] = b;
        }

        static Toggle AddToggle(TableLayoutPanel t, int col, int row, string text)
        {
            Toggle tg = new Toggle(text);
            tg.Dock = DockStyle.Fill;
            t.Controls.Add(tg, col, row);
            return tg;
        }

        string GetGlobal(string id)
        {
            switch (id)
            {
                case "next": return app.S.KeyNext;
                case "prev": return app.S.KeyPrev;
                case "leader": return app.S.KeyLeader;
                case "bar": return app.S.KeyToggleBar;
                case "pause": return app.S.KeyPause;
                case "stack": return app.S.KeyStack;
                case "mosaic": return app.S.KeyMosaic;
                case "invite": return app.S.KeyInvite;
                case "layout": return app.S.KeyLayout;
                case "preview": return app.S.KeyPreview;
                case "lastchar": return app.S.KeyLastChar;
            }
            return null;
        }

        void SetGlobal(string id, string v)
        {
            switch (id)
            {
                case "next": app.S.KeyNext = v; break;
                case "prev": app.S.KeyPrev = v; break;
                case "leader": app.S.KeyLeader = v; break;
                case "bar": app.S.KeyToggleBar = v; break;
                case "pause": app.S.KeyPause = v; break;
                case "stack": app.S.KeyStack = v; break;
                case "mosaic": app.S.KeyMosaic = v; break;
                case "invite": app.S.KeyInvite = v; break;
                case "layout": app.S.KeyLayout = v; break;
                case "preview": app.S.KeyPreview = v; break;
                case "lastchar": app.S.KeyLastChar = v; break;
            }
        }

        void RebuildProfileKeys()
        {
            string sig = "";
            foreach (Profile p in app.S.Profiles) sig += p.Name + "\u0001" + p.Hotkey + "\u0002";
            if (sig == profileKeysSig) return;
            profileKeysSig = sig;

            profileKeys.SuspendLayout();
            foreach (Control c in profileKeys.Controls) c.Dispose();
            profileKeys.Controls.Clear();
            profileKeys.RowStyles.Clear();
            int rows = app.S.Profiles.Count;
            profileKeys.RowCount = rows;
            for (int i = 0; i < rows; i++)
            {
                Profile p = app.S.Profiles[i];
                profileKeys.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.S(42)));
                Label l = new Label();
                l.Text = p.Name; l.ForeColor = Theme.Text; l.AutoSize = false; l.AutoEllipsis = true;
                l.Dock = DockStyle.Fill; l.TextAlign = ContentAlignment.MiddleLeft;
                HotkeyBox b = new HotkeyBox();
                b.Dock = DockStyle.Fill;
                b.Margin = new Padding(Theme.S(2), Theme.S(5), Theme.S(12), Theme.S(5));
                b.Value = p.Hotkey;
                b.BeginCapture = delegate (HotkeyBox box) { app.Capture(box); };
                Profile prof = p;
                b.ValueChanged += delegate
                {
                    string owner = app.Owner(b.Value, prof);
                    if (owner != null)
                    {
                        MessageBox.Show(this, "Ce raccourci est déjà utilisé par « " + owner + " ».", "Relais", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        b.Value = prof.Hotkey;
                        return;
                    }
                    prof.Hotkey = b.Value;
                    app.ProfileEdited();
                };
                profileKeys.Controls.Add(l, 0, i);
                profileKeys.Controls.Add(b, 1, i);
            }
            profileKeys.Height = Theme.S(42) * Math.Max(1, rows);
            profileKeys.ResumeLayout();
        }

        // ------------------------------------------------------------ données

        void LoadAll()
        {
            loading = true;
            profiles.Items.Clear();
            foreach (Profile p in app.S.Profiles) profiles.Items.Add(p.Name);
            profiles.SelectedItem = app.S.GetCurrent().Name;
            foreach (KeyValuePair<string, HotkeyBox> kv in globalBoxes) if (!kv.Value.Capturing) kv.Value.Value = GetGlobal(kv.Key);
            RebuildProfileKeys();
            tOnly.Checked = app.S.OnlyWhenDofusFocused;
            tAuto.Checked = app.S.AutoAddNewCharacters;
            tTurn.Checked = app.S.AutoSwitchOnTurn;
            tAlert.Checked = app.S.AlertOnDisconnect;
            tSound.Checked = app.S.AlertSound;
            tBar.Checked = app.S.ShowBar;
            tVertical.Checked = app.S.BarVertical;
            tNames.Checked = app.S.BarShowNames;
            tLarge.Checked = app.S.BarLarge;
            tAutoHide.Checked = app.S.BarAutoHide;
            sOpacity.Value = app.S.BarOpacity;
            tTray.Checked = app.S.MinimizeToTray;
            tStartup.Checked = Autostart.Enabled;
            tPrio.Checked = app.S.PriorityBoost;
            tMute.Checked = app.S.AudioMuteBackground;
            tLeaderSound.Checked = app.S.AudioKeepLeader;
            tAutoLayout.Checked = app.S.AutoApplyLayout;
            tAutoProfile.Checked = app.S.AutoProfile;
            tHoverPrev.Checked = app.S.BarHoverPreview;
            tOverlay.Checked = app.S.OverlayEnabled;
            tFrame.Checked = app.S.OverlayFrame;
            tOvTimer.Checked = app.S.OverlayShowTimer;
            tRDisc.Checked = app.S.RemoteDisconnect;
            tRTimers.Checked = app.S.RemoteTimers;
            tRTurn.Checked = app.S.RemoteTurn;
            tAway.Checked = app.S.RemoteOnlyWhenAway;
            nAway.Value = Math.Max(1, Math.Min(120, app.S.AwayMinutes));
            tLight.Checked = app.S.LightTheme;
            tUpdates.Checked = app.S.CheckUpdates;
            if (!tbDiscord.Focused) tbDiscord.Text = app.S.DiscordWebhook ?? "";
            if (!tbNtfy.Focused) tbNtfy.Text = app.S.NtfyTopic ?? "";
            if (!tbRepo.Focused) tbRepo.Text = app.S.UpdateRepo ?? "";
            CornerText();
            loading = false;
            list.Invalidate();
            UpdateStatus();
        }

        void UpdateStatus()
        {
            int online = app.Windows.Count;
            int inRot = app.Rotation().Count;
            string s = online == 0
                ? "Aucune fenêtre Dofus connectée détectée — connecte tes persos, ils apparaîtront ici."
                : online + " perso" + (online > 1 ? "s" : "") + " connecté" + (online > 1 ? "s" : "") + " · " + inRot + " dans la rotation · profil « " + app.S.GetCurrent().Name + " »";
            if (app.Paused) s = "EN PAUSE · " + s;
            status.Text = s;
            status.ForeColor = app.Paused ? Theme.Accent : Theme.Muted;
            pauseBtn.Text = app.Paused ? "Reprendre" : "Pause";
            pauseBtn.Primary = app.Paused;
            pauseBtn.Invalidate();
        }

        void NewProfile()
        {
            string n = Prompt.Ask(this, "Nouveau profil", "Nom du profil (les persos connectés y seront ajoutés) :", "Team " + (app.S.Profiles.Count + 1));
            if (n == null) return;
            if (FindProfile(n) != null) { MessageBox.Show(this, "Ce nom existe déjà.", "Relais"); return; }
            Profile p = new Profile();
            p.Name = n;
            foreach (GameWindow w in app.Windows) { Member m = new Member(); m.Name = w.Name; p.Members.Add(m); }
            app.S.Profiles.Add(p);
            app.S.CurrentProfile = n;
            app.ProfileEdited();
        }

        void RenameProfile()
        {
            Profile p = app.S.GetCurrent();
            string n = Prompt.Ask(this, "Renommer le profil", "Nouveau nom :", p.Name);
            if (n == null || n == p.Name) return;
            if (FindProfile(n) != null) { MessageBox.Show(this, "Ce nom existe déjà.", "Relais"); return; }
            p.Name = n;
            app.S.CurrentProfile = n;
            app.ProfileEdited();
        }

        void DeleteProfile()
        {
            Profile p = app.S.GetCurrent();
            if (MessageBox.Show(this, "Supprimer le profil « " + p.Name + " » ?", "Relais", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            app.S.Profiles.Remove(p);
            app.S.CurrentProfile = null;
            app.S.GetCurrent();
            app.ProfileEdited();
        }

        Profile FindProfile(string n)
        {
            foreach (Profile p in app.S.Profiles) if (string.Equals(p.Name, n, StringComparison.OrdinalIgnoreCase)) return p;
            return null;
        }

        void CornerText()
        {
            string[] n = { "haut gauche", "haut droite", "bas gauche", "bas droite" };
            bCorner.Text = "Coin : " + n[Math.Max(0, Math.Min(3, app.S.OverlayCorner))];
            bCorner.Invalidate();
        }

        static Panel LabeledInput(string label, TextBox tb, params FlatButton[] buttons)
        {
            Panel row = new Panel();
            row.Height = Theme.S(38);
            Label l = new Label();
            l.Text = label; l.ForeColor = Theme.Muted; l.TextAlign = ContentAlignment.MiddleLeft;
            row.Controls.Add(l); row.Controls.Add(tb);
            foreach (FlatButton b in buttons) row.Controls.Add(b);
            row.Resize += delegate
            {
                int lw = Theme.S(140), x = row.Width;
                for (int i = buttons.Length - 1; i >= 0; i--) { x -= Theme.S(100); buttons[i].SetBounds(x, Theme.S(3), Theme.S(100), Theme.S(30)); x -= Theme.S(6); }
                l.SetBounds(0, 0, lw, row.Height);
                tb.SetBounds(lw, Theme.S(6), Math.Max(Theme.S(60), x - lw), Theme.S(26));
            };
            return row;
        }

        void ShareMenu(Control anchor)
        {
            ContextMenuStrip cm = new ContextMenuStrip();
            cm.Items.Add("Copier le code de « " + app.S.GetCurrent().Name + " »", null, delegate
            {
                string code = app.ExportCode(app.S.GetCurrent());
                if (App.CopyText(code)) app.Toast.ShowMessage("Code copié — envoie-le à un ami ou colle-le sur ton autre PC", false, 3);
            });
            cm.Items.Add("Importer un code…", null, delegate
            {
                string code = Prompt.Ask(this, "Importer une team", "Colle le code (RELAIS2-…) :", "");
                if (code == null) return;
                try
                {
                    string n = app.ImportCode(code);
                    app.Toast.ShowMessage("Profil « " + n + " » importé", false);
                }
                catch (Exception ex) { MessageBox.Show(this, "Code invalide : " + ex.Message, "Relais"); }
            });
            cm.Show(anchor, new Point(0, anchor.Height));
        }

        void ShowDiagnostic()
        {
            string text = app.Diagnostic();
            Program.Log("Diagnostic :\r\n" + text);
            using (Form f = new Form())
            {
                f.Text = "Relais — diagnostic des fenêtres";
                Chrome.Hook(f);
                f.StartPosition = FormStartPosition.CenterParent;
                f.BackColor = Theme.Bg; f.ForeColor = Theme.Text; f.Font = Theme.Normal;
                f.ClientSize = new Size(Theme.S(620), Theme.S(420));
                f.MinimizeBox = false; f.ShowInTaskbar = false;
                TextBox tb = new TextBox();
                tb.Multiline = true; tb.ReadOnly = true; tb.ScrollBars = ScrollBars.Both; tb.WordWrap = false;
                tb.Font = new Font("Consolas", 9f);
                tb.BackColor = Theme.Panel2; tb.ForeColor = Theme.Text; tb.BorderStyle = BorderStyle.FixedSingle;
                tb.Text = text;
                tb.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
                tb.SetBounds(Theme.S(14), Theme.S(14), Theme.S(592), Theme.S(340));
                FlatButton copy = new FlatButton("Copier"); copy.Primary = true;
                copy.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
                copy.SetBounds(Theme.S(486), Theme.S(370), Theme.S(120), Theme.S(34));
                copy.Click += delegate { if (App.CopyText(text)) app.Toast.ShowMessage("Diagnostic copié — colle-le à Claude", false); };
                Label hint = new Label();
                hint.Text = "« PLEIN ÉCRAN » : passe Dofus en mode fenêtré. « admin : OUI » sur Dofus mais pas sur Relais : relance Relais en admin.";
                hint.ForeColor = Theme.Faint; hint.Font = Theme.Small; hint.AutoSize = false;
                hint.Anchor = AnchorStyles.Left | AnchorStyles.Bottom | AnchorStyles.Right;
                hint.SetBounds(Theme.S(14), Theme.S(366), Theme.S(460), Theme.S(44));
                f.Controls.Add(tb); f.Controls.Add(copy); f.Controls.Add(hint);
                f.ShowDialog(this);
            }
        }

        void ExportProfiles()
        {
            using (SaveFileDialog d = new SaveFileDialog())
            {
                d.Title = "Exporter les profils";
                d.Filter = "Profils Relais (*.json)|*.json";
                d.FileName = "relais-profils.json";
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    File.WriteAllText(d.FileName, new JavaScriptSerializer().Serialize(app.S.Profiles), System.Text.Encoding.UTF8);
                    app.Toast.ShowMessage(app.S.Profiles.Count + " profil(s) exporté(s)", false);
                }
                catch (Exception ex) { MessageBox.Show(this, "Export impossible : " + ex.Message, "Relais"); }
            }
        }

        void ImportProfiles()
        {
            using (OpenFileDialog d = new OpenFileDialog())
            {
                d.Title = "Importer des profils";
                d.Filter = "Profils Relais (*.json)|*.json|Tous les fichiers|*.*";
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    List<Profile> list = new JavaScriptSerializer().Deserialize<List<Profile>>(File.ReadAllText(d.FileName, System.Text.Encoding.UTF8));
                    int n = 0;
                    if (list != null)
                        foreach (Profile p in list)
                        {
                            if (p == null || string.IsNullOrEmpty(p.Name)) continue;
                            if (p.Members == null) p.Members = new List<Member>();
                            string baseName = p.Name, name = baseName;
                            for (int k = 2; FindProfile(name) != null; k++) name = baseName + " (" + k + ")";
                            p.Name = name;
                            if (p.Hotkey != null && app.Owner(p.Hotkey, null) != null) p.Hotkey = null; // évite les conflits
                            app.S.Profiles.Add(p);
                            n++;
                        }
                    app.ProfileEdited();
                    app.Toast.ShowMessage(n + " profil(s) importé(s)", false);
                }
                catch (Exception ex) { MessageBox.Show(this, "Fichier illisible : " + ex.Message, "Relais"); }
            }
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible) { list.Invalidate(); UpdateStatus(); }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (app.HandleCaptureKey(keyData)) return true;
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            app.CancelCapture();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            app.CancelCapture();
            if (!AllowClose && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                app.S.Save();
                if (app.S.MinimizeToTray) Hide();
                else app.Quit();
                return;
            }
            base.OnFormClosing(e);
        }
    }
}

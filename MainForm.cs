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
        DropButton profiles;
        MemberList list;
        Label status;
        FlatButton pauseBtn;
        readonly Dictionary<string, HotkeyBox> globalBoxes = new Dictionary<string, HotkeyBox>();
        TableLayoutPanel profileKeys;
        string profileKeysSig = "";
        Toggle tOnly, tAuto, tTurn, tAlert, tSound, tBar, tVertical, tNames, tLarge, tAutoHide, tTray, tStartup;
        Toggle tPrio, tMute, tLeaderSound, tAutoLayout;
        Toggle tSnd, tSndTurn, tSndTimer, tSndDeco, tSndSwitch;
        Slider sVolume;
        Toggle tAutoProfile, tHoverPrev, tOverlay, tFrame, tOvTimer, tAway, tRDisc, tRTimers, tRTurn, tLight, tUpdates;
        TextBox tbDiscord, tbNtfy, tbRepo;
        NumericUpDown nAway;
        FlatButton bCorner;
        Slider sOpacity;
        Toggle tWheel, tCombat, tAlmanax, tPhone, tAutoSync;
        DropButton dLayoutScreen, dPreviewScreen, dLang;
        Label lPhone, lSync;
        TextBox tbToken;
        bool loading;
        int shellMsg;

        public MainForm(App app)
        {
            this.app = app;
            Text = L.T("Relais — organizer multicompte");
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
            FlatButton prevBtn = new FlatButton(L.T("Aperçus"));
            prevBtn.Width = Theme.S(96);
            prevBtn.Click += delegate { app.TogglePreviewWall(); };
            new ToolTip().SetToolTip(prevBtn, L.T("Aperçus en direct de tous tes comptes (idéal sur un 2e écran)"));
            titleBar.AddAction(prevBtn);
            titleBar.AddAction(pauseBtn);

            // --- Navigation latérale
            tabs = new SideNav();
            tabs.Dock = DockStyle.Left;
            tabs.Tabs.Add(L.T("Accueil"));
            tabs.Tabs.Add("Team");
            tabs.Tabs.Add(L.T("Raccourcis"));
            tabs.Tabs.Add(L.T("Minuteurs"));
            tabs.Tabs.Add("Craft");
            tabs.Tabs.Add("Stats");
            tabs.Tabs.Add("Options");
            tabs.Kinds.AddRange(new int[] { 6, 0, 1, 2, 3, 4, 5 });
            tabs.Footer = L.T("Une touche = une fenêtre. Relais ne joue jamais à ta place.");
            tabs.SelectedChanged += delegate { ShowPage(tabs.Selected); app.CancelCapture(); app.S.LastTab = tabs.Selected; };

            Panel host = new Panel();
            host.Dock = DockStyle.Fill;
            pages = new Panel[] { new HomePage(app), BuildTeamPage(), BuildKeysPage(), new TimersPage(app), new CraftPage(app), new StatsPage(app), BuildOptionsPage() };
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

            tabs.Selected = 0; // on ouvre toujours sur l'accueil
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
            profiles = new DropButton();
            profiles.SelectedIndexChanged += delegate
            {
                if (loading || profiles.SelectedItem == null) return;
                app.SwitchProfile((string)profiles.SelectedItem);
            };
            FlatButton bNew = new FlatButton(L.T("Nouveau"));
            FlatButton bRen = new FlatButton(L.T("Renommer"));
            FlatButton bDel = new FlatButton(L.T("Supprimer")); bDel.Danger = true;
            bNew.Click += delegate { NewProfile(); };
            bRen.Click += delegate { RenameProfile(); };
            bDel.Click += delegate { DeleteProfile(); };
            FlatButton bShare = new FlatButton(L.T("Partager"));
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
                profiles.SetBounds(0, 0, Math.Max(Theme.S(80), w - 4 * bw - 4 * gap - Theme.S(4)), Theme.S(32));
            };

            // fenêtres
            Panel win = new Panel();
            win.Dock = DockStyle.Bottom;
            win.Height = Theme.S(96);
            FlatButton bStack = new FlatButton(L.T("Superposer"));
            FlatButton bMosaic = new FlatButton(L.T("Mosaïque"));
            FlatButton bInvite = new FlatButton(L.T("Inviter la team")); bInvite.Primary = true;
            FlatButton bSave = new FlatButton(L.T("Enregistrer la disposition"));
            FlatButton bRestore = new FlatButton(L.T("Restaurer"));
            bStack.Click += delegate { app.StackWindows(); };
            bMosaic.Click += delegate { app.MosaicWindows(); };
            bInvite.Click += delegate { app.InviteNext(); };
            bSave.Click += delegate { app.SaveLayout(); };
            bRestore.Click += delegate { app.ApplyLayout(false); };
            ToolTip tt = new ToolTip();
            tt.SetToolTip(bStack, L.T("Toutes les fenêtres prennent la taille et la place de celle au premier plan : un PNJ est au même endroit sur chaque compte."));
            tt.SetToolTip(bInvite, L.T("Copie « /invite Perso » du perso suivant. Colle dans le chat du chef, puis reclique pour le suivant."));
            tt.SetToolTip(bSave, L.T("Mémorise la position de chaque fenêtre pour ce profil."));
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
            StackTop(page, SectionTitle(L.T("Profil de team")), profRow, SectionTitle(L.T("Personnages · glisse pour l'ordre d'initiative · double-clic pour y aller")));
            return page;
        }

        Panel BuildKeysPage()
        {
            Panel page = Page();
            page.AutoScroll = true;

            TableLayoutPanel kt = Grid(4, 6, 42);
            // (ligne 6 : palette)
            kt.ColumnStyles.Clear();
            kt.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22));
            kt.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28));
            kt.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22));
            kt.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28));
            AddKey(kt, 0, 0, L.T("Perso suivant"), "next");
            AddKey(kt, 2, 0, L.T("Perso précédent"), "prev");
            AddKey(kt, 0, 1, L.T("Chef de team"), "leader");
            AddKey(kt, 2, 1, "Pause", "pause");
            AddKey(kt, 0, 2, L.T("Superposer"), "stack");
            AddKey(kt, 2, 2, L.T("Mosaïque"), "mosaic");
            AddKey(kt, 0, 3, L.T("Invitation suivante"), "invite");
            AddKey(kt, 2, 3, L.T("Restaurer disposition"), "layout");
            AddKey(kt, 0, 4, L.T("Afficher la barre"), "bar");
            AddKey(kt, 2, 4, L.T("Aperçus en direct"), "preview");
            AddKey(kt, 0, 5, L.T("Perso d'avant"), "lastchar");
            AddKey(kt, 2, 5, L.T("Palette (en jeu)"), "palette");

            profileKeys = new TableLayoutPanel();
            profileKeys.ColumnCount = 2;
            profileKeys.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            profileKeys.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

            TableLayoutPanel mouse = Grid(2, 1, 34);
            tWheel = AddToggle(mouse, 0, 0, L.T("Alt + molette : changer de perso"));
            FlatButton bSide = new FlatButton(L.T("Boutons 4 / 5 : préc. / suiv."));
            bSide.Dock = DockStyle.Fill; bSide.Margin = new Padding(0, Theme.S(2), Theme.S(12), Theme.S(2));
            mouse.Controls.Add(bSide, 1, 0);
            bSide.Click += delegate
            {
                app.S.KeyNext = Hotkey.MOUSE5; app.S.KeyPrev = Hotkey.MOUSE4;
                app.ProfileEdited(); LoadAll();
                app.Toast.ShowMessage(L.T("Souris 5 = perso suivant · Souris 4 = précédent"), false, 2.5);
            };
            tWheel.CheckedChanged += delegate { if (!loading) { app.S.WheelCycle = tWheel.Checked; app.ProfileEdited(); } };

            StackTop(page,
                SectionTitle(L.T("Souris")), mouse,
                Hint(L.T("La molette seule reste au jeu : Relais ne réagit qu'avec Alt enfoncé. Les boutons latéraux (4 et 5) et le clic molette peuvent aussi servir de raccourci partout ci-dessous.")),
                SectionTitle(L.T("Raccourcis généraux")), kt,
                SectionTitle(L.T("Changer de profil")), profileKeys,
                Hint(L.T("Clic : définir · Clic droit : effacer · Échap : annuler.  Les raccourcis des persos se règlent dans l'onglet Team.")));
            return page;
        }

        Panel BuildOptionsPage()
        {
            Panel page = Page();
            page.AutoScroll = true;

            TableLayoutPanel behav = Grid(2, 3, 32);
            tOnly = AddToggle(behav, 0, 0, L.T("Raccourcis seulement en jeu"));
            tAuto = AddToggle(behav, 1, 0, L.T("Ajouter les nouveaux persos"));
            tTurn = AddToggle(behav, 0, 1, L.T("Aller au perso dont c'est le tour"));
            tAlert = AddToggle(behav, 1, 1, L.T("Alerte si un perso se déconnecte"));
            tSound = AddToggle(behav, 1, 2, L.T("Son pour l'alerte"));
            tAutoProfile = AddToggle(behav, 0, 2, L.T("Choisir le profil automatiquement"));
            Label turnHint = Hint(L.T("« Tour auto » : active dans Dofus la notification de début de tour avec clignotement de la fenêtre. Relais bascule dès que la fenêtre clignote."));
            turnHint.Height = Theme.S(40);

            TableLayoutPanel bar = Grid(2, 3, 32);
            tBar = AddToggle(bar, 0, 0, L.T("Afficher la mini-barre"));
            tVertical = AddToggle(bar, 1, 0, L.T("Barre verticale"));
            tNames = AddToggle(bar, 0, 1, L.T("Noms dans la barre"));
            tLarge = AddToggle(bar, 1, 1, L.T("Grandes vignettes"));
            tAutoHide = AddToggle(bar, 0, 2, L.T("Masquer hors du jeu"));
            tHoverPrev = AddToggle(bar, 1, 2, L.T("Aperçu au survol"));
            sOpacity = new Slider(L.T("Opacité de la barre"));
            sOpacity.Minimum = 30; sOpacity.Maximum = 100;
            sOpacity.Height = Theme.S(34);

            TableLayoutPanel perf = Grid(2, 2, 32);
            tPrio = AddToggle(perf, 0, 0, L.T("Priorité CPU au perso actif"));
            tMute = AddToggle(perf, 1, 0, L.T("Couper le son en arrière-plan"));
            tLeaderSound = AddToggle(perf, 1, 1, L.T("Garder le son du chef"));
            tAutoLayout = AddToggle(perf, 0, 1, L.T("Disposition auto par profil"));
            Label perfHint = Hint(L.T("Priorité : le perso au premier plan passe « au-dessus de la normale », les autres « en dessous ». Disposition auto : la disposition enregistrée du profil est remise en place quand tu changes de profil. Volume par perso : clic droit sur un perso."));
            perfHint.Height = Theme.S(52);

            Panel accent = new Panel();
            accent.Height = Theme.S(42);
            Label al = new Label();
            al.Text = L.T("Couleur d'accent"); al.ForeColor = Theme.Text; al.AutoSize = false; al.TextAlign = ContentAlignment.MiddleLeft;
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
            FlatButton other = new FlatButton(L.T("Autre…"));
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
            tStartup = AddToggle(appg, 0, 0, L.T("Lancer avec Windows"));
            tTray = AddToggle(appg, 1, 0, L.T("Fermer = réduire en icône"));

            Panel btns = new Panel();
            btns.Height = Theme.S(46);
            FlatButton bExp = new FlatButton(L.T("Exporter les profils"));
            FlatButton bImp = new FlatButton(L.T("Importer"));
            FlatButton bDir = new FlatButton(L.T("Dossier de config"));
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
            FlatButton bDiag = new FlatButton(L.T("Diagnostic des fenêtres"));
            FlatButton bAdmin = new FlatButton(L.T("Relancer Relais en admin"));
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
            tOverlay = AddToggle(ov, 0, 0, L.T("Vignette dans le jeu"));
            tFrame = AddToggle(ov, 1, 0, L.T("Cadre coloré sur le perso actif"));
            tOvTimer = AddToggle(ov, 0, 1, L.T("Afficher le prochain minuteur"));
            bCorner = new FlatButton(L.T("Coin : haut gauche"));
            bCorner.Dock = DockStyle.Fill; bCorner.Margin = new Padding(0, Theme.S(2), Theme.S(12), Theme.S(2));
            ov.Controls.Add(bCorner, 1, 1);
            bCorner.Click += delegate { app.S.OverlayCorner = (app.S.OverlayCorner + 1) % 4; app.S.Save(); CornerText(); app.NotifyProfileChanged(); };
            tOverlay.CheckedChanged += delegate { if (!loading) { app.S.OverlayEnabled = tOverlay.Checked; app.S.Save(); app.Overlays.Sync(); } };
            tFrame.CheckedChanged += delegate { if (!loading) { app.S.OverlayFrame = tFrame.Checked; app.S.Save(); app.NotifyProfileChanged(); } };
            tOvTimer.CheckedChanged += delegate { if (!loading) { app.S.OverlayShowTimer = tOvTimer.Checked; app.S.Save(); app.NotifyProfileChanged(); } };
            Label ovHint = Hint(L.T("La vignette laisse passer tous les clics et suit la fenêtre. Le cadre clignote quand c'est le tour d'un perso."));
            ovHint.Height = Theme.S(36);

            // --- alertes téléphone
            tbDiscord = UiKit.Input(); tbNtfy = UiKit.Input();
            Panel rowD = LabeledInput("Webhook Discord", tbDiscord);
            Panel rowN = LabeledInput(L.T("Sujet ntfy"), tbNtfy);
            tbDiscord.Leave += delegate { app.S.DiscordWebhook = tbDiscord.Text.Trim(); app.S.Save(); };
            tbNtfy.Leave += delegate { app.S.NtfyTopic = tbNtfy.Text.Trim(); app.S.Save(); };
            TableLayoutPanel rem = Grid(2, 2, 32);
            tRDisc = AddToggle(rem, 0, 0, L.T("Déconnexions"));
            tRTimers = AddToggle(rem, 1, 0, L.T("Minuteurs et rappels"));
            tRTurn = AddToggle(rem, 0, 1, L.T("Début de tour / notifications"));
            Panel awayRow = new Panel(); awayRow.Height = Theme.S(36);
            tAway = new Toggle(L.T("Seulement si je suis absent depuis"));
            nAway = new NumericUpDown(); nAway.Minimum = 1; nAway.Maximum = 120;
            nAway.BackColor = Theme.Panel2; nAway.ForeColor = Theme.Text; nAway.BorderStyle = BorderStyle.FixedSingle;
            Label lmin = new Label(); lmin.Text = "min"; lmin.ForeColor = Theme.Muted; lmin.TextAlign = ContentAlignment.MiddleLeft;
            FlatButton bTest = new FlatButton(L.T("Envoyer un test"));
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
                bTest.Enabled = false; bTest.Text = L.T("Envoi…");
                Remote.Test(app.S, delegate (string r) { bTest.Enabled = true; bTest.Text = L.T("Envoyer un test"); MessageBox.Show(this, r, L.T("Relais — alertes")); });
            };
            Label remHint = Hint(L.T("Discord : sur ton serveur, Paramètres du salon > Intégrations > Webhooks > Nouveau, puis copie l'URL. ntfy : installe l'appli ntfy, abonne-toi à un sujet unique (ex. relais-loic-8k2f) et écris-le ici."));
            remHint.Height = Theme.S(52);

            // --- thème
            TableLayoutPanel th = Grid(2, 1, 32);
            tLight = AddToggle(th, 0, 0, L.T("Thème clair"));
            tLight.CheckedChanged += delegate
            {
                if (loading) return;
                app.S.LightTheme = tLight.Checked; app.S.Save();
                if (MessageBox.Show(this, L.T("Redémarrer Relais maintenant pour appliquer le thème ?"), "Relais", MessageBoxButtons.YesNo) == DialogResult.Yes) app.Restart();
            };

            // --- mises à jour
            tbRepo = UiKit.Input();
            FlatButton bCheck = new FlatButton(L.T("Vérifier"));
            Panel repoRow = LabeledInput(L.T("Dépôt GitHub"), tbRepo, bCheck);
            tbRepo.Leave += delegate { app.S.UpdateRepo = tbRepo.Text.Trim(); app.S.Save(); };
            bCheck.Click += delegate { app.S.UpdateRepo = tbRepo.Text.Trim(); app.S.Save(); Updater.CheckInBackground(app, true); };
            // --- sons
            TableLayoutPanel snd = Grid(2, 3, 32);
            tSnd = AddToggle(snd, 0, 0, L.T("Sons de Relais"));
            tSndTurn = AddToggle(snd, 1, 0, L.T("Carillon de tour"));
            tSndTimer = AddToggle(snd, 0, 1, L.T("Cloche des minuteurs"));
            tSndDeco = AddToggle(snd, 1, 1, L.T("Alerte de déconnexion"));
            tSndSwitch = AddToggle(snd, 0, 2, L.T("Petit « tic » à chaque bascule"));
            FlatButton bListen = new FlatButton(L.T("Écouter"));
            bListen.Dock = DockStyle.Fill; bListen.Margin = new Padding(0, Theme.S(2), Theme.S(12), Theme.S(2));
            snd.Controls.Add(bListen, 1, 2);
            int listenIdx = 0;
            bListen.Click += delegate
            {
                Sounds.Kind[] ks = { Sounds.Kind.Turn, Sounds.Kind.Timer, Sounds.Kind.Disconnect, Sounds.Kind.Switch };
                Sounds.PlayRaw(ks[listenIdx % ks.Length], app.S.SoundVolume);
                string[] nm = { L.T("Carillon de tour"), L.T("Cloche de minuteur"), L.T("Déconnexion"), L.T("Bascule") };
                app.Toast.ShowMessage("♪ " + nm[listenIdx % nm.Length], false, 1.2);
                listenIdx++;
            };
            sVolume = new Slider(L.T("Volume des sons"));
            sVolume.Minimum = 0; sVolume.Maximum = 100; sVolume.Height = Theme.S(34);
            sVolume.ValueChanged += delegate { if (!loading) app.S.SoundVolume = sVolume.Value; };
            sVolume.MouseUp += delegate { app.S.Save(); Sounds.PlayRaw(Sounds.Kind.Turn, app.S.SoundVolume); };
            tSnd.CheckedChanged += delegate { if (!loading) { app.S.SoundsEnabled = tSnd.Checked; app.S.Save(); } };
            tSndTurn.CheckedChanged += delegate { if (!loading) { app.S.SoundTurn = tSndTurn.Checked; app.S.Save(); } };
            tSndTimer.CheckedChanged += delegate { if (!loading) { app.S.SoundTimer = tSndTimer.Checked; app.S.Save(); } };
            tSndDeco.CheckedChanged += delegate { if (!loading) { app.S.SoundDisconnect = tSndDeco.Checked; app.S.Save(); } };
            tSndSwitch.CheckedChanged += delegate { if (!loading) { app.S.SoundSwitch = tSndSwitch.Checked; app.S.Save(); } };

            TableLayoutPanel upd = Grid(2, 1, 32);
            tUpdates = AddToggle(upd, 0, 0, L.T("Vérifier chaque jour"));
            tUpdates.CheckedChanged += delegate { if (!loading) { app.S.CheckUpdates = tUpdates.Checked; app.S.Save(); } };
            Label updHint = Hint(L.T("Ex. : tonpseudo/relais. Relais regarde la dernière « Release » du dépôt et se met à jour en un clic (voir COMPILER.md)."));
            updHint.Height = Theme.S(36);
            tAutoProfile.CheckedChanged += delegate { if (!loading) { app.S.AutoProfile = tAutoProfile.Checked; app.S.Save(); } };
            tHoverPrev.CheckedChanged += delegate { if (!loading) { app.S.BarHoverPreview = tHoverPrev.Checked; app.S.Save(); } };

            // --- v2.3 : combat & accueil
            TableLayoutPanel cmb = Grid(2, 1, 32);
            tCombat = AddToggle(cmb, 0, 0, L.T("Suivi des combats (tour, durée)"));
            tAlmanax = AddToggle(cmb, 1, 0, L.T("Almanax du jour sur l'accueil"));
            tCombat.CheckedChanged += delegate { if (!loading) { app.S.CombatTracking = tCombat.Checked; app.S.Save(); if (!tCombat.Checked) app.Combat.EndNow(); } };
            tAlmanax.CheckedChanged += delegate { if (!loading) { app.S.AlmanaxShow = tAlmanax.Checked; app.ProfileEdited(); } };
            Label cmbHint = Hint(L.T("Le combat est repéré grâce aux alertes de tour (fenêtres qui clignotent) : la mini-barre affiche le tour et le chrono, et un repère sous le perso qui joue. Bilan dans Stats."));
            cmbHint.Height = Theme.S(40);

            // --- écrans
            dLayoutScreen = new DropButton(); dPreviewScreen = new DropButton();
            Panel scrRow1 = LabeledControl(L.T("Mosaïque de la team"), dLayoutScreen);
            Panel scrRow2 = LabeledControl(L.T("Aperçus en direct"), dPreviewScreen);
            dLayoutScreen.SelectedIndexChanged += delegate { if (!loading) { app.S.LayoutScreen = ScreenAt(dLayoutScreen.SelectedIndex); app.S.Save(); } };
            dPreviewScreen.SelectedIndexChanged += delegate { if (!loading) { app.S.PreviewScreen = ScreenAt(dPreviewScreen.SelectedIndex); app.S.Save(); } };
            Label scrHint = Hint(L.T("Choisis un écran pour la mosaïque de tes fenêtres et un autre pour les aperçus : chaque bouton les y envoie en un clic. « Automatique » = l'écran de la fenêtre active."));
            scrHint.Height = Theme.S(40);

            // --- télécommande
            TableLayoutPanel ph = Grid(2, 1, 32);
            tPhone = AddToggle(ph, 0, 0, L.T("Télécommande sur le téléphone"));
            Panel phBtns = new Panel(); phBtns.Dock = DockStyle.Fill; phBtns.Margin = new Padding(0);
            FlatButton bQr = new FlatButton("QR code"); FlatButton bPin = new FlatButton(L.T("Nouveau code"));
            phBtns.Controls.Add(bQr); phBtns.Controls.Add(bPin);
            phBtns.Resize += delegate
            {
                int w = (phBtns.Width - Theme.S(18)) / 2;
                bQr.SetBounds(0, Theme.S(1), w, Theme.S(30)); bPin.SetBounds(w + Theme.S(6), Theme.S(1), w, Theme.S(30));
            };
            ph.Controls.Add(phBtns, 1, 0);
            lPhone = Hint("");
            lPhone.Height = Theme.S(40);
            tPhone.CheckedChanged += delegate { if (!loading) { app.SetPhone(tPhone.Checked); PhoneText(); if (tPhone.Checked) ShowQr(); } };
            bQr.Click += delegate { if (!app.S.PhoneRemote) { app.SetPhone(true); LoadAll(); } ShowQr(); };
            bPin.Click += delegate
            {
                app.S.PhonePin = new Random().Next(100000, 999999).ToString(); app.S.Save(); PhoneText();
                app.Toast.ShowMessage(L.T("Nouveau code : ") + app.S.PhonePin, false, 3);
            };
            Label phHint = Hint(L.T("Ouvre l'adresse (ou scanne le QR code) avec le téléphone connecté au même Wi-Fi. Au premier lancement, Windows demande d'autoriser Relais sur le réseau : accepte pour les réseaux privés."));
            phHint.Height = Theme.S(40);

            // --- synchro cloud
            tbToken = UiKit.Input(); tbToken.UseSystemPasswordChar = true;
            Panel tokRow = LabeledInput(L.T("Jeton GitHub"), tbToken);
            tbToken.Leave += delegate { app.S.GistToken = tbToken.Text.Trim(); app.S.Save(); };
            TableLayoutPanel sy = Grid(2, 1, 32);
            tAutoSync = AddToggle(sy, 0, 0, L.T("Sauvegarde automatique"));
            tAutoSync.CheckedChanged += delegate { if (!loading) { app.S.AutoSync = tAutoSync.Checked; app.S.Save(); } };
            Panel syBtns = new Panel(); syBtns.Dock = DockStyle.Fill; syBtns.Margin = new Padding(0);
            FlatButton bUp = new FlatButton(L.T("Envoyer")); FlatButton bDown = new FlatButton(L.T("Récupérer"));
            syBtns.Controls.Add(bUp); syBtns.Controls.Add(bDown);
            syBtns.Resize += delegate
            {
                int w = (syBtns.Width - Theme.S(18)) / 2;
                bUp.SetBounds(0, Theme.S(1), w, Theme.S(30)); bDown.SetBounds(w + Theme.S(6), Theme.S(1), w, Theme.S(30));
            };
            sy.Controls.Add(syBtns, 1, 0);
            lSync = Hint("");
            lSync.Height = Theme.S(24);
            bUp.Click += delegate
            {
                app.S.GistToken = tbToken.Text.Trim(); app.S.Save();
                bUp.Enabled = false; lSync.Text = L.T("Envoi en cours…");
                app.CloudUpload(delegate (string msg, bool ok) { bUp.Enabled = true; lSync.Text = msg; if (!ok) MessageBox.Show(this, msg, L.T("Relais — sauvegarde")); });
            };
            bDown.Click += delegate
            {
                app.S.GistToken = tbToken.Text.Trim(); app.S.Save();
                if (MessageBox.Show(this, L.T("Remplacer tous les réglages de ce PC par la sauvegarde en ligne ?\nRelais redémarrera ensuite."), L.T("Relais — récupérer"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                bDown.Enabled = false; lSync.Text = L.T("Téléchargement…");
                app.CloudDownload(delegate (string msg, bool ok)
                {
                    bDown.Enabled = true; lSync.Text = msg;
                    if (ok) { MessageBox.Show(this, msg + L.T("\nRelais va redémarrer."), "Relais"); app.Restart(); }
                    else MessageBox.Show(this, msg, L.T("Relais — récupérer"));
                });
            };
            Label syHint = Hint(L.T("Tes réglages vont dans un Gist secret de TON compte GitHub. Jeton : github.com > Settings > Developer settings > Personal access tokens (classic) > Generate, coche seulement « gist ». Sur un autre PC : colle le même jeton puis « Récupérer »."));
            syHint.Height = Theme.S(52);

            // --- langue
            dLang = new DropButton();
            dLang.Items.Add("Français"); dLang.Items.Add("English");
            Panel langRow = LabeledControl("Langue / Language", dLang);
            dLang.SelectedIndexChanged += delegate
            {
                if (loading) return;
                string l = dLang.SelectedIndex == 1 ? "en" : "fr";
                if (l == app.S.Language) return;
                app.S.Language = l; app.S.Save();
                if (MessageBox.Show(this, l == "en" ? "Restart Relais now to switch to English?" : L.T("Redémarrer Relais maintenant pour passer en français ?"), "Relais", MessageBoxButtons.YesNo) == DialogResult.Yes) app.Restart();
            };

            // --- soutenir
            Panel sup = new Panel(); sup.Height = Theme.S(46);
            FlatButton bSupport = new FlatButton(L.T("♥  Soutenir Relais")); bSupport.Primary = true;
            FlatButton bSite = new FlatButton(L.T("Page de Relais"));
            sup.Controls.Add(bSupport); sup.Controls.Add(bSite);
            sup.Resize += delegate
            {
                int w = (sup.Width - Theme.S(8)) / 2;
                bSupport.SetBounds(0, Theme.S(6), w, Theme.S(34)); bSite.SetBounds(w + Theme.S(8), Theme.S(6), sup.Width - w - Theme.S(8), Theme.S(34));
            };
            bSupport.Click += delegate { Links.OpenSupport(app); };
            bSite.Click += delegate { Links.OpenSite(app); };
            Label supHint = Hint(L.T("Relais est gratuit et le restera. Si l'outil t'aide au quotidien, un petit don ou une étoile sur GitHub fait vraiment plaisir — et en parler à ta guilde aussi !"));
            supHint.Height = Theme.S(40);

            StackTop(page,
                SectionTitle(L.T("Soutenir Relais")), sup, supHint,
                SectionTitle(L.T("Comportement")), behav, turnHint,
                SectionTitle(L.T("Combats & accueil")), cmb, cmbHint,
                SectionTitle(L.T("Performance & son")), perf, perfHint,
                SectionTitle(L.T("Mini-barre")), bar, sOpacity,
                SectionTitle(L.T("Vignettes en jeu")), ov, ovHint,
                SectionTitle(L.T("Alertes sur le téléphone")), rowD, rowN, rem, awayRow, remHint,
                SectionTitle(L.T("Apparence")), accent, th,
                SectionTitle(L.T("Sons")), snd, sVolume,
                SectionTitle(L.T("Écrans")), scrRow1, scrRow2, scrHint,
                SectionTitle(L.T("Télécommande (téléphone)")), ph, lPhone, phHint,
                SectionTitle(L.T("Sauvegarde en ligne")), tokRow, sy, lSync, syHint,
                SectionTitle(L.T("Langue")), langRow,
                SectionTitle(L.T("Mises à jour")), repoRow, upd, updHint,
                SectionTitle(L.T("Application")), appg, btns,
                SectionTitle(L.T("Dépannage")), btns2);
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
                    MessageBox.Show(this, L.T("Ce raccourci est déjà utilisé par « ") + owner + " ».", "Relais", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
                case "palette": return app.S.KeyPalette;
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
                case "palette": app.S.KeyPalette = v; break;
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
                        MessageBox.Show(this, L.T("Ce raccourci est déjà utilisé par « ") + owner + " ».", "Relais", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
            tSnd.Checked = app.S.SoundsEnabled;
            tSndTurn.Checked = app.S.SoundTurn;
            tSndTimer.Checked = app.S.SoundTimer;
            tSndDeco.Checked = app.S.SoundDisconnect;
            tSndSwitch.Checked = app.S.SoundSwitch;
            sVolume.Value = app.S.SoundVolume;
            if (!tbDiscord.Focused) tbDiscord.Text = app.S.DiscordWebhook ?? "";
            if (!tbNtfy.Focused) tbNtfy.Text = app.S.NtfyTopic ?? "";
            if (!tbRepo.Focused) tbRepo.Text = app.S.UpdateRepo ?? "";
            tWheel.Checked = app.S.WheelCycle;
            tCombat.Checked = app.S.CombatTracking;
            tAlmanax.Checked = app.S.AlmanaxShow;
            tPhone.Checked = app.S.PhoneRemote;
            tAutoSync.Checked = app.S.AutoSync;
            FillScreens(dLayoutScreen, app.S.LayoutScreen);
            FillScreens(dPreviewScreen, app.S.PreviewScreen);
            dLang.SelectedIndex = app.S.Language == "en" ? 1 : 0;
            if (!tbToken.Focused) tbToken.Text = app.S.GistToken ?? "";
            lSync.Text = string.IsNullOrEmpty(app.S.LastSync) ? L.T("Jamais synchronisé.") : L.T("Dernière synchro : ") + app.S.LastSync;
            PhoneText();
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
                ? L.T("Aucune fenêtre Dofus connectée détectée — connecte tes persos, ils apparaîtront ici.")
                : online + L.T(" perso") + (online > 1 ? "s" : "") + L.T(" connecté") + (online > 1 ? "s" : "") + " · " + inRot + L.T(" dans la rotation · profil « ") + app.S.GetCurrent().Name + " »";
            if (app.Paused) s = L.T("EN PAUSE · ") + s;
            status.Text = s;
            status.ForeColor = app.Paused ? Theme.Accent : Theme.Muted;
            pauseBtn.Text = app.Paused ? L.T("Reprendre") : "Pause";
            pauseBtn.Primary = app.Paused;
            pauseBtn.Invalidate();
        }

        void NewProfile()
        {
            string n = Prompt.Ask(this, L.T("Nouveau profil"), L.T("Nom du profil (les persos connectés y seront ajoutés) :"), L.T("Team ") + (app.S.Profiles.Count + 1));
            if (n == null) return;
            if (FindProfile(n) != null) { MessageBox.Show(this, L.T("Ce nom existe déjà."), "Relais"); return; }
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
            string n = Prompt.Ask(this, L.T("Renommer le profil"), L.T("Nouveau nom :"), p.Name);
            if (n == null || n == p.Name) return;
            if (FindProfile(n) != null) { MessageBox.Show(this, L.T("Ce nom existe déjà."), "Relais"); return; }
            p.Name = n;
            app.S.CurrentProfile = n;
            app.ProfileEdited();
        }

        void DeleteProfile()
        {
            Profile p = app.S.GetCurrent();
            if (MessageBox.Show(this, L.T("Supprimer le profil « ") + p.Name + " » ?", "Relais", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
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
            string[] n = { L.T("haut gauche"), L.T("haut droite"), L.T("bas gauche"), L.T("bas droite") };
            bCorner.Text = L.T("Coin : ") + n[Math.Max(0, Math.Min(3, app.S.OverlayCorner))];
            bCorner.Invalidate();
        }

        static Panel LabeledControl(string label, Control c)
        {
            Panel row = new Panel();
            row.Height = Theme.S(40);
            Label l = new Label();
            l.UseMnemonic = false;
            l.Text = label; l.ForeColor = Theme.Muted; l.TextAlign = ContentAlignment.MiddleLeft;
            row.Controls.Add(l); row.Controls.Add(c);
            row.Resize += delegate
            {
                int lw = Theme.S(180);
                l.SetBounds(0, 0, lw, row.Height);
                c.SetBounds(lw, Theme.S(4), Math.Max(Theme.S(120), Math.Min(Theme.S(380), row.Width - lw - Theme.S(12))), Theme.S(32));
            };
            return row;
        }

        static string ScreenAt(int index)
        {
            Screen[] all = Screen.AllScreens;
            return index >= 1 && index <= all.Length ? all[index - 1].DeviceName : "";
        }

        void FillScreens(DropButton d, string current)
        {
            d.Items.Clear();
            d.Items.Add(L.T("Automatique"));
            int sel = 0;
            Screen[] all = Screen.AllScreens;
            for (int i = 0; i < all.Length; i++)
            {
                d.Items.Add(App.ScreenLabel(all[i]));
                if (all[i].DeviceName == current) sel = i + 1;
            }
            d.SelectedIndex = sel;
        }

        void PhoneText()
        {
            if (!app.S.PhoneRemote) { lPhone.Text = L.T("Désactivée. Code actuel : ") + app.S.PhonePin; return; }
            string err = app.Phone != null ? app.Phone.Error : null;
            lPhone.Text = err != null ? L.T("Erreur : ") + err : L.T("Adresse : ") + app.Phone.Url + L.T("      Code : ") + app.S.PhonePin;
        }

        void ShowQr()
        {
            if (app.Phone == null || !app.Phone.Running) { PhoneText(); return; }
            string url = app.Phone.Url;
            using (Form f = new Form())
            {
                f.Text = L.T("Relais — télécommande");
                f.FormBorderStyle = FormBorderStyle.FixedToolWindow;
                f.StartPosition = FormStartPosition.CenterParent;
                f.BackColor = Theme.Bg; f.ForeColor = Theme.Text; f.Font = Theme.Normal;
                PictureBox pb = new PictureBox();
                try { pb.Image = Qr.Render(url, Math.Max(4, Theme.S(6))); } catch { }
                pb.SizeMode = PictureBoxSizeMode.AutoSize;
                pb.Location = new Point(Theme.S(20), Theme.S(20));
                f.Controls.Add(pb);
                Label l = new Label();
                l.UseMnemonic = false;
                l.Text = L.T("Scanne avec l'appareil photo du téléphone (même Wi-Fi).\n") + url + L.T("\nCode : ") + app.S.PhonePin;
                l.ForeColor = Theme.Muted; l.AutoSize = false; l.TextAlign = ContentAlignment.TopCenter;
                int w = Math.Max(pb.Width, Theme.S(320));
                pb.Left = Theme.S(20) + (w - pb.Width) / 2;
                l.SetBounds(Theme.S(20), pb.Bottom + Theme.S(10), w, Theme.S(64));
                f.Controls.Add(l);
                f.ClientSize = new Size(w + Theme.S(40), l.Bottom + Theme.S(16));
                f.ShowDialog(this);
            }
        }

        static Panel LabeledInput(string label, TextBox tb, params FlatButton[] buttons)
        {
            Panel row = new Panel();
            row.Height = Theme.S(38);
            Label l = new Label();
            l.Text = label; l.ForeColor = Theme.Muted; l.TextAlign = ContentAlignment.MiddleLeft;
            InputFrame frame = new InputFrame(tb);
            row.Controls.Add(l); row.Controls.Add(frame);
            foreach (FlatButton b in buttons) row.Controls.Add(b);
            row.Resize += delegate
            {
                int lw = Theme.S(140), x = row.Width;
                for (int i = buttons.Length - 1; i >= 0; i--) { x -= Theme.S(100); buttons[i].SetBounds(x, Theme.S(3), Theme.S(100), Theme.S(30)); x -= Theme.S(6); }
                l.SetBounds(0, 0, lw, row.Height);
                frame.SetBounds(lw, Theme.S(3), Math.Max(Theme.S(60), x - lw), Theme.S(31));
            };
            return row;
        }

        void ShareMenu(Control anchor)
        {
            ContextMenuStrip cm = new ContextMenuStrip();
            cm.Items.Add(L.T("Copier le code de « ") + app.S.GetCurrent().Name + " »", null, delegate
            {
                string code = app.ExportCode(app.S.GetCurrent());
                if (App.CopyText(code)) app.Toast.ShowMessage(L.T("Code copié — envoie-le à un ami ou colle-le sur ton autre PC"), false, 3);
            });
            cm.Items.Add(L.T("Importer un code…"), null, delegate
            {
                string code = Prompt.Ask(this, L.T("Importer une team"), L.T("Colle le code (RELAIS2-…) :"), "");
                if (code == null) return;
                try
                {
                    string n = app.ImportCode(code);
                    app.Toast.ShowMessage(L.T("Profil « ") + n + L.T(" » importé"), false);
                }
                catch (Exception ex) { MessageBox.Show(this, L.T("Code invalide : ") + ex.Message, "Relais"); }
            });
            cm.Show(anchor, new Point(0, anchor.Height));
        }

        void ShowDiagnostic()
        {
            string text = app.Diagnostic();
            Program.Log("Diagnostic :\r\n" + text);
            using (Form f = new Form())
            {
                f.Text = L.T("Relais — diagnostic des fenêtres");
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
                FlatButton copy = new FlatButton(L.T("Copier")); copy.Primary = true;
                copy.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
                copy.SetBounds(Theme.S(486), Theme.S(370), Theme.S(120), Theme.S(34));
                copy.Click += delegate { if (App.CopyText(text)) app.Toast.ShowMessage(L.T("Diagnostic copié — colle-le à Claude"), false); };
                Label hint = new Label();
                hint.Text = L.T("« PLEIN ÉCRAN » : passe Dofus en mode fenêtré. « admin : OUI » sur Dofus mais pas sur Relais : relance Relais en admin.");
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
                d.Title = L.T("Exporter les profils");
                d.Filter = L.T("Profils Relais (*.json)|*.json");
                d.FileName = "relais-profils.json";
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    File.WriteAllText(d.FileName, new JavaScriptSerializer().Serialize(app.S.Profiles), System.Text.Encoding.UTF8);
                    app.Toast.ShowMessage(app.S.Profiles.Count + L.T(" profil(s) exporté(s)"), false);
                }
                catch (Exception ex) { MessageBox.Show(this, L.T("Export impossible : ") + ex.Message, "Relais"); }
            }
        }

        void ImportProfiles()
        {
            using (OpenFileDialog d = new OpenFileDialog())
            {
                d.Title = L.T("Importer des profils");
                d.Filter = L.T("Profils Relais (*.json)|*.json|Tous les fichiers|*.*");
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
                    app.Toast.ShowMessage(n + L.T(" profil(s) importé(s)"), false);
                }
                catch (Exception ex) { MessageBox.Show(this, L.T("Fichier illisible : ") + ex.Message, "Relais"); }
            }
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible) { list.Invalidate(); UpdateStatus(); }
        }

        public void GoTo(int page)
        {
            if (page >= 0 && page < pages.Length) tabs.Selected = page;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (app.HandleCaptureKey(keyData)) return true;
            if (keyData == (Keys.Control | Keys.K)) { app.OpenPalette(); return true; }
            Keys k = keyData & Keys.KeyCode;
            if ((keyData & Keys.Modifiers) == Keys.Control && k >= Keys.D1 && k <= Keys.D7) { GoTo(k - Keys.D1); return true; }
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

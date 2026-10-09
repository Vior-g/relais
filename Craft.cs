using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace Relais
{
    static class UiKit
    {
        public static TextBox Input()
        {
            TextBox t = new TextBox();
            t.BorderStyle = BorderStyle.FixedSingle;
            t.BackColor = Theme.Panel2;
            t.ForeColor = Theme.Text;
            t.Font = Theme.Normal;
            return t;
        }

        public static Label Title(string t)
        {
            return new OrnamentTitle(t);
        }

        public static Label Hint(string t, int h)
        {
            Label l = new Label();
            l.UseMnemonic = false;
            l.Text = t; l.ForeColor = Theme.Faint; l.Font = Theme.Small; l.AutoSize = false; l.Height = Theme.S(h);
            return l;
        }

        public static Panel Space(int h) { Panel p = new Panel(); p.Height = Theme.S(h); return p; }

        public static void StackTop(Control parent, params Control[] items)
        {
            for (int i = items.Length - 1; i >= 0; i--) { items[i].Dock = DockStyle.Top; parent.Controls.Add(items[i]); }
        }

        /// <summary>Ligne « champ texte + bouton » qui s'adapte à la largeur.</summary>
        public static Panel InputRow(TextBox tb, params FlatButton[] buttons)
        {
            Panel row = new Panel();
            row.Height = Theme.S(40);
            InputFrame frame = new InputFrame(tb);
            row.Controls.Add(frame);
            foreach (FlatButton b in buttons) row.Controls.Add(b);
            row.Resize += delegate
            {
                int x = row.Width, g = Theme.S(6);
                for (int i = buttons.Length - 1; i >= 0; i--)
                {
                    int bw = Math.Max(Theme.S(90), TextRenderer.MeasureText(buttons[i].Text, Theme.Normal).Width + Theme.S(24));
                    x -= bw;
                    buttons[i].SetBounds(x, Theme.S(4), bw, Theme.S(30));
                    x -= g;
                }
                frame.SetBounds(0, Theme.S(4), Math.Max(Theme.S(60), x), Theme.S(30));
            };
            return row;
        }
    }

    /// <summary>Liste à cocher (objectifs, quotidien, donjons…).</summary>
    public sealed class Checklist : Panel
    {
        readonly List<CheckItem> items;
        readonly RowList list = new RowList();
        public event EventHandler Changed;

        public Checklist(List<CheckItem> items, string placeholder, string empty)
        {
            this.items = items;
            BackColor = Theme.Bg;
            TextBox tb = UiKit.Input();
            FlatButton add = new FlatButton(L.T("Ajouter")); add.Primary = true;
            Panel row = UiKit.InputRow(tb, add);
            EventHandler doAdd = delegate
            {
                string t = tb.Text.Trim();
                if (t.Length == 0) return;
                CheckItem c = new CheckItem(); c.Text = t;
                items.Add(c);
                tb.Text = "";
                Fire();
            };
            add.Click += doAdd;
            tb.KeyDown += delegate (object s, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; doAdd(s, e); } };
            if (!string.IsNullOrEmpty(placeholder)) tb.Text = "";

            list.Empty = empty;
            list.CountF = delegate { return items.Count; };
            list.Title = delegate (int i) { return (items[i].Done ? "✓  " : "") + items[i].Text; };
            list.Sub = delegate (int i) { return items[i].Done ? L.T("fait") : L.T("à faire"); };
            list.Highlight = delegate (int i) { return false; };
            list.Buttons = delegate (int i) { return new string[] { items[i].Done ? L.T("Décocher") : L.T("Fait"), "✕" }; };
            list.OnButton = delegate (int i, int b)
            {
                if (b == 0) items[i].Done = !items[i].Done;
                else items.RemoveAt(i);
                Fire();
            };
            Label ph = UiKit.Hint(placeholder ?? "", 20);
            UiKit.StackTop(this, row, ph, list);
            Relayout();
        }

        void Fire()
        {
            Relayout();
            if (Changed != null) Changed(this, EventArgs.Empty);
        }

        public void Relayout()
        {
            list.Height = list.WantedHeight;
            Height = Theme.S(40) + Theme.S(20) + list.Height + Theme.S(6);
            list.Invalidate();
        }
    }

    /// <summary>Fiche d'un personnage : niveau, métiers, stuff, objectifs, quotidien, donjons.</summary>
    public sealed class SheetForm : Form
    {
        readonly App app;
        readonly string name;
        readonly CharSheet sheet;

        public SheetForm(App app, string name)
        {
            this.app = app;
            this.name = name;
            sheet = app.S.SheetOf(name);
            // remise à zéro du quotidien
            if (sheet.DailyDate != App.Today)
            {
                foreach (CheckItem c in sheet.Daily) c.Done = false;
                sheet.DailyDate = App.Today;
            }
            Text = L.T("Fiche — ") + name;
            Chrome.Hook(this);
            Icon = app.AppIcon;
            BackColor = Theme.Bg; ForeColor = Theme.Text; Font = Theme.Normal;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(Theme.S(560), Theme.S(640));
            MinimumSize = new Size(Theme.S(460), Theme.S(420));
            ShowInTaskbar = false;

            // en-tête
            Panel head = new Panel();
            head.Height = Theme.S(64);
            head.Paint += delegate (object s, PaintEventArgs e)
            {
                Theme.Hq(e.Graphics);
                GameWindow w = app.WindowOf(name);
                Theme.Avatar(e.Graphics, new RectangleF(0, Theme.S(10), Theme.S(44), Theme.S(44)), name, w != null ? w.Class : "", false);
                Theme.DrawText(e.Graphics, name, Theme.Title, Theme.Text, new Rectangle(Theme.S(56), Theme.S(8), head.Width - Theme.S(60), Theme.S(28)), TextFormatFlags.Left);
                Theme.DrawText(e.Graphics, w != null ? (w.Class.Length > 0 ? w.Class + L.T(" · connecté") : L.T("connecté")) : L.T("hors ligne"), Theme.Small, Theme.Muted,
                    new Rectangle(Theme.S(58), Theme.S(36), head.Width - Theme.S(60), Theme.S(18)), TextFormatFlags.Left);
            };

            // infos
            TableLayoutPanel info = new TableLayoutPanel();
            info.ColumnCount = 2; info.RowCount = 3;
            info.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Theme.S(110)));
            info.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 3; i++) info.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.S(36)));
            info.Height = Theme.S(108);
            NumericUpDown lvl = new NumericUpDown();
            lvl.Minimum = 0; lvl.Maximum = 200; lvl.Value = Math.Max(0, Math.Min(200, sheet.Level));
            lvl.BackColor = Theme.Panel2; lvl.ForeColor = Theme.Text; lvl.BorderStyle = BorderStyle.FixedSingle; lvl.Width = Theme.S(80);
            lvl.ValueChanged += delegate { sheet.Level = (int)lvl.Value; Save(); };
            TextBox jobs = UiKit.Input(); jobs.Text = sheet.Jobs ?? ""; jobs.Dock = DockStyle.Fill;
            jobs.TextChanged += delegate { sheet.Jobs = jobs.Text; };
            jobs.Leave += delegate { Save(); };
            Panel linkRow = new Panel(); linkRow.Dock = DockStyle.Fill;
            TextBox link = UiKit.Input(); link.Text = sheet.StuffLink ?? "";
            FlatButton open = new FlatButton(L.T("Ouvrir"));
            linkRow.Controls.Add(link); linkRow.Controls.Add(open);
            linkRow.Resize += delegate
            {
                open.SetBounds(linkRow.Width - Theme.S(80), Theme.S(2), Theme.S(80), Theme.S(28));
                link.SetBounds(0, Theme.S(4), linkRow.Width - Theme.S(88), Theme.S(26));
            };
            link.TextChanged += delegate { sheet.StuffLink = link.Text; };
            link.Leave += delegate { Save(); };
            open.Click += delegate
            {
                string u = (link.Text ?? "").Trim();
                if (u.Length == 0) return;
                if (!u.StartsWith("http", StringComparison.OrdinalIgnoreCase) && !System.IO.File.Exists(u)) u = "https://" + u;
                try { Process.Start(u); } catch (Exception ex) { MessageBox.Show(this, L.T("Impossible d'ouvrir : ") + ex.Message, "Relais"); }
            };
            info.Controls.Add(Lbl(L.T("Niveau")), 0, 0); info.Controls.Add(lvl, 1, 0);
            info.Controls.Add(Lbl(L.T("Métiers")), 0, 1); info.Controls.Add(jobs, 1, 1);
            info.Controls.Add(Lbl(L.T("Stuff (lien)")), 0, 2); info.Controls.Add(linkRow, 1, 2);

            // onglets
            TabStrip tabs = new TabStrip();
            tabs.Tabs.Add(L.T("Objectifs")); tabs.Tabs.Add(L.T("Quotidien")); tabs.Tabs.Add(L.T("Donjons"));

            Panel host = new Panel(); host.Dock = DockStyle.Fill; host.AutoScroll = true;
            Checklist goals = new Checklist(sheet.Goals, L.T("Ex. : Dofus Ocre, niveau 200, panoplie X…"), L.T("Aucun objectif pour l'instant."));
            Checklist daily = new Checklist(sheet.Daily, L.T("Ex. : Almanax, quête du jour, Kolizéum… (décoché chaque matin)"), L.T("Rien de prévu chaque jour."));
            Checklist dungeons = new Checklist(sheet.Dungeons, L.T("Ajoute un donjon, ou charge la liste complète depuis DofusDB :"), L.T("Aucun donjon suivi."));
            FlatButton load = new FlatButton(L.T("Charger les donjons depuis DofusDB"));
            Panel loadRow = new Panel(); loadRow.Height = Theme.S(44);
            loadRow.Controls.Add(load);
            loadRow.Resize += delegate { load.SetBounds(0, Theme.S(6), Math.Min(loadRow.Width, Theme.S(300)), Theme.S(32)); };
            Panel dWrap = new Panel(); dWrap.AutoSize = false;
            UiKit.StackTop(dWrap, loadRow, dungeons);
            dungeons.Resize += delegate { dWrap.Height = loadRow.Height + dungeons.Height; };
            dWrap.Height = loadRow.Height + dungeons.Height;
            load.Click += delegate
            {
                load.Enabled = false; load.Text = L.T("Chargement…");
                DofusDb.Run(delegate { return DofusDb.Dungeons(); }, delegate (List<DofusDb.Item> res, Exception err)
                {
                    load.Enabled = true; load.Text = L.T("Charger les donjons depuis DofusDB");
                    if (err != null || res == null) { MessageBox.Show(this, L.T("DofusDB ne répond pas : ") + (err != null ? err.Message : "?"), "Relais"); return; }
                    HashSet<string> have = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (CheckItem c in sheet.Dungeons) have.Add(c.Text);
                    int added = 0;
                    foreach (DofusDb.Item it in res)
                    {
                        string t = it.Name + (it.Level > 0 ? L.T(" (niv. ") + it.Level + ")" : "");
                        if (have.Contains(t)) continue;
                        CheckItem c = new CheckItem(); c.Text = t; sheet.Dungeons.Add(c); added++;
                    }
                    dungeons.Relayout();
                    Save();
                    app.Toast.ShowMessage(added + L.T(" donjon(s) ajouté(s)"), false);
                });
            };
            Control[] pages = { goals, daily, dWrap };
            foreach (Control c in pages) { c.Dock = DockStyle.Top; c.Visible = false; host.Controls.Add(c); }
            goals.Changed += delegate { Save(); };
            daily.Changed += delegate { Save(); };
            dungeons.Changed += delegate { Save(); };
            tabs.SelectedChanged += delegate { for (int i = 0; i < pages.Length; i++) pages[i].Visible = i == tabs.Selected; };
            pages[0].Visible = true;

            Panel body = new Panel();
            body.Dock = DockStyle.Fill;
            body.Padding = new Padding(Theme.S(18), Theme.S(4), Theme.S(18), Theme.S(12));
            body.Controls.Add(host);
            UiKit.StackTop(body, head, info, UiKit.Space(6), tabs, UiKit.Space(4));
            host.BringToFront();
            Controls.Add(body);
        }

        static Label Lbl(string t)
        {
            Label l = new Label();
            l.Text = t; l.ForeColor = Theme.Muted; l.Dock = DockStyle.Fill; l.TextAlign = ContentAlignment.MiddleLeft;
            return l;
        }

        void Save()
        {
            app.S.Save();
            app.NotifyProfileChanged();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            Save();
            base.OnFormClosing(e);
        }
    }

    /// <summary>Onglet « Craft » : chercher un objet sur DofusDB, récupérer la recette, répartir les ingrédients.</summary>
    public sealed class CraftPage : Panel
    {
        readonly App app;
        readonly DropButton projects = new DropButton();
        readonly RowList ingredients = new RowList();
        readonly ListBox results = new ListBox();
        readonly NumericUpDown count = new NumericUpDown();
        readonly Label progress;
        readonly FlatButton addProject;
        List<DofusDb.Item> found = new List<DofusDb.Item>();
        bool loading;

        CraftProject Current
        {
            get
            {
                int i = projects.SelectedIndex;
                return i >= 0 && i < app.S.Crafts.Count ? app.S.Crafts[i] : null;
            }
        }

        public CraftPage(App app)
        {
            this.app = app;
            Dock = DockStyle.Fill;
            BackColor = Theme.Bg;
            AutoScroll = true;
            Padding = new Padding(Theme.S(18), Theme.S(4), Theme.S(18), Theme.S(10));

            // recherche
            TextBox q = UiKit.Input();
            FlatButton search = new FlatButton(L.T("Chercher")); search.Primary = true;
            Panel searchRow = UiKit.InputRow(q, search);
            results.BorderStyle = BorderStyle.None;
            results.BackColor = Theme.Panel; results.ForeColor = Theme.Text; results.Font = Theme.Normal;
            results.IntegralHeight = false;
            results.Height = 0;
            Panel addRow = new Panel(); addRow.Height = 0;
            count.Minimum = 1; count.Maximum = 999; count.Value = 1;
            count.BackColor = Theme.Panel2; count.ForeColor = Theme.Text; count.BorderStyle = BorderStyle.FixedSingle;
            Label times = new Label(); times.Text = "exemplaire(s)"; times.ForeColor = Theme.Muted; times.TextAlign = ContentAlignment.MiddleLeft;
            addProject = new FlatButton(L.T("Ajouter au carnet")); addProject.Primary = true;
            addRow.Controls.Add(count); addRow.Controls.Add(times); addRow.Controls.Add(addProject);
            addRow.Resize += delegate
            {
                count.SetBounds(0, Theme.S(8), Theme.S(64), Theme.S(26));
                times.SetBounds(Theme.S(70), Theme.S(4), Theme.S(110), Theme.S(32));
                addProject.SetBounds(addRow.Width - Theme.S(170), Theme.S(4), Theme.S(170), Theme.S(32));
            };

            EventHandler doSearch = delegate
            {
                string text = q.Text.Trim();
                if (text.Length < 2) return;
                search.Enabled = false; search.Text = "…";
                DofusDb.Run(delegate { return DofusDb.Search(text); }, delegate (List<DofusDb.Item> res, Exception err)
                {
                    search.Enabled = true; search.Text = L.T("Chercher");
                    if (err != null) { MessageBox.Show(FindForm(), L.T("DofusDB ne répond pas : ") + err.Message, "Relais"); return; }
                    found = res ?? new List<DofusDb.Item>();
                    results.Items.Clear();
                    foreach (DofusDb.Item it in found) results.Items.Add(it);
                    if (found.Count == 0) results.Items.Add(L.T("Aucun résultat"));
                    results.Height = Theme.S(Math.Min(8, Math.Max(1, results.Items.Count)) * 22 + 6);
                    addRow.Height = found.Count > 0 ? Theme.S(42) : 0;
                    if (found.Count > 0) results.SelectedIndex = 0;
                });
            };
            search.Click += doSearch;
            q.KeyDown += delegate (object s, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; doSearch(s, e); } };

            addProject.Click += delegate
            {
                DofusDb.Item it = results.SelectedItem as DofusDb.Item;
                if (it == null) return;
                int n = (int)count.Value;
                addProject.Enabled = false; addProject.Text = L.T("Recette…");
                DofusDb.Run(delegate { return DofusDb.Recipe(it.Id); }, delegate (List<DofusDb.Ingredient> rec, Exception err)
                {
                    addProject.Enabled = true; addProject.Text = L.T("Ajouter au carnet");
                    if (err != null) { MessageBox.Show(FindForm(), L.T("DofusDB ne répond pas : ") + err.Message, "Relais"); return; }
                    if (rec == null || rec.Count == 0) { MessageBox.Show(FindForm(), it.Name + L.T(" ne se fabrique pas (pas de recette)."), "Relais"); return; }
                    CraftProject p = new CraftProject();
                    p.Name = it.Name; p.ItemId = it.Id; p.Count = n;
                    foreach (DofusDb.Ingredient g in rec)
                    {
                        CheckItem c = new CheckItem(); c.Text = g.Name; c.Qty = g.Qty * n;
                        p.Ingredients.Add(c);
                    }
                    app.S.Crafts.Add(p);
                    app.S.Save();
                    Reload(app.S.Crafts.Count - 1);
                    app.Toast.ShowMessage(L.T("Ajouté : ") + it.Name + L.T(" ×") + n + " (" + rec.Count + L.T(" ingrédients)"), false);
                });
            };

            // projets
            Panel projRow = new Panel(); projRow.Height = Theme.S(40);
            FlatButton manual = new FlatButton(L.T("+ Ingrédient"));
            FlatButton del = new FlatButton(L.T("Supprimer")); del.Danger = true;
            projRow.Controls.Add(projects); projRow.Controls.Add(manual); projRow.Controls.Add(del);
            projRow.Resize += delegate
            {
                int w = projRow.Width, bw = Theme.S(110), g = Theme.S(6);
                del.SetBounds(w - bw, Theme.S(4), bw, Theme.S(32));
                manual.SetBounds(w - 2 * bw - g, Theme.S(4), bw, Theme.S(32));
                projects.SetBounds(0, Theme.S(4), Math.Max(Theme.S(80), w - 2 * bw - 2 * g), Theme.S(32));
            };
            projects.SelectedIndexChanged += delegate { if (!loading) Refresh2(); };
            del.Click += delegate
            {
                CraftProject p = Current;
                if (p == null) return;
                if (MessageBox.Show(FindForm(), L.T("Supprimer « ") + p.Name + L.T(" » du carnet ?"), "Relais", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
                app.S.Crafts.Remove(p); app.S.Save(); Reload(0);
            };
            manual.Click += delegate
            {
                CraftProject p = Current;
                if (p == null)
                {
                    string pn = Prompt.Ask(FindForm(), L.T("Nouveau projet"), L.T("Nom du projet (ex. Stuff Kaelis) :"), L.T("Projet"));
                    if (pn == null) return;
                    p = new CraftProject(); p.Name = pn; app.S.Crafts.Add(p);
                }
                string t = Prompt.Ask(FindForm(), L.T("Ingrédient"), L.T("Nom de l'ingrédient (tu peux mettre « 10 Blé ») :"), "");
                if (t == null) return;
                CheckItem c = new CheckItem(); c.Qty = 1; c.Text = t;
                string[] parts = t.Split(new char[] { ' ' }, 2);
                int qn;
                if (parts.Length == 2 && int.TryParse(parts[0].TrimEnd('x', 'X', '×'), out qn)) { c.Qty = qn; c.Text = parts[1]; }
                p.Ingredients.Add(c);
                app.S.Save();
                Reload(app.S.Crafts.IndexOf(p));
            };

            progress = new Label(); progress.Height = Theme.S(26); progress.ForeColor = Theme.Muted; progress.Font = Theme.Small;
            progress.TextAlign = ContentAlignment.MiddleLeft;

            ingredients.Empty = L.T("Cherche un objet ci-dessus puis « Ajouter au carnet » : sa recette apparaîtra ici, à répartir entre tes persos.");
            ingredients.CountF = delegate { CraftProject p = Current; return p == null ? 0 : p.Ingredients.Count; };
            ingredients.Title = delegate (int i) { CheckItem c = Current.Ingredients[i]; return (c.Done ? "✓  " : "") + c.Qty + " × " + c.Text; };
            ingredients.Sub = delegate (int i) { CheckItem c = Current.Ingredients[i]; return (c.Done ? L.T("prêt") : L.T("à réunir")) + " · " + (string.IsNullOrEmpty(c.Who) ? L.T("personne d'assigné") : L.T("par ") + c.Who); };
            ingredients.Highlight = delegate (int i) { return false; };
            ingredients.Buttons = delegate (int i) { CheckItem c = Current.Ingredients[i]; return new string[] { L.T("Qui ?"), c.Done ? L.T("Annuler") : L.T("Prêt"), "✕" }; };
            ingredients.OnButton = delegate (int i, int b)
            {
                CraftProject p = Current;
                CheckItem c = p.Ingredients[i];
                if (b == 0) { AssignMenu(c); return; }
                if (b == 1) c.Done = !c.Done;
                else p.Ingredients.RemoveAt(i);
                app.S.Save();
                Refresh2();
            };

            UiKit.StackTop(this,
                UiKit.Title(L.T("Chercher un objet (DofusDB)")), searchRow, results, addRow,
                UiKit.Title(L.T("Carnet de craft")), projRow, progress, ingredients,
                UiKit.Hint(L.T("Les recettes viennent de l'API publique DofusDB. « Qui ? » répartit la récolte entre tes persos ; tout reste sur ton PC."), 40));
            Reload(0);
            VisibleChanged += delegate { if (Visible) Reload(projects.SelectedIndex < 0 ? 0 : projects.SelectedIndex); };
        }

        void AssignMenu(CheckItem c)
        {
            ContextMenuStrip cm = new ContextMenuStrip();
            foreach (Member m in app.S.GetCurrent().Members)
            {
                string n = m.Name;
                ToolStripMenuItem it = new ToolStripMenuItem(n, null, delegate { c.Who = n; app.S.Save(); Refresh2(); });
                it.Checked = c.Who == n;
                cm.Items.Add(it);
            }
            cm.Items.Add(new ToolStripSeparator());
            cm.Items.Add(L.T("Personne"), null, delegate { c.Who = null; app.S.Save(); Refresh2(); });
            cm.Show(Cursor.Position);
        }

        void Reload(int select)
        {
            loading = true;
            projects.Items.Clear();
            foreach (CraftProject p in app.S.Crafts) projects.Items.Add(p.Name + (p.Count > 1 ? L.T(" ×") + p.Count : ""));
            if (projects.Items.Count > 0) projects.SelectedIndex = Math.Max(0, Math.Min(select, projects.Items.Count - 1));
            loading = false;
            Refresh2();
        }

        void Refresh2()
        {
            CraftProject p = Current;
            if (p == null) progress.Text = app.S.Crafts.Count == 0 ? L.T("Aucun projet.") : "";
            else
            {
                int done = 0;
                foreach (CheckItem c in p.Ingredients) if (c.Done) done++;
                progress.Text = done + " / " + p.Ingredients.Count + L.T(" ingrédients prêts") + (done == p.Ingredients.Count && done > 0 ? L.T(" — tout est prêt, au craft !") : "");
                progress.ForeColor = done == p.Ingredients.Count && done > 0 ? Theme.Green : Theme.Muted;
            }
            ingredients.Height = ingredients.WantedHeight;
            ingredients.Invalidate();
        }
    }
}

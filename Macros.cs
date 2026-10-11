using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace Relais
{
    /// <summary>
    /// Commandes de chat : Relais prépare le texte et le copie, tu le colles toi-même (Ctrl+V puis Entrée).
    /// Depuis Dofus 3.7, plusieurs commandes peuvent s'enchaîner avec « ; » sur une seule ligne.
    ///
    /// Jokers :
    ///   {chaque}  répète la commande pour chaque autre perso de la team (« /invite {chaque} » → « /invite B;/invite C »)
    ///   {chef}    le perso n°1        {perso}  le perso au premier plan        {team}  tous les persos, séparés par des virgules
    /// </summary>
    public static class Macros
    {
        public static List<Macro> Presets()
        {
            List<Macro> l = new List<Macro>();
            l.Add(Make("Inviter toute la team", "/invite {chaque}", true));
            l.Add(Make("Message privé au chef", "/w {chef} ", false));
            l.Add(Make("Présenter la team", "Team : {team}", false));
            return l;
        }

        static Macro Make(string name, string text, bool leader)
        {
            Macro m = new Macro();
            m.Name = name; m.Text = text; m.SwitchToLeader = leader;
            return m;
        }

        /// <summary>Le perso « émetteur » : le chef si on va sur lui, sinon le perso au premier plan.</summary>
        static GameWindow Self(App app, List<GameWindow> rot, bool leader)
        {
            if (rot.Count == 0) return null;
            if (leader) return rot[0];
            IntPtr fg = app.LastActive;
            foreach (GameWindow w in rot) if (w.Handle == fg) return w;
            return rot[0];
        }

        /// <summary>Remplace les jokers. Les commandes sont séparées par « ; ».</summary>
        public static string Expand(App app, string text, bool leader)
        {
            List<GameWindow> rot = app.Rotation();
            GameWindow self = Self(app, rot, leader);
            List<string> others = new List<string>();
            foreach (GameWindow w in rot) if (self == null || w.Handle != self.Handle) others.Add(w.Name);
            List<string> all = new List<string>();
            foreach (GameWindow w in rot) all.Add(w.Name);

            string t = (text ?? "").Replace("\r\n", ";").Replace("\n", ";");
            t = t.Replace("{chef}", rot.Count > 0 ? rot[0].Name : "")
                 .Replace("{perso}", self != null ? self.Name : "")
                 .Replace("{team}", string.Join(", ", all.ToArray()));
            if (t.Contains("{chaque}"))
            {
                List<string> parts = new List<string>();
                foreach (string cmd in t.Split(';'))
                {
                    if (!cmd.Contains("{chaque}")) { if (cmd.Trim().Length > 0) parts.Add(cmd.Trim()); continue; }
                    foreach (string o in others) parts.Add(cmd.Trim().Replace("{chaque}", o));
                }
                t = string.Join(";", parts.ToArray());
            }
            return t;
        }

        /// <summary>Lance une commande : va sur le chef si demandé, copie, et explique quoi faire.</summary>
        public static void Run(App app, Macro m)
        {
            List<GameWindow> rot = app.Rotation();
            if (m.Text != null && m.Text.Contains("{chaque}") && rot.Count < 2)
            {
                app.Toast.ShowMessage(L.T("Il faut au moins 2 persos connectés"), false);
                return;
            }
            string text = Expand(app, m.Text, m.SwitchToLeader);
            if (text.Length == 0) return;
            if (m.SwitchToLeader && rot.Count > 0) app.Activate(rot[0].Name);
            if (!App.CopyText(text)) return;
            string where = m.SwitchToLeader && rot.Count > 0 ? L.T(" dans le chat de ") + rot[0].Name : "";
            app.Toast.ShowMessage("« " + L.T(m.Name) + " » " + L.T("copié") + " — Ctrl+V" + where + L.T(" puis Entrée"), false, 4);
            app.Record("info", L.T("Commande copiée : ") + m.Name);
        }

        public static string Preview(App app, Macro m)
        {
            string t = Expand(app, m.Text, m.SwitchToLeader);
            return t.Length > 0 ? t : (m.Text ?? "");
        }
    }

    /// <summary>Création / modification d'une commande de chat.</summary>
    public sealed class MacroForm : Form
    {
        readonly App app;
        readonly TextBox tbName = new TextBox(), tbText = new TextBox();
        readonly HotkeyBox hk = new HotkeyBox();
        readonly Toggle tLeader = new Toggle(L.T("Aller sur le chef avant de copier"));
        readonly Label preview = new Label();
        public readonly Macro Result;

        public MacroForm(App app, Macro src)
        {
            this.app = app;
            Result = new Macro();
            if (src != null) { Result.Name = src.Name; Result.Text = src.Text; Result.Hotkey = src.Hotkey; Result.SwitchToLeader = src.SwitchToLeader; }
            Text = src == null ? L.T("Nouvelle commande") : L.T("Modifier la commande");
            Chrome.Hook(this);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.Bg; ForeColor = Theme.Text; Font = Theme.Normal;
            ClientSize = new Size(Theme.S(480), Theme.S(402));

            int x = Theme.S(16), w = Theme.S(448), y = Theme.S(12);
            Add(Lbl(L.T("Nom"), x, y, w)); y += Theme.S(22);
            tbName.Text = Result.Name ?? ""; Style(tbName); tbName.SetBounds(x, y, w, Theme.S(26)); Controls.Add(tbName); y += Theme.S(36);
            Add(Lbl(L.T("Texte (plusieurs commandes : une par ligne ou séparées par « ; »)"), x, y, w)); y += Theme.S(22);
            tbText.Multiline = true; tbText.AcceptsReturn = true; tbText.ScrollBars = ScrollBars.Vertical;
            tbText.Text = (Result.Text ?? "").Replace(";", "\r\n");
            Style(tbText); tbText.SetBounds(x, y, w, Theme.S(70)); Controls.Add(tbText); y += Theme.S(76);
            Label help = Lbl(L.T("Jokers : {chaque} = chaque autre perso · {chef} · {perso} = perso au premier plan · {team}"), x, y, w);
            help.ForeColor = Theme.Faint; help.Font = Theme.Small; help.Height = Theme.S(32); Add(help); y += Theme.S(34);
            preview.UseMnemonic = false; preview.ForeColor = Theme.AccentSoft; preview.Font = Theme.Small; preview.AutoSize = false;
            preview.SetBounds(x, y, w, Theme.S(36)); Controls.Add(preview); y += Theme.S(42);
            tLeader.Checked = Result.SwitchToLeader; tLeader.SetBounds(x, y, w, Theme.S(28)); Controls.Add(tLeader); y += Theme.S(36);
            Add(Lbl(L.T("Raccourci (facultatif)"), x, y + Theme.S(6), Theme.S(200)));
            hk.Value = Result.Hotkey;
            hk.SetBounds(x + Theme.S(210), y, Theme.S(238), Theme.S(32));
            hk.BeginCapture = delegate (HotkeyBox b) { app.Capture(b); };
            Controls.Add(hk); y += Theme.S(48);

            FlatButton ok = new FlatButton(L.T("Enregistrer")); ok.Primary = true;
            FlatButton cancel = new FlatButton(L.T("Annuler"));
            ok.SetBounds(Theme.S(364), y, Theme.S(100), Theme.S(32));
            cancel.SetBounds(Theme.S(256), y, Theme.S(100), Theme.S(32));
            Controls.Add(ok); Controls.Add(cancel);
            ok.Click += delegate
            {
                if (tbName.Text.Trim().Length == 0 || tbText.Text.Trim().Length == 0) { tbName.Focus(); return; }
                Result.Name = tbName.Text.Trim();
                Result.Text = Join(tbText.Text);
                Result.Hotkey = hk.Value;
                Result.SwitchToLeader = tLeader.Checked;
                DialogResult = DialogResult.OK;
            };
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; };
            tbText.TextChanged += delegate { UpdatePreview(); };
            tLeader.CheckedChanged += delegate { UpdatePreview(); };
            UpdatePreview();
        }

        static string Join(string multi)
        {
            List<string> parts = new List<string>();
            foreach (string line in multi.Replace("\r\n", "\n").Split('\n', ';'))
                if (line.Trim().Length > 0) parts.Add(line.Trim());
            return string.Join(";", parts.ToArray());
        }

        void UpdatePreview()
        {
            string t = Macros.Expand(app, Join(tbText.Text), tLeader.Checked);
            preview.Text = t.Length == 0 ? "" : L.T("Aperçu : ") + t;
        }

        void Add(Control c) { Controls.Add(c); }

        static Label Lbl(string t, int x, int y, int w)
        {
            Label l = new Label();
            l.UseMnemonic = false; l.Text = t; l.ForeColor = Theme.Muted; l.AutoSize = false;
            l.SetBounds(x, y, w, Theme.S(20));
            return l;
        }

        static void Style(TextBox tb)
        {
            tb.BorderStyle = BorderStyle.FixedSingle; tb.BackColor = Theme.Panel2; tb.ForeColor = Theme.Text;
        }
    }
}

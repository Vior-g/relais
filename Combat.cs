using System;
using System.Collections.Generic;

namespace Relais
{
    /// <summary>
    /// Suivi des combats à partir des seules alertes de tour (fenêtre qui clignote) :
    /// début / fin de combat, numéro de tour, durée et perso qui joue. Rien n'est lu dans le jeu.
    /// </summary>
    public sealed class CombatTracker
    {
        readonly App app;
        public bool Active { get; private set; }
        public DateTime Start { get; private set; }
        DateTime lastSignal;
        public int Round { get; private set; }
        public string Current { get; private set; }
        int lastIndex = -1;
        int turns;

        /// <summary>Délai sans alerte de tour après lequel le combat est considéré fini.</summary>
        public static int EndAfterSeconds = 75;

        public event EventHandler Changed;

        public CombatTracker(App app) { this.app = app; }

        public TimeSpan Elapsed { get { return Active ? DateTime.Now - Start : TimeSpan.Zero; } }

        /// <summary>Alerte de tour (ou bascule manuelle pendant un combat) pour ce perso.</summary>
        public void Signal(string name, bool fromFlash)
        {
            if (!app.S.CombatTracking) return;
            if (!Active && !fromFlash) return; // une bascule seule ne lance pas de combat
            List<GameWindow> rot = app.Rotation();
            int idx = -1;
            for (int i = 0; i < rot.Count; i++) if (string.Equals(rot[i].Name, name, StringComparison.OrdinalIgnoreCase)) idx = i;
            if (idx < 0) return;
            DateTime now = DateTime.Now;
            if (!Active)
            {
                Active = true; Start = now; Round = 1; lastIndex = idx; turns = 1;
                app.Record("combat", L.T("Combat commencé"));
            }
            else if (!string.Equals(Current, name, StringComparison.OrdinalIgnoreCase))
            {
                if (idx <= lastIndex) Round++;
                lastIndex = idx;
                turns++;
            }
            Current = name;
            lastSignal = now;
            Fire();
        }

        /// <summary>Appelé chaque seconde : termine le combat après un silence prolongé.</summary>
        public void Tick()
        {
            if (!Active) return;
            if ((DateTime.Now - lastSignal).TotalSeconds > EndAfterSeconds) End(lastSignal.AddSeconds(15));
            else Fire();
        }

        /// <summary>Fin manuelle (clic sur la frise).</summary>
        public void EndNow() { if (Active) End(DateTime.Now); }

        void End(DateTime end)
        {
            Active = false;
            double secs = Math.Max(1, (end - Start).TotalSeconds);
            string day = DateTime.Now.ToString("yyyy-MM-dd");
            CombatDay d;
            if (!app.S.Combat.TryGetValue(day, out d)) { d = new CombatDay(); app.S.Combat[day] = d; }
            d.Fights++;
            d.Turns += Round;
            d.Seconds += secs;
            if (secs > d.Longest) d.Longest = secs;
            // ne garde que ~1 an d'historique
            if (app.S.Combat.Count > 400)
            {
                List<string> keys = new List<string>(app.S.Combat.Keys);
                keys.Sort();
                for (int i = 0; i < keys.Count - 366; i++) app.S.Combat.Remove(keys[i]);
            }
            app.S.Save();
            app.Record("combat", L.T("Combat fini : ") + Round + L.T(" tour(s), ") + Fmt.Duration(secs));
            Current = null;
            Fire();
        }

        void Fire() { if (Changed != null) Changed(this, EventArgs.Empty); }

        /// <summary>Texte court pour la mini-barre : « Tour 3 · 2:14 ».</summary>
        public string Short()
        {
            TimeSpan t = Elapsed;
            return L.T("Tour ") + Round + " · " + ((int)t.TotalMinutes) + ":" + t.Seconds.ToString("00");
        }

        public CombatDay Today()
        {
            CombatDay d;
            return app.S.Combat.TryGetValue(DateTime.Now.ToString("yyyy-MM-dd"), out d) ? d : new CombatDay();
        }
    }
}

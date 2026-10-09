using System;
using System.Diagnostics;

namespace Relais
{
    /// <summary>
    /// Liens publics de Relais. Pour activer le bouton « Soutenir », mets ici l'adresse de ta page
    /// (Ko-fi, Tipeee, GitHub Sponsors…). Tant qu'elle est vide, le bouton ouvre la page GitHub du projet.
    /// </summary>
    public static class Links
    {
        /// <summary>Ex. : "https://ko-fi.com/tonpseudo"</summary>
        public const string Support = "";

        /// <summary>Dépôt officiel (sert aussi aux mises à jour si l'utilisateur n'en a pas réglé).</summary>
        public const string Repo = "Vior-g/relais";

        static string RepoOf(App app)
        {
            string r = app != null && !string.IsNullOrEmpty(app.S.UpdateRepo) ? app.S.UpdateRepo.Trim() : Repo;
            return r.Replace("https://github.com/", "").Trim('/');
        }

        /// <summary>Page de présentation (GitHub Pages) : https://pseudo.github.io/relais/</summary>
        public static string Site(App app)
        {
            string[] p = RepoOf(app).Split('/');
            return p.Length == 2 ? "https://" + p[0].ToLowerInvariant() + ".github.io/" + p[1] + "/" : "https://github.com/" + RepoOf(app);
        }

        public static void OpenSupport(App app)
        {
            Open(Support.Length > 0 ? Support : "https://github.com/" + RepoOf(app));
        }

        public static void OpenSite(App app) { Open(Site(app)); }

        public static void Open(string url)
        {
            try { Process.Start(url); } catch (Exception ex) { Program.Log("Lien : " + ex.Message); }
        }
    }
}

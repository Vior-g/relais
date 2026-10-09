using System;
using System.Collections.Generic;

namespace Relais
{
    /// <summary>
    /// Traduction de l'interface. Le texte français sert de clé ; en anglais, il est remplacé
    /// par sa traduction (LangEn.cs). Une phrase absente du dictionnaire reste en français.
    /// </summary>
    public static partial class L
    {
        /// <summary>Vrai si l'interface est en anglais (lu au démarrage, changement = redémarrage).</summary>
        public static bool En;

        static Dictionary<string, string> en;

        public static string T(string fr)
        {
            if (!En || fr == null) return fr;
            if (en == null) en = BuildEn();
            string r;
            return en.TryGetValue(fr, out r) ? r : fr;
        }
    }
}

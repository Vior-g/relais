using System;
using System.Collections.Generic;
using System.IO;
using System.Media;
using System.Threading;

namespace Relais
{
    /// <summary>
    /// Sons d'interface synthétisés à la volée (aucun fichier audio) : petites notes « aventure »,
    /// douces et courtes. Chaque son est généré une fois puis gardé en mémoire.
    /// </summary>
    public static class Sounds
    {
        public enum Kind { Switch, Turn, Timer, Disconnect, Toggle }

        static readonly Dictionary<string, byte[]> cache = new Dictionary<string, byte[]>();
        const int Rate = 22050;

        /// <summary>Note : fréquence (Hz), durée (s), départ (s), volume 0..1.</summary>
        struct Note { public double F, Dur, Start, Vol; public Note(double f, double d, double s, double v) { F = f; Dur = d; Start = s; Vol = v; } }

        static Note[] Recipe(Kind k)
        {
            switch (k)
            {
                case Kind.Switch:     // petit « tic » boisé
                    return new Note[] { new Note(880, 0.05, 0, 0.5), new Note(1320, 0.04, 0.012, 0.25) };
                case Kind.Turn:       // carillon montant (quinte + octave)
                    return new Note[] { new Note(659.3, 0.35, 0, 0.55), new Note(987.8, 0.35, 0.09, 0.45), new Note(1318.5, 0.5, 0.18, 0.4) };
                case Kind.Timer:      // cloche : deux coups
                    return new Note[] { new Note(784, 0.7, 0, 0.6), new Note(1568, 0.5, 0, 0.18), new Note(784, 0.7, 0.32, 0.5), new Note(1568, 0.5, 0.32, 0.15) };
                case Kind.Disconnect: // deux notes descendantes, graves
                    return new Note[] { new Note(392, 0.25, 0, 0.55), new Note(293.7, 0.4, 0.16, 0.55) };
                default:              // Toggle : clic doux
                    return new Note[] { new Note(1046.5, 0.04, 0, 0.35) };
            }
        }

        static byte[] Build(Kind k, int volume)
        {
            Note[] notes = Recipe(k);
            double total = 0;
            foreach (Note n in notes) total = Math.Max(total, n.Start + n.Dur);
            int samples = (int)(total * Rate) + 200;
            double[] buf = new double[samples];
            foreach (Note n in notes)
            {
                int s0 = (int)(n.Start * Rate), len = (int)(n.Dur * Rate);
                for (int i = 0; i < len && s0 + i < samples; i++)
                {
                    double t = i / (double)Rate;
                    double attack = Math.Min(1, t / 0.006);
                    double env = attack * Math.Exp(-t * (5.5 / n.Dur));
                    // fondamentale + harmoniques légères = timbre de clochette
                    double v = Math.Sin(2 * Math.PI * n.F * t) + 0.32 * Math.Sin(2 * Math.PI * n.F * 2.01 * t) + 0.12 * Math.Sin(2 * Math.PI * n.F * 3.02 * t);
                    buf[s0 + i] += v * env * n.Vol;
                }
            }
            double gain = Math.Max(0, Math.Min(100, volume)) / 100.0 * 0.42;
            using (MemoryStream ms = new MemoryStream())
            using (BinaryWriter w = new BinaryWriter(ms))
            {
                int dataLen = samples * 2;
                w.Write(new char[] { 'R', 'I', 'F', 'F' }); w.Write(36 + dataLen);
                w.Write(new char[] { 'W', 'A', 'V', 'E', 'f', 'm', 't', ' ' }); w.Write(16);
                w.Write((short)1); w.Write((short)1); w.Write(Rate); w.Write(Rate * 2); w.Write((short)2); w.Write((short)16);
                w.Write(new char[] { 'd', 'a', 't', 'a' }); w.Write(dataLen);
                for (int i = 0; i < samples; i++)
                {
                    double v = Math.Max(-1, Math.Min(1, buf[i] * gain));
                    w.Write((short)(v * 32000));
                }
                w.Flush();
                return ms.ToArray();
            }
        }

        public static void Play(Settings s, Kind k)
        {
            if (s == null || !s.SoundsEnabled) return;
            bool on = k == Kind.Switch ? s.SoundSwitch : k == Kind.Turn ? s.SoundTurn : k == Kind.Timer ? s.SoundTimer : k == Kind.Disconnect ? s.SoundDisconnect : true;
            if (!on) return;
            PlayRaw(k, s.SoundVolume);
        }

        public static void PlayRaw(Kind k, int volume)
        {
            string key = k + "@" + volume;
            byte[] data;
            lock (cache)
            {
                if (!cache.TryGetValue(key, out data)) { data = Build(k, volume); cache[key] = data; }
            }
            ThreadPool.QueueUserWorkItem(delegate
            {
                try { using (SoundPlayer p = new SoundPlayer(new MemoryStream(data))) p.PlaySync(); }
                catch { }
            });
        }
    }
}

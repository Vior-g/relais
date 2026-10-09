using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;

namespace Relais
{
    /// <summary>
    /// Petit générateur de QR code (mode octet, correction L, versions 1 à 5 : jusqu'à ~100 caractères).
    /// Suffit pour afficher l'adresse de la télécommande sans aucune bibliothèque.
    /// </summary>
    public static class Qr
    {
        static readonly int[] Total = { 0, 26, 44, 70, 100, 134 };
        static readonly int[] Ec = { 0, 7, 10, 15, 20, 26 };

        public static bool[,] Encode(string text)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            int ver = 0;
            for (int v = 1; v <= 5; v++) if ((Total[v] - Ec[v]) * 8 >= 4 + 8 + bytes.Length * 8) { ver = v; break; }
            if (ver == 0) throw new ArgumentException("Texte trop long pour le QR code");
            int dataCw = Total[ver] - Ec[ver];

            // --- flux de bits
            List<bool> bits = new List<bool>();
            Action<int, int> put = delegate (int val, int len) { for (int i = len - 1; i >= 0; i--) bits.Add(((val >> i) & 1) != 0); };
            put(4, 4);
            put(bytes.Length, 8);
            foreach (byte b in bytes) put(b, 8);
            int cap = dataCw * 8;
            put(0, Math.Min(4, cap - bits.Count));
            while (bits.Count % 8 != 0) bits.Add(false);
            for (int pad = 0xEC; bits.Count < cap; pad ^= 0xEC ^ 0x11) put(pad, 8);
            byte[] data = new byte[dataCw];
            for (int i = 0; i < bits.Count; i++) if (bits[i]) data[i >> 3] |= (byte)(1 << (7 - (i & 7)));

            byte[] ec = Remainder(data, Divisor(Ec[ver]));
            byte[] all = new byte[Total[ver]];
            Array.Copy(data, all, dataCw);
            Array.Copy(ec, 0, all, dataCw, ec.Length);

            // --- matrice
            int size = ver * 4 + 17;
            bool[,] m = new bool[size, size];      // [y, x]
            bool[,] fn = new bool[size, size];
            Action<int, int, bool> set = delegate (int x, int y, bool dark) { m[y, x] = dark; fn[y, x] = true; };

            for (int i = 0; i < size; i++) { set(6, i, i % 2 == 0); set(i, 6, i % 2 == 0); }
            int[][] finders = { new int[] { 3, 3 }, new int[] { size - 4, 3 }, new int[] { 3, size - 4 } };
            foreach (int[] f in finders)
                for (int dy = -4; dy <= 4; dy++)
                    for (int dx = -4; dx <= 4; dx++)
                    {
                        int x = f[0] + dx, y = f[1] + dy;
                        if (x < 0 || y < 0 || x >= size || y >= size) continue;
                        int d = Math.Max(Math.Abs(dx), Math.Abs(dy));
                        set(x, y, d != 2 && d != 4);
                    }
            if (ver >= 2)
            {
                int c = size - 7;
                for (int dy = -2; dy <= 2; dy++)
                    for (int dx = -2; dx <= 2; dx++)
                        set(c + dx, c + dy, Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1);
            }

            // format (correction L = 1, masque 0) — réserve les modules puis écrit
            int fdata = (1 << 3) | 0;
            int rem = fdata;
            for (int i = 0; i < 10; i++) rem = (rem << 1) ^ ((rem >> 9) * 0x537);
            int fbits = ((fdata << 10) | rem) ^ 0x5412;
            Func<int, bool> fb = delegate (int i) { return ((fbits >> i) & 1) != 0; };
            for (int i = 0; i <= 5; i++) set(8, i, fb(i));
            set(8, 7, fb(6)); set(8, 8, fb(7)); set(7, 8, fb(8));
            for (int i = 9; i < 15; i++) set(14 - i, 8, fb(i));
            for (int i = 0; i < 8; i++) set(size - 1 - i, 8, fb(i));
            for (int i = 8; i < 15; i++) set(8, size - 15 + i, fb(i));
            set(8, size - 8, true);

            // données en zigzag
            int bi = 0;
            for (int right = size - 1; right >= 1; right -= 2)
            {
                if (right == 6) right = 5;
                for (int vert = 0; vert < size; vert++)
                    for (int j = 0; j < 2; j++)
                    {
                        int x = right - j;
                        bool upward = ((right + 1) & 2) == 0;
                        int y = upward ? size - 1 - vert : vert;
                        if (fn[y, x]) continue;
                        if (bi < all.Length * 8) { m[y, x] = ((all[bi >> 3] >> (7 - (bi & 7))) & 1) != 0; bi++; }
                    }
            }
            // masque 0
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    if (!fn[y, x] && (x + y) % 2 == 0) m[y, x] = !m[y, x];
            return m;
        }

        static int Mul(int x, int y)
        {
            int z = 0;
            for (int i = 7; i >= 0; i--)
            {
                z = (z << 1) ^ ((z >> 7) * 0x11D);
                z ^= ((y >> i) & 1) * x;
            }
            return z & 0xFF;
        }

        static byte[] Divisor(int degree)
        {
            byte[] r = new byte[degree];
            r[degree - 1] = 1;
            int root = 1;
            for (int i = 0; i < degree; i++)
            {
                for (int j = 0; j < degree; j++)
                {
                    r[j] = (byte)Mul(r[j], root);
                    if (j + 1 < degree) r[j] ^= r[j + 1];
                }
                root = Mul(root, 0x02);
            }
            return r;
        }

        static byte[] Remainder(byte[] data, byte[] div)
        {
            byte[] r = new byte[div.Length];
            foreach (byte b in data)
            {
                int factor = b ^ r[0];
                Array.Copy(r, 1, r, 0, r.Length - 1);
                r[r.Length - 1] = 0;
                for (int i = 0; i < r.Length; i++) r[i] ^= (byte)Mul(div[i], factor);
            }
            return r;
        }

        /// <summary>Image du QR code (noir sur blanc, marge de 4 modules).</summary>
        public static Bitmap Render(string text, int scale)
        {
            bool[,] m = Encode(text);
            int n = m.GetLength(0), q = 4;
            Bitmap bmp = new Bitmap((n + 2 * q) * scale, (n + 2 * q) * scale);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.White);
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                        if (m[y, x]) g.FillRectangle(Brushes.Black, (x + q) * scale, (y + q) * scale, scale, scale);
            }
            return bmp;
        }
    }
}

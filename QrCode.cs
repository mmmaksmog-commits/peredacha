// Свой генератор QR-кода: байтовый режим, уровень коррекции M, версии 1–10 (до 213 байт).
// Перенесён один в один с эталона, проверенного декодером OpenCV на 75 строках всех версий.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Peredacha
{
    static class QrCode
    {
        // версия → EC на блок; группы: (блоков, данных в блоке)
        static readonly int[] EcLen = { 0, 10, 16, 26, 18, 24, 16, 18, 22, 22, 26 };
        static readonly int[][] Groups =
        {
            null,
            new[] { 1, 16 }, new[] { 1, 28 }, new[] { 1, 44 }, new[] { 2, 32 }, new[] { 2, 43 },
            new[] { 4, 27 }, new[] { 4, 31 }, new[] { 2, 38, 2, 39 }, new[] { 3, 36, 2, 37 }, new[] { 4, 43, 1, 44 }
        };
        static readonly int[][] Align =
        {
            null, new int[0], new[] { 6, 18 }, new[] { 6, 22 }, new[] { 6, 26 }, new[] { 6, 30 }, new[] { 6, 34 },
            new[] { 6, 22, 38 }, new[] { 6, 24, 42 }, new[] { 6, 26, 46 }, new[] { 6, 28, 50 }
        };

        static readonly int[] Exp = new int[512];
        static readonly int[] Log = new int[256];

        static QrCode()
        {
            int x = 1;
            for (int i = 0; i < 255; i++)
            {
                Exp[i] = x;
                Log[x] = i;
                x <<= 1;
                if ((x & 0x100) != 0) x ^= 0x11D;
            }
            for (int i = 255; i < 512; i++) Exp[i] = Exp[i - 255];
        }

        static int Mul(int a, int b) { return a == 0 || b == 0 ? 0 : Exp[Log[a] + Log[b]]; }

        static int[] Generator(int n)
        {
            var g = new List<int> { 1 };
            for (int i = 0; i < n; i++)
            {
                var ng = new int[g.Count + 1];
                for (int j = 0; j < g.Count; j++)
                {
                    ng[j] ^= g[j];
                    ng[j + 1] ^= Mul(g[j], Exp[i]);
                }
                g = ng.ToList();
            }
            return g.ToArray();
        }

        static int[] Remainder(List<int> data, int n)
        {
            var g = Generator(n);
            var rem = new int[n];
            foreach (int b in data)
            {
                int factor = b ^ rem[0];
                Array.Copy(rem, 1, rem, 0, n - 1);
                rem[n - 1] = 0;
                for (int j = 0; j < n; j++) rem[j] ^= Mul(g[j + 1], factor);
            }
            return rem;
        }

        static int Capacity(int v)
        {
            var g = Groups[v];
            int s = 0;
            for (int i = 0; i < g.Length; i += 2) s += g[i] * g[i + 1];
            return s;
        }

        public static bool[,] Encode(string text)
        {
            byte[] data = Encoding.UTF8.GetBytes(text);
            int v = 1, cntBits = 8;
            for (; v <= 10; v++)
            {
                cntBits = v < 10 ? 8 : 16;
                if (4 + cntBits + data.Length * 8 <= Capacity(v) * 8) break;
            }
            if (v > 10) throw new ArgumentException("Слишком длинный текст для QR-кода");
            int cap = Capacity(v) * 8;

            var bits = new List<int>();
            Action<int, int> put = (val, n) => { for (int i = n - 1; i >= 0; i--) bits.Add((val >> i) & 1); };
            put(4, 4);
            put(data.Length, cntBits);
            foreach (byte b in data) put(b, 8);
            put(0, Math.Min(4, cap - bits.Count));
            while (bits.Count % 8 != 0) bits.Add(0);
            var cw = new List<int>();
            for (int i = 0; i < bits.Count; i += 8)
            {
                int b = 0;
                for (int j = 0; j < 8; j++) b = (b << 1) | bits[i + j];
                cw.Add(b);
            }
            for (int i = 0; cw.Count < cap / 8; i++) cw.Add(i % 2 == 0 ? 0xEC : 0x11);

            // блоки и коррекция ошибок
            int ecLen = EcLen[v];
            var blocks = new List<List<int>>();
            int k = 0;
            var grp = Groups[v];
            for (int gi = 0; gi < grp.Length; gi += 2)
                for (int c = 0; c < grp[gi]; c++)
                {
                    blocks.Add(cw.GetRange(k, grp[gi + 1]));
                    k += grp[gi + 1];
                }
            var ecs = blocks.Select(b => Remainder(b, ecLen)).ToList();
            var final = new List<int>();
            int maxLen = blocks.Max(b => b.Count);
            for (int i = 0; i < maxLen; i++)
                foreach (var b in blocks) if (i < b.Count) final.Add(b[i]);
            for (int i = 0; i < ecLen; i++)
                foreach (var e in ecs) final.Add(e[i]);

            int size = 17 + 4 * v;
            var m = new int[size, size];
            var fn = new bool[size, size];
            Action<int, int, int> setf = (r, c, val) => { m[r, c] = val; fn[r, c] = true; };

            // поисковые узоры с разделителями
            foreach (var o in new[] { new[] { 0, 0 }, new[] { 0, size - 7 }, new[] { size - 7, 0 } })
                for (int r = -1; r < 8; r++)
                    for (int c = -1; c < 8; c++)
                    {
                        int rr = o[0] + r, cc = o[1] + c;
                        if (rr < 0 || rr >= size || cc < 0 || cc >= size) continue;
                        bool inner = r >= 0 && r <= 6 && c >= 0 && c <= 6;
                        bool on = (inner && (c == 0 || c == 6 || r == 0 || r == 6)) || (r >= 2 && r <= 4 && c >= 2 && c <= 4);
                        setf(rr, cc, on && inner ? 1 : 0);
                    }
            // синхронизирующие полосы
            for (int i = 8; i < size - 8; i++)
            {
                setf(6, i, i % 2 == 0 ? 1 : 0);
                setf(i, 6, i % 2 == 0 ? 1 : 0);
            }
            // выравнивающие узоры
            var pos = Align[v];
            int last = pos.Length - 1;
            for (int i = 0; i < pos.Length; i++)
                for (int j = 0; j < pos.Length; j++)
                {
                    if ((i == 0 && j == 0) || (i == 0 && j == last) || (i == last && j == 0)) continue;
                    for (int dr = -2; dr <= 2; dr++)
                        for (int dc = -2; dc <= 2; dc++)
                            setf(pos[i] + dr, pos[j] + dc, Math.Max(Math.Abs(dr), Math.Abs(dc)) != 1 ? 1 : 0);
                }
            // резерв под формат, тёмный модуль, версия
            for (int i = 0; i < 9; i++)
            {
                if (!fn[8, i]) setf(8, i, 0);
                if (!fn[i, 8]) setf(i, 8, 0);
            }
            for (int i = 0; i < 8; i++)
            {
                setf(8, size - 1 - i, 0);
                setf(size - 1 - i, 8, 0);
            }
            setf(size - 8, 8, 1);
            if (v >= 7)
            {
                int vb = (v << 12) | Bch(v, 0x1F25, 12);
                for (int i = 0; i < 18; i++)
                {
                    int bit = (vb >> i) & 1, a = size - 11 + i % 3, b = i / 3;
                    setf(a, b, bit);
                    setf(b, a, bit);
                }
            }

            // данные змейкой снизу вверх
            var all = new List<int>();
            foreach (int b in final) for (int i = 7; i >= 0; i--) all.Add((b >> i) & 1);
            int idx = 0;
            bool up = true;
            for (int col = size - 1; col > 0; col -= 2)
            {
                if (col == 6) col = 5;
                for (int t = 0; t < size; t++)
                {
                    int r = up ? size - 1 - t : t;
                    for (int dc = 0; dc < 2; dc++)
                    {
                        int c = col - dc;
                        if (fn[r, c]) continue;
                        m[r, c] = idx < all.Count ? all[idx] : 0;
                        idx++;
                    }
                }
                up = !up;
            }

            // лучшая маска
            int[,] best = null;
            int bestPen = int.MaxValue;
            for (int mask = 0; mask < 8; mask++)
            {
                var mm = (int[,])m.Clone();
                for (int r = 0; r < size; r++)
                    for (int c = 0; c < size; c++)
                        if (!fn[r, c] && MaskBit(mask, r, c)) mm[r, c] ^= 1;
                DrawFormat(mm, size, mask);
                int p = Penalty(mm, size);
                if (p < bestPen) { bestPen = p; best = mm; }
            }

            var res = new bool[size, size];
            for (int r = 0; r < size; r++)
                for (int c = 0; c < size; c++) res[r, c] = best[r, c] == 1;
            return res;
        }

        static bool MaskBit(int mask, int r, int c)
        {
            switch (mask)
            {
                case 0: return (r + c) % 2 == 0;
                case 1: return r % 2 == 0;
                case 2: return c % 3 == 0;
                case 3: return (r + c) % 3 == 0;
                case 4: return (r / 2 + c / 3) % 2 == 0;
                case 5: return (r * c) % 2 + (r * c) % 3 == 0;
                case 6: return ((r * c) % 2 + (r * c) % 3) % 2 == 0;
                default: return ((r + c) % 2 + (r * c) % 3) % 2 == 0;
            }
        }

        static int Bch(int val, int poly, int deg)
        {
            int v = val << deg;
            for (int i = 31; i >= deg; i--)
                if (((v >> i) & 1) != 0) v ^= poly << (i - deg);
            return v;
        }

        static void DrawFormat(int[,] m, int size, int mask)
        {
            int data = (0 << 3) | mask;  // уровень M = 00
            int bits = ((data << 10) | Bch(data, 0x537, 10)) ^ 0x5412;
            for (int i = 0; i < 15; i++)
            {
                int b = (bits >> i) & 1;
                if (i < 6) m[i, 8] = b;
                else if (i == 6) m[7, 8] = b;
                else if (i == 7) m[8, 8] = b;
                else if (i == 8) m[8, 7] = b;
                else m[8, 14 - i] = b;

                if (i < 8) m[8, size - 1 - i] = b;
                else m[size - 15 + i, 8] = b;
            }
            m[size - 8, 8] = 1;
        }

        static int Penalty(int[,] m, int size)
        {
            int p = 0;
            for (int pass = 0; pass < 2; pass++)
            {
                for (int a = 0; a < size; a++)
                {
                    int run = 0, prev = -1;
                    var sb = new StringBuilder(size);
                    for (int b = 0; b < size; b++)
                    {
                        int v = pass == 0 ? m[a, b] : m[b, a];
                        sb.Append(v);
                        if (v == prev) run++;
                        else
                        {
                            if (run >= 5) p += run - 2;
                            run = 1;
                            prev = v;
                        }
                    }
                    if (run >= 5) p += run - 2;
                    string s = sb.ToString();
                    p += 40 * (Count(s, "10111010000") + Count(s, "00001011101"));
                }
            }
            for (int r = 0; r < size - 1; r++)
                for (int c = 0; c < size - 1; c++)
                    if (m[r, c] == m[r, c + 1] && m[r, c] == m[r + 1, c] && m[r, c] == m[r + 1, c + 1]) p += 3;
            int dark = 0;
            foreach (int x in m) dark += x;
            p += Math.Abs(dark * 20 - size * size * 10) / (size * size) * 10;
            return p;
        }

        // Как str.count в Python: без перекрытий.
        static int Count(string s, string sub)
        {
            int n = 0, i = 0;
            while ((i = s.IndexOf(sub, i, StringComparison.Ordinal)) >= 0) { n++; i += sub.Length; }
            return n;
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;

namespace LostRegalia.Sim
{
    /// <summary>
    /// 곶의 땅 하나. 타일 격자다. 타일 (x, y)에서 y = 0이 성 쪽 어귀, y가 클수록 바다 쪽이다.
    /// 생성기(LandGen)가 만들고, 그 뒤로는 채굴이 바꾼다.
    /// </summary>
    public sealed class Land
    {
        public readonly CapeDef Cape;
        public readonly LandDef Def;
        public readonly int W, H;
        public ulong Seed;
        public int Attempt;                 // 검증에 실패해 다시 만든 횟수

        public readonly Rock[] Rocks;
        public readonly ushort[] Hp;        // 남은 강도
        public readonly Ore[] OreKind;
        public readonly byte[] OreAmount;
        public readonly Feature[] Feat;
        public readonly bool[] Low;         // 밀물에 잠기는 갯골
        public readonly bool[] Tidal;       // 썰물에만 드러나는 광맥
        public readonly bool[] Reserved;    // 자리의 발자국
        public readonly bool[] Crystal, Corrupt;
        public readonly List<int> Boulders = new List<int>();       // 타일 번호 * 4 + 구르는 방향
        public int EntranceX, EntranceY, CraterX = -1, CraterY = -1;
        public int Passes;                  // 이어지게 하려고 뚫은 고개의 타일 수
        public bool FragmentTaken;

        public Land(CapeDef cape, LandDef def)
        {
            Cape = cape;
            Def = def;
            W = cape.Width;
            H = cape.Length;
            int n = W * H;
            Rocks = new Rock[n];
            Hp = new ushort[n];
            OreKind = new Ore[n];
            OreAmount = new byte[n];
            Feat = new Feature[n];
            Low = new bool[n];
            Tidal = new bool[n];
            Reserved = new bool[n];
            Crystal = new bool[n];
            Corrupt = new bool[n];
        }

        public int Index(int x, int y) { return y * W + x; }
        public bool Inside(int x, int y) { return x >= 0 && x < W && y >= 0 && y < H; }
        public Rock At(int x, int y) { return Inside(x, y) ? Rocks[y * W + x] : Rock.Sea; }
        public int BandOf(int y) { return Math.Min(y / Cape.BandRows, Cape.Bands - 1); }

        /// <summary>파서 지나갈 수 있는 땅인가. 바다와 바위산이 아니면 그렇다.</summary>
        public bool Passable(int x, int y)
        {
            Rock r = At(x, y);
            return r != Rock.Sea && r != Rock.Ridge;
        }

        /// <summary>부숴야 지나가는 암반인가.</summary>
        public bool Solid(int x, int y)
        {
            return At(x, y) != Rock.Open;
        }

        public static bool IsRock(Rock r) { return r >= Rock.Soil; }

        public int HardnessOf(int i)
        {
            if (!IsRock(Rocks[i])) return 0;
            return Crystal[i] ? Cape.Hardness[(int)Rock.Soft - 4] : Cape.Hardness[(int)Rocks[i] - 4];
        }

        public int OreLeft(Ore kind)
        {
            int total = 0;
            for (int i = 0; i < OreKind.Length; i++) if (OreKind[i] == kind) total += OreAmount[i];
            return total;
        }

        /// <summary>어귀에서 걸어서(파서) 닿는 타일.</summary>
        public bool[] Flood()
        {
            var seen = new bool[W * H];
            var stack = new List<int>();
            int start = Index(EntranceX, EntranceY);
            seen[start] = true;
            stack.Add(start);
            while (stack.Count > 0)
            {
                int p = stack[stack.Count - 1];
                stack.RemoveAt(stack.Count - 1);
                int x = p % W, y = p / W;
                for (int d = 0; d < 4; d++)
                {
                    int qx = x + LandGen.DX[d], qy = y + LandGen.DY[d];
                    if (!Inside(qx, qy) || !Passable(qx, qy)) continue;
                    int q = Index(qx, qy);
                    if (seen[q]) continue;
                    seen[q] = true;
                    stack.Add(q);
                }
            }
            return seen;
        }

        // 지금은 타일을 통째로 담는다(땅 하나에 약 5KB). 시드와 바뀐 것만 담는 것은 저장 파일을 만들 때 한다.
        public void Write(BinaryWriter w)
        {
            w.Write(Seed); w.Write(Attempt); w.Write(FragmentTaken);
            for (int i = 0; i < Rocks.Length; i++)
            {
                w.Write((byte)Rocks[i]);
                w.Write(Hp[i]);
                w.Write((byte)OreKind[i]);
                w.Write(OreAmount[i]);
            }
        }

        public void Read(BinaryReader r)
        {
            Seed = r.ReadUInt64(); Attempt = r.ReadInt32(); FragmentTaken = r.ReadBoolean();
            for (int i = 0; i < Rocks.Length; i++)
            {
                Rocks[i] = (Rock)r.ReadByte();
                Hp[i] = r.ReadUInt16();
                OreKind[i] = (Ore)r.ReadByte();
                OreAmount[i] = r.ReadByte();
            }
        }
    }

    /// <summary>
    /// 땅 생성기. tools/cape_gen.py와 같은 순서, 같은 규칙, 같은 검증이다. 같은 땅이 나오지는 않는다.
    /// 정수만 쓴다. 같은 시드는 어느 컴퓨터에서든 같은 땅을 낸다.
    /// </summary>
    public static class LandGen
    {
        public static readonly int[] DX = { 1, -1, 0, 0 };
        public static readonly int[] DY = { 0, 0, 1, -1 };
        static readonly int[] Drift = { -1, 0, 0, 1 };

        /// <summary>만들고, 검증하고, 실패하면 다시 만든다.</summary>
        public static Land GenerateValid(CapeDef cape, LandDef def, ulong seed)
        {
            Land land = null;
            for (int attempt = 0; attempt < cape.RetryLimit; attempt++)
            {
                land = Generate(cape, def, seed, attempt);
                if (Validate(land).Count == 0) return land;
            }
            return land;
        }

        static int Clamp(int v, int lo, int hi) { return v < lo ? lo : (v > hi ? hi : v); }
        static int Between(ref Rng rng, int lo, int hi) { return lo + rng.Range(hi - lo + 1); }
        static int RoundDiv(long a, long b) { return (int)((a * 2 + b) / (b * 2)); }

        /// <summary>격자 값 잡음. 0~65535. 보간도 정수로 한다.</summary>
        sealed class Noise
        {
            readonly int scale, gw;
            readonly int[] g;

            public Noise(ref Rng rng, int w, int h, int scale)
            {
                this.scale = scale;
                gw = w / scale + 3;
                int gh = h / scale + 3;
                g = new int[gw * gh];
                for (int i = 0; i < g.Length; i++) g[i] = rng.Range(65536);
            }

            static int Smooth(int r, int scale)
            {
                long t = r * 1024 / scale;
                return (int)((t * t * (3072 - 2 * t)) >> 20);
            }

            public int At(int x, int y)
            {
                int x0 = x / scale, y0 = y / scale;
                int sx = Smooth(x % scale, scale), sy = Smooth(y % scale, scale);
                int a = (g[y0 * gw + x0] * (1024 - sx) + g[y0 * gw + x0 + 1] * sx) >> 10;
                int b = (g[(y0 + 1) * gw + x0] * (1024 - sx) + g[(y0 + 1) * gw + x0 + 1] * sx) >> 10;
                return (a * (1024 - sy) + b * sy) >> 10;
            }
        }

        static int[] SortedByNoise(List<int> cells, Noise n1, Noise n2, int w, bool descending)
        {
            var keys = new long[cells.Count];
            for (int i = 0; i < cells.Count; i++)
            {
                int x = cells[i] % w, y = cells[i] / w;
                long v = 65L * n1.At(x, y) + 35L * n2.At(x, y);
                if (descending) v = -v;
                keys[i] = v * 4096 + cells[i];              // 값이 같으면 타일 번호 순서
            }
            var arr = cells.ToArray();
            Array.Sort(keys, arr);
            return arr;
        }

        public static Land Generate(CapeDef cape, LandDef def, ulong seed, int attempt)
        {
            var rng = new Rng(seed * 1000003UL + (ulong)def.Id * 7919UL + (ulong)attempt * 104729UL + 1UL);
            var L = new Land(cape, def) { Seed = seed, Attempt = attempt };
            int W = L.W, H = L.H, band = cape.BandRows;
            var centers = new List<int>();

            // 1. 해안선: 좌우는 바다. 땅끝은 뒤쪽도 바다
            int lm = 1, rm = 1;
            for (int y = 0; y < H; y++)
            {
                lm = Clamp(lm + Drift[rng.Range(4)], 0, 2);
                rm = Clamp(rm + Drift[rng.Range(4)], 0, 2);
                for (int x = 0; x < W; x++)
                    if (x < lm || x >= W - rm) L.Rocks[L.Index(x, y)] = Rock.Sea;
            }
            if (def.SeaEnd)
            {
                int end = 2;
                for (int x = 0; x < W; x++)
                {
                    end = Clamp(end + Drift[rng.Range(4)], 1, 3);
                    for (int y = H - end; y < H; y++) L.Rocks[L.Index(x, y)] = Rock.Sea;
                }
            }

            // 2. 바위산: 잡음의 높은 쪽부터 정해진 비율만큼
            {
                var n1 = new Noise(ref rng, W, H, 6);
                var n2 = new Noise(ref rng, W, H, 3);
                var cand = new List<int>();
                for (int y = 0; y < H; y++)
                    for (int x = 0; x < W; x++)
                        if (L.Rocks[L.Index(x, y)] == Rock.Raw && !(y < 4 && x >= cape.EntranceCol0 - 2 && x <= cape.EntranceCol1 + 2))
                            cand.Add(L.Index(x, y));
                int[] sorted = SortedByNoise(cand, n1, n2, W, true);
                int count = (int)((long)sorted.Length * def.Ridge / 1000);
                for (int i = 0; i < count; i++) L.Rocks[sorted[i]] = Rock.Ridge;
            }

            // 3. 갯골: 땅을 가로지르는 낮은 띠. 둑 하나는 언제나 말라 있다
            for (int c = 0; c < def.Channels.Length; c++)
            {
                int r0 = def.Channels[c], cy = r0, xd = Between(ref rng, 4, W - 6);
                for (int x = 0; x < W; x++)
                {
                    if (rng.Range(100) < 35)
                        cy = Clamp(cy + (rng.Range(2) == 0 ? -1 : 1), r0 - cape.ChannelWander, r0 + cape.ChannelWander);
                    for (int y = cy; y <= cy + 1; y++)
                    {
                        int i = L.Index(x, y);
                        if (L.Rocks[i] == Rock.Sea) continue;
                        L.Rocks[i] = Rock.Open;
                        if (x == xd || x == xd + 1) L.Feat[i] = Feature.Dike;
                        else L.Low[i] = true;
                    }
                }
            }

            // 4. 어귀의 빈터
            for (int y = 0; y < cape.EntranceRows; y++)
                for (int x = cape.EntranceCol0; x <= cape.EntranceCol1; x++)
                    L.Rocks[L.Index(x, y)] = Rock.Open;
            L.EntranceX = W / 2 - 1;
            L.EntranceY = 0;
            L.Feat[L.Index(L.EntranceX, 0)] = Feature.Entrance;

            // 5. 자리: 낙성지, 봉인실, 석관
            int spot = Place(L, ref rng, centers, cape.CraterSize, cape.CraterRow0 - 2, cape.CraterRow1 - 2);
            if (spot >= 0)
            {
                int x0 = spot % W, y0 = spot / W, s = cape.CraterSize;
                for (int j = 0; j < s; j++)
                    for (int i = 0; i < s; i++) L.Rocks[L.Index(x0 + i, y0 + j)] = Rock.Open;
                L.CraterX = x0 + s / 2;
                L.CraterY = y0 + s / 2;
                L.Feat[L.Index(L.CraterX, L.CraterY)] = Feature.Crater;
            }
            for (int b = 0; b < def.Vaults.Length; b++)
                for (int v = 0; v < def.Vaults[b]; v++)
                {
                    int s = cape.VaultSize;
                    spot = Place(L, ref rng, centers, s, Math.Max(3, b * band), b * band + band - s);
                    if (spot < 0) spot = Place(L, ref rng, centers, s, 3, H - 7);       // 구간 안에 자리가 없으면 어디든
                    if (spot < 0) continue;
                    int x0 = spot % W, y0 = spot / W;
                    for (int j = 0; j < s; j++)
                        for (int i = 0; i < s; i++)
                            L.Rocks[L.Index(x0 + i, y0 + j)] = (i >= 1 && i <= s - 2 && j >= 1 && j <= s - 2) ? Rock.Open : def.VaultWall;
                    L.Feat[L.Index(x0 + s / 2, y0 + s / 2)] = Feature.Vault;
                }
            spot = Place(L, ref rng, centers, cape.CoffinSize, band, H - 8);
            if (spot >= 0)
            {
                int x0 = spot % W, y0 = spot / W, s = cape.CoffinSize;
                for (int j = 0; j < s; j++)
                    for (int i = 0; i < s; i++) L.Rocks[L.Index(x0 + i, y0 + j)] = Rock.Hard;
                int c = L.Index(x0 + s / 2, y0 + s / 2);
                L.Rocks[c] = Rock.Open;
                L.Feat[c] = Feature.Coffin;
            }

            // 6. 끊긴 곳 잇기: 바위산에 막힌 땅이 있으면 가장 얇은 곳에 고개를 뚫는다
            Connect(L);

            // 7. 암반: 구간마다 정해진 비율로, 잡음을 따라 덩어리지게
            {
                var n1 = new Noise(ref rng, W, H, 4);
                var n2 = new Noise(ref rng, W, H, 2);
                for (int b = 0; b < cape.Bands; b++)
                {
                    var cells = new List<int>();
                    for (int y = b * band; y < (b + 1) * band; y++)
                        for (int x = 0; x < W; x++)
                            if (L.Rocks[L.Index(x, y)] == Rock.Raw) cells.Add(L.Index(x, y));
                    int[] sorted = SortedByNoise(cells, n1, n2, W, false);
                    int[] mix = def.Mix[b];
                    int last = 0;
                    for (int k = 0; k < 4; k++) if (mix[k] > 0) last = k;
                    int at = 0;
                    for (int k = 0; k <= last; k++)
                    {
                        int n = k == last ? sorted.Length - at : Math.Min(sorted.Length - at, RoundDiv((long)sorted.Length * mix[k], 1000));
                        for (int i = 0; i < n; i++) L.Rocks[sorted[at + i]] = (Rock)(4 + k);
                        at += n;
                    }
                }
            }

            // 8. 빈터와 샘
            {
                int[] radii = { 100, 196, 400 };            // 반지름 1.0, 1.4, 2.0의 제곱 × 100
                for (int p = 0; p < def.Pockets; p++)
                {
                    var cells = new List<int>();
                    for (int y = 4; y < H; y++)
                        for (int x = 0; x < W; x++)
                            if (FreeRock(L, x, y)) cells.Add(L.Index(x, y));
                    if (cells.Count == 0) break;
                    int c = cells[rng.Range(cells.Count)];
                    int cx = c % W, cy = c / W, rad = radii[rng.Range(3)];
                    for (int y = cy - 2; y <= cy + 2; y++)
                        for (int x = cx - 2; x <= cx + 2; x++)
                            if (((x - cx) * (x - cx) + (y - cy) * (y - cy)) * 100 <= rad && FreeRock(L, x, y))
                                L.Rocks[L.Index(x, y)] = Rock.Open;
                    if (p < def.Springs) L.Feat[c] = Feature.Spring;
                }
            }

            // 9. 옛 길: 구간을 가로지르는 지름길. 바위산도 뚫고 지나간다
            {
                int x = Between(ref rng, 4, W - 5), y = Between(ref rng, 8, 14);
                int length = Between(ref rng, cape.RoadMin, cape.RoadMax);
                for (int s = 0; s < length; s++)
                {
                    int i = L.Index(x, y);
                    if (L.Rocks[i] == Rock.Sea || L.Reserved[i] || L.Low[i] || y >= H - 1) break;
                    L.Rocks[i] = Rock.Open;
                    if (L.Feat[i] == Feature.None) L.Feat[i] = Feature.OldRoad;
                    if (rng.Range(1000) < cape.RoadTurn) x = Clamp(x + (rng.Range(2) == 0 ? -1 : 1), 2, W - 3);
                    else y++;
                }
            }

            // 10. 광맥: 덩어리로. 총량은 밸런스 시트의 매장량과 정확히 같다
            for (int kind = 1; kind <= 3; kind++) Veins(L, ref rng, (Ore)kind);

            // 11. 위험: 걸린 바위, 불안정한 결정, 침식의 씨앗
            Hazards(L, ref rng);

            for (int i = 0; i < L.Rocks.Length; i++) L.Hp[i] = (ushort)L.HardnessOf(i);
            return L;
        }

        static bool FreeRock(Land L, int x, int y)
        {
            if (!L.Inside(x, y)) return false;
            int i = L.Index(x, y);
            return Land.IsRock(L.Rocks[i]) && !L.Reserved[i] && L.OreKind[i] == Ore.None && L.Feat[i] == Feature.None;
        }

        /// <summary>정사각 자리 하나를 놓을 곳을 찾는다. 왼쪽 위 타일의 번호, 없으면 -1.</summary>
        static int Place(Land L, ref Rng rng, List<int> centers, int size, int y0, int y1)
        {
            if (y1 < y0) return -1;
            int gap2 = L.Cape.MinGap * L.Cape.MinGap;
            for (int t = 0; t < 400; t++)
            {
                int x = Between(ref rng, 2, L.W - 2 - size), y = Between(ref rng, y0, y1);
                int cx = x + size / 2, cy = y + size / 2;
                bool ok = true;
                for (int j = 0; j < size && ok; j++)
                    for (int i = 0; i < size && ok; i++)
                    {
                        if (!L.Inside(x + i, y + j)) { ok = false; break; }
                        int q = L.Index(x + i, y + j);
                        if (L.Rocks[q] == Rock.Sea || L.Low[q] || L.Reserved[q] || L.Feat[q] != Feature.None) ok = false;
                    }
                for (int c = 0; c < centers.Count && ok; c++)
                {
                    int dx = centers[c] % L.W - cx, dy = centers[c] / L.W - cy;
                    if (dx * dx + dy * dy < gap2) ok = false;
                }
                if (!ok) continue;
                for (int j = 0; j < size; j++)
                    for (int i = 0; i < size; i++) L.Reserved[L.Index(x + i, y + j)] = true;
                centers.Add(L.Index(cx, cy));
                return L.Index(x, y);
            }
            return -1;
        }

        static void Connect(Land L)
        {
            int n = L.W * L.H;
            var dist = new int[n];
            var prev = new int[n];
            var dq = new int[n * 8 + 8];
            for (int guard = 0; guard < n; guard++)
            {
                bool[] seen = L.Flood();
                bool lost = false;
                for (int i = 0; i < n && !lost; i++)
                    if (!seen[i] && L.Passable(i % L.W, i / L.W)) lost = true;
                if (!lost) return;

                // 닿은 땅 전체에서 출발해, 바위산을 가장 적게 지나 닿지 않은 땅에 이르는 길 (0-1 너비 우선)
                int head = dq.Length / 2, tail = head;
                for (int i = 0; i < n; i++)
                {
                    prev[i] = -1;
                    dist[i] = seen[i] ? 0 : int.MaxValue;
                    if (seen[i]) dq[tail++] = i;
                }
                int target = -1;
                while (head < tail)
                {
                    int p = dq[head++];
                    int x = p % L.W, y = p / L.W;
                    if (!seen[p] && L.Passable(x, y)) { target = p; break; }
                    for (int d = 0; d < 4; d++)
                    {
                        int qx = x + DX[d], qy = y + DY[d];
                        if (!L.Inside(qx, qy) || L.Rocks[L.Index(qx, qy)] == Rock.Sea) continue;
                        int q = L.Index(qx, qy);
                        int step = L.Rocks[q] == Rock.Ridge ? 1 : 0;
                        if (dist[p] + step >= dist[q]) continue;
                        dist[q] = dist[p] + step;
                        prev[q] = p;
                        if (step == 0) dq[--head] = q; else dq[tail++] = q;
                    }
                }
                if (target < 0)
                {
                    // 바다로 완전히 끊긴 조각은 바다로 돌린다
                    for (int i = 0; i < n; i++)
                        if (!seen[i] && L.Passable(i % L.W, i / L.W)) L.Rocks[i] = Rock.Sea;
                    return;
                }
                for (int p = target; prev[p] >= 0; p = prev[p])
                    if (L.Rocks[p] == Rock.Ridge)
                    {
                        L.Rocks[p] = Rock.Raw;
                        L.Passes++;
                    }
            }
        }

        static void Veins(Land L, ref Rng rng, Ore kind)
        {
            LandDef def = L.Def;
            OreSpec spec = def.Ores[(int)kind];
            VeinRule rule = L.Cape.Veins[(int)kind];
            if (spec.Total == 0) return;
            int W = L.W, H = L.H, band = L.Cape.BandRows;
            int nAll = Math.Max(1, RoundDiv(spec.Total, spec.PerTile));
            var placed = new List<int>();

            if (kind == Ore.Star && def.TidalShare > 0)
            {
                var beds = new List<int>();
                for (int i = 0; i < L.Low.Length; i++) if (L.Low[i] && L.OreKind[i] == Ore.None) beds.Add(i);
                int take = Math.Min(beds.Count, RoundDiv((long)nAll * def.TidalShare, 1000));
                for (int t = 0; t < take; t++)
                {
                    int j = t + rng.Range(beds.Count - t);
                    int p = beds[j];
                    beds[j] = beds[t];
                    L.OreKind[p] = kind;
                    L.OreAmount[p] = (byte)spec.PerTile;
                    L.Tidal[p] = true;
                    placed.Add(p);
                }
            }

            int rest = nAll - placed.Count;
            var perBand = new int[L.Cape.Bands];
            int last = 0, sum = 0;
            for (int b = 0; b < perBand.Length; b++)
            {
                perBand[b] = RoundDiv((long)rest * spec.Split[b], 1000);
                sum += perBand[b];
                if (spec.Split[b] > 0) last = b;
            }
            perBand[last] += rest - sum;

            var cells = new List<int>();
            var weights = new List<int>();
            var cluster = new List<int>();
            var near = new List<int>();
            for (int b = 0; b < perBand.Length; b++)
            {
                int done = 0;
                while (done < perBand[b])
                {
                    cells.Clear();
                    weights.Clear();
                    int total = 0;
                    for (int y = b * band; y < (b + 1) * band; y++)
                        for (int x = 0; x < W; x++)
                        {
                            if (!FreeRock(L, x, y)) continue;
                            int wgt = rule.HostWeight[(int)L.Rocks[L.Index(x, y)] - 4];
                            if (wgt <= 0) continue;
                            cells.Add(L.Index(x, y));
                            weights.Add(wgt);
                            total += wgt;
                        }
                    if (cells.Count == 0)
                    {
                        for (int y = 0; y < H; y++)
                            for (int x = 0; x < W; x++)
                            {
                                if (!FreeRock(L, x, y)) continue;
                                int wgt = Math.Max(1, rule.HostWeight[(int)L.Rocks[L.Index(x, y)] - 4]);
                                cells.Add(L.Index(x, y));
                                weights.Add(wgt);
                                total += wgt;
                            }
                    }
                    if (cells.Count == 0) return;               // 놓을 곳이 없다. 검증이 걸러 낸다
                    int roll = rng.Range(total), pick = 0;
                    while (roll >= weights[pick]) roll -= weights[pick++];
                    int size = Math.Min(perBand[b] - done, Between(ref rng, rule.ClusterMin, rule.ClusterMax));
                    cluster.Clear();
                    cluster.Add(cells[pick]);
                    L.OreKind[cells[pick]] = kind;
                    L.OreAmount[cells[pick]] = (byte)spec.PerTile;
                    while (cluster.Count < size)
                    {
                        near.Clear();
                        for (int c = 0; c < cluster.Count; c++)
                            for (int d = 0; d < 4; d++)
                            {
                                int qx = cluster[c] % W + DX[d], qy = cluster[c] / W + DY[d];
                                if (FreeRock(L, qx, qy) && L.BandOf(qy) == b) near.Add(L.Index(qx, qy));
                            }
                        if (near.Count == 0) break;
                        int q = near[rng.Range(near.Count)];
                        L.OreKind[q] = kind;
                        L.OreAmount[q] = (byte)spec.PerTile;
                        cluster.Add(q);
                    }
                    placed.AddRange(cluster);
                    done += cluster.Count;
                }
            }

            // 타일 수로 나누어 남거나 모자란 양을 한 개씩 고르게 더하거나 뺀다
            int diff = spec.Total - placed.Count * spec.PerTile;
            int steps = Math.Abs(diff);
            if (steps > placed.Count) return;
            for (int t = 0; t < steps; t++)
            {
                int j = t + rng.Range(placed.Count - t);
                int p = placed[j];
                placed[j] = placed[t];
                placed[t] = p;
                L.OreAmount[p] = (byte)(L.OreAmount[p] + (diff > 0 ? 1 : -1));
            }
        }

        static void Hazards(Land L, ref Rng rng)
        {
            LandDef def = L.Def;
            int W = L.W, H = L.H, band = L.Cape.BandRows;
            var taken = new bool[W * H];
            if (def.Boulders > 0)
            {
                var edges = new List<int>();
                for (int y = 4; y < H; y++)
                    for (int x = 0; x < W; x++)
                    {
                        if (!FreeRock(L, x, y)) continue;
                        for (int d = 0; d < 4; d++)
                            if (L.At(x + DX[d], y + DY[d]) == Rock.Ridge) edges.Add(L.Index(x, y) * 4 + (d ^ 1));     // 바위산의 반대쪽으로 구른다
                    }
                for (int i = edges.Count - 1; i > 0; i--)
                {
                    int j = rng.Range(i + 1), t = edges[i];
                    edges[i] = edges[j];
                    edges[j] = t;
                }
                for (int i = 0; i < edges.Count && L.Boulders.Count < def.Boulders; i++)
                {
                    if (taken[edges[i] / 4]) continue;
                    taken[edges[i] / 4] = true;
                    L.Boulders.Add(edges[i]);
                }
            }

            var deep = new List<int>();
            for (int y = band; y < H; y++)
                for (int x = 0; x < W; x++)
                    if (FreeRock(L, x, y) && !taken[L.Index(x, y)]) deep.Add(L.Index(x, y));
            int crystals = 0;
            for (int guard = 0; guard < 2000 && crystals < def.Crystals && deep.Count > 0; guard++)
            {
                int p = deep[rng.Range(deep.Count)];
                if (L.Crystal[p]) continue;
                L.Crystal[p] = true;
                crystals++;
                if (crystals >= def.Crystals) break;
                int d0 = rng.Range(4);                          // 결정은 둘씩 붙어 있곤 하다
                for (int d = 0; d < 4; d++)
                {
                    int qx = p % W + DX[(d0 + d) % 4], qy = p / W + DY[(d0 + d) % 4];
                    if (qy < band || !FreeRock(L, qx, qy)) continue;
                    int q = L.Index(qx, qy);
                    if (taken[q] || L.Crystal[q]) continue;
                    L.Crystal[q] = true;
                    crystals++;
                    break;
                }
            }

            var far = new List<int>();
            for (int y = 2 * band; y < H; y++)
                for (int x = 0; x < W; x++)
                    if (FreeRock(L, x, y) && !L.Crystal[L.Index(x, y)]) far.Add(L.Index(x, y));
            int corrupt = Math.Min(far.Count, def.Corruption);
            for (int t = 0; t < corrupt; t++)
            {
                int j = t + rng.Range(far.Count - t);
                int p = far[j];
                far[j] = far[t];
                L.Corrupt[p] = true;
            }
        }

        /// <summary>검증. 어긋난 것의 목록을 돌려준다. 비어 있으면 통과다. (곶 생성 규칙 8장)</summary>
        public static List<string> Validate(Land L)
        {
            var errors = new List<string>();
            LandDef def = L.Def;
            bool[] seen = L.Flood();
            int lost = 0;
            for (int i = 0; i < seen.Length; i++)
                if (!seen[i] && L.Passable(i % L.W, i / L.W)) lost++;
            if (lost > 0) errors.Add("닿을 수 없는 타일 " + lost + "개");
            for (int kind = 1; kind <= 3; kind++)
            {
                int total = L.OreLeft((Ore)kind);
                if (total != def.Ores[kind].Total) errors.Add((Ore)kind + " 매장량 " + total + " / " + def.Ores[kind].Total);
            }
            int vaults = 0, craters = 0, coffins = 0;
            for (int i = 0; i < seen.Length; i++)
            {
                if (L.OreKind[i] != Ore.None && !seen[i]) errors.Add("닿을 수 없는 광맥 " + i);
                if (L.OreKind[i] != Ore.None && L.OreAmount[i] == 0) errors.Add("빈 광맥 " + i);
                if (L.Feat[i] == Feature.Vault) vaults++;
                if (L.Feat[i] == Feature.Crater) craters++;
                if (L.Feat[i] == Feature.Coffin) coffins++;
            }
            if (craters != 1 || L.CraterX < 0) errors.Add("낙성지가 없다");
            else if (L.BandOf(L.CraterY) != L.Cape.Bands - 1) errors.Add("낙성지가 가장 먼 구간에 있지 않다");
            int want = 0;
            for (int b = 0; b < def.Vaults.Length; b++) want += def.Vaults[b];
            if (vaults != want) errors.Add("봉인실 " + vaults + " / " + want);
            if (coffins != 1) errors.Add("석관이 없다");
            for (int c = 0; c < def.Channels.Length; c++)
            {
                bool dike = false;
                for (int y = def.Channels[c] - 2; y <= def.Channels[c] + 3 && !dike; y++)
                    for (int x = 0; x < L.W && !dike; x++)
                        if (L.Inside(x, y) && L.Feat[L.Index(x, y)] == Feature.Dike) dike = true;
                if (!dike) errors.Add("갯골 " + def.Channels[c] + "에 둑이 없다");
            }
            return errors;
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;

namespace LostRegalia.Sim
{
    /// <summary>성과 왕이 가진 자원. 뒷문에 내려놓은 것만 센다.</summary>
    public sealed class Economy
    {
        public int Star, Iron, Rune, Essence;

        public void Write(BinaryWriter w) { w.Write(Star); w.Write(Iron); w.Write(Rune); w.Write(Essence); }
        public void Read(BinaryReader r) { Star = r.ReadInt32(); Iron = r.ReadInt32(); Rune = r.ReadInt32(); Essence = r.ReadInt32(); }
    }

    /// <summary>땅에 내려놓았거나 들지 못해 남은 자원 더미.</summary>
    public struct Pile
    {
        public float X, Y;
        public int Star, Iron, Rune;
        public int Count { get { return Star + Iron + Rune; } }
    }

    /// <summary>
    /// 곶. 지금은 땅 하나다. 왕의 이동과 막힘, 채굴, 운반, 내려놓기와 줍기, 대파편을 맡는다.
    /// 좌표는 미터다. x는 땅의 왼쪽 끝이 0, y는 땅의 어귀가 0이고 바다 쪽이 +다.
    /// y가 음수인 곳은 성에서 땅으로 가는 길이고, 그 끝(-ApproachM)이 뒷문이다.
    /// </summary>
    public sealed class Cape
    {
        public readonly CapeDef Def;
        public readonly Land Land;
        public int CarryLimit;                      // 운반 한도. 등 강화가 올린다. 상태가 아니다
        public int DigPower;                        // 타격 한 번이 깎는 강도. 줄기의 채굴 위력이 올린다
        public readonly List<Pile> Piles = new List<Pile>();
        public int Swings, TilesDug, MinedStar, MinedIron, MinedRune;      // 계측용

        public Cape(CapeDef def, Land land)
        {
            Def = def;
            Land = land;
            DigPower = def.PowerByNodes[0];
            CarryLimit = def.CarryLimit;
        }

        /// <summary>길의 가운데. 어귀 타일과 그 오른쪽 타일의 사이다.</summary>
        public float GateX { get { return (Land.EntranceX + 1) * Def.TileM; } }

        bool SolidTile(int tx, int ty)
        {
            if (ty < 0) return tx < Land.EntranceX || tx > Land.EntranceX + 1 || ty < -Def.ApproachM / Def.TileM;
            return Land.Solid(tx, ty);
        }

        /// <summary>반지름 r인 왕이 (x, y)에 설 수 없는가.</summary>
        public bool Blocked(float x, float y, float r)
        {
            int m = Def.TileM;
            const float Skin = 0.001f;                  // 타일의 모서리에 꼭 붙어 선 것은 겹친 것이 아니다
            int x0 = (int)Math.Floor((x - r + Skin) / m), x1 = (int)Math.Floor((x + r - Skin) / m);
            int y0 = (int)Math.Floor((y - r + Skin) / m), y1 = (int)Math.Floor((y + r - Skin) / m);
            for (int ty = y0; ty <= y1; ty++)
                for (int tx = x0; tx <= x1; tx++)
                    if (SolidTile(tx, ty)) return true;
            return false;
        }

        public int CarryCount(King king) { return king.Star + king.Iron + king.Rune; }

        /// <summary>끄는 것이 많으면 느려진다. 1이 제 속도.</summary>
        public float SpeedFactor(King king)
        {
            int over = CarryCount(king) - Def.CarryFree;
            if (over <= 0) return 1f;
            return 1f - Math.Min(Def.CarrySlowMaxPermille, over * Def.CarrySlowPermille) / 1000f;
        }

        /// <summary>뒷문을 나서 길에 선다.</summary>
        public void Enter(King king)
        {
            king.CX = GateX;
            king.CY = -Def.ApproachM + Def.KingRadius + 0.1f;
        }

        /// <summary>왕의 틱 하나. 뒷문으로 돌아가려 하면 참을 돌려준다.</summary>
        public bool StepKing(King king, InputFrame input, Buttons pressed, float dt, World w)
        {
            float r = Def.KingRadius, dx, dy;
            bool dashing = king.Displace(input, pressed, dt, SpeedFactor(king) * king.MoveFactor, out dx, out dy);
            // 뒷문: 길의 끝에 닿았고 성 쪽으로 밀고 있으면 돌아간다
            if (input.MoveY < -0.5f && king.CY + Math.Min(dy, 0f) - r <= -Def.ApproachM + 0.1f) return true;
            // 가로와 세로를 따로 움직인다. 막히면 걸음을 반씩 줄여 벽에 붙는다
            for (int half = 0; half < 4 && dx != 0f; half++, dx *= 0.5f)
                if (!Blocked(king.CX + dx, king.CY, r)) { king.CX += dx; break; }
            for (int half = 0; half < 4 && dy != 0f; half++, dy *= 0.5f)
                if (!Blocked(king.CX, king.CY + dy, r)) { king.CY += dy; break; }
            if (dashing) return false;

            if ((pressed & Buttons.Drop) != 0 && CarryCount(king) > 0)
            {
                AddPile(king.CX, king.CY, king.Star, king.Iron, king.Rune);
                king.Star = king.Iron = king.Rune = 0;
            }
            if ((pressed & Buttons.Interact) != 0) Interact(king, w);
            if ((input.Held & Buttons.Attack) != 0 && king.Busy <= 0f && king.CY > -r) Swing(king);
            return false;
        }

        void Interact(King king, World w)
        {
            int m = Def.TileM;
            float reach2 = Def.PickupReach * Def.PickupReach;
            if (!Land.FragmentTaken && Land.CraterX >= 0)
            {
                float fx = Land.CraterX * m + m * 0.5f - king.CX, fy = Land.CraterY * m + m * 0.5f - king.CY;
                if (fx * fx + fy * fy <= reach2)
                {
                    Land.FragmentTaken = true;
                    king.HasFragment = true;
                    w.Emit(EventKind.FragmentTaken, 0, 0, 0);
                    return;
                }
            }
            int best = -1;
            float bestD = reach2;
            for (int i = 0; i < Piles.Count; i++)
            {
                float px = Piles[i].X - king.CX, py = Piles[i].Y - king.CY;
                float d = px * px + py * py;
                if (d <= bestD) { bestD = d; best = i; }
            }
            if (best < 0) return;
            Pile p = Piles[best];
            int room = CarryLimit - CarryCount(king);
            int n = Math.Min(room, p.Star); king.Star += n; p.Star -= n; room -= n;
            n = Math.Min(room, p.Iron); king.Iron += n; p.Iron -= n; room -= n;
            n = Math.Min(room, p.Rune); king.Rune += n; p.Rune -= n;
            if (p.Count == 0) Piles.RemoveAt(best); else Piles[best] = p;
        }

        void AddPile(float x, float y, int star, int iron, int rune)
        {
            for (int i = 0; i < Piles.Count; i++)
            {
                float px = Piles[i].X - x, py = Piles[i].Y - y;
                if (px * px + py * py > 1f) continue;
                Pile p = Piles[i];
                p.Star += star; p.Iron += iron; p.Rune += rune;
                Piles[i] = p;
                return;
            }
            Piles.Add(new Pile { X = x, Y = y, Star = star, Iron = iron, Rune = rune });
        }

        /// <summary>채굴 타격 한 번. 조준한 쪽 호 안의 암반 가운데 가까운 것 셋까지 깎는다.</summary>
        void Swing(King king)
        {
            int m = Def.TileM;
            float cos = (float)Math.Cos(Def.DigArc * 0.5 * Math.PI / 180.0);
            int kx = (int)Math.Floor(king.CX / m), ky = (int)Math.Floor(king.CY / m);
            int a = -1, b = -1, c = -1;
            float da = float.MaxValue, db = float.MaxValue, dc = float.MaxValue;
            for (int ty = ky - 2; ty <= ky + 2; ty++)
                for (int tx = kx - 2; tx <= kx + 2; tx++)
                {
                    if (!Land.Inside(tx, ty) || !Land.IsRock(Land.Rocks[Land.Index(tx, ty)])) continue;
                    float ox = tx * m + m * 0.5f - king.CX, oy = ty * m + m * 0.5f - king.CY;
                    float d = (float)Math.Sqrt(ox * ox + oy * oy);
                    if (d > Def.DigReach) continue;
                    if (d > 0.001f && (ox * king.AimX + oy * king.AimY) / d < cos) continue;
                    int i = Land.Index(tx, ty);
                    if (d < da) { c = b; dc = db; b = a; db = da; a = i; da = d; }
                    else if (d < db) { c = b; dc = db; b = i; db = d; }
                    else if (d < dc) { c = i; dc = d; }
                }
            king.Busy = Def.Swing;
            Swings++;
            if (a >= 0) Hit(king, a);
            if (b >= 0) Hit(king, b);
            if (c >= 0) Hit(king, c);
        }

        void Hit(King king, int i)
        {
            if (Land.Hp[i] > DigPower)
            {
                Land.Hp[i] = (ushort)(Land.Hp[i] - DigPower);
                return;
            }
            Land.Hp[i] = 0;
            Land.Rocks[i] = Rock.Open;
            TilesDug++;
            Ore kind = Land.OreKind[i];
            if (kind == Ore.None) return;
            int amount = Land.OreAmount[i];
            Land.OreKind[i] = Ore.None;
            Land.OreAmount[i] = 0;
            int take = Math.Min(amount, CarryLimit - CarryCount(king));
            int star = kind == Ore.Star ? 1 : 0, iron = kind == Ore.Iron ? 1 : 0, rune = kind == Ore.Rune ? 1 : 0;
            king.Star += take * star; king.Iron += take * iron; king.Rune += take * rune;
            MinedStar += amount * star; MinedIron += amount * iron; MinedRune += amount * rune;
            int left = amount - take;                // 들지 못한 것은 그 자리에 남는다
            if (left > 0)
            {
                int m = Def.TileM;
                AddPile(i % Land.W * m + m * 0.5f, i / Land.W * m + m * 0.5f, left * star, left * iron, left * rune);
            }
        }

        public void Write(BinaryWriter w)
        {
            w.Write(DigPower); w.Write(Swings); w.Write(TilesDug); w.Write(MinedStar); w.Write(MinedIron); w.Write(MinedRune);
            Land.Write(w);
            w.Write(Piles.Count);
            for (int i = 0; i < Piles.Count; i++)
            {
                w.Write(Piles[i].X); w.Write(Piles[i].Y); w.Write(Piles[i].Star); w.Write(Piles[i].Iron); w.Write(Piles[i].Rune);
            }
        }

        public void Read(BinaryReader r)
        {
            DigPower = r.ReadInt32(); Swings = r.ReadInt32(); TilesDug = r.ReadInt32();
            MinedStar = r.ReadInt32(); MinedIron = r.ReadInt32(); MinedRune = r.ReadInt32();
            Land.Read(r);
            Piles.Clear();
            int n = r.ReadInt32();
            for (int i = 0; i < n; i++)
                Piles.Add(new Pile { X = r.ReadSingle(), Y = r.ReadSingle(), Star = r.ReadInt32(), Iron = r.ReadInt32(), Rune = r.ReadInt32() });
        }
    }
}

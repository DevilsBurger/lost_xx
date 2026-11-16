using System;
using System.IO;

namespace LostRegalia.Sim
{
    /// <summary>사거나 되돌리려 한 결과.</summary>
    public enum Buy : byte
    {
        Ok = 0,
        NotNow,         // 고요 중에 성벽 위(왕좌)에 있을 때만 바꿀 수 있다
        NoFunds,
        Locked,         // 조건이나 장의 상한에 걸렸다
        Maxed,          // 더 올릴 단계가 없다
        Taken,          // 이미 깨웠다, 이미 지었다
        Missing,        // 깨우지 않았다, 비어 있다
        NoRefunds,      // 이번 장의 되돌리기를 다 썼다
        Needed          // 다른 노드가 이것에 기대고 있다
    }

    /// <summary>
    /// 왕이 고른 것 전부: 깨운 노드, 왕 강화, 시설, 성벽 강화. 그리고 그것을 사고 되돌리는 명령.
    /// 명령은 메뉴에서, 틱과 틱 사이에 부른다. 바뀐 값은 Apply가 왕·곶·성벽에 다시 적는다.
    /// </summary>
    public sealed class Build
    {
        public readonly TreeDef Tree;
        public readonly CastleDef Castle;
        public readonly bool[] Awake;
        public readonly int[] KingLevel = new int[4];
        public readonly int[] SlotKind, SlotTier;       // 종류는 Castle.Facilities의 번호, 비어 있으면 -1
        public readonly float[] SlotTimer;              // 재장전
        public readonly int[] Equip;                    // 기술 칸에 장착한 노드의 번호. 비어 있으면 -1
        public int WallLevel, RefundsLeft, Chapter = 1;
        public int Shots;                               // 계측용

        public Build(TreeDef tree, CastleDef castle)
        {
            Tree = tree;
            Castle = castle;
            Awake = new bool[tree.Nodes.Length];
            int slots = castle.Slots[castle.Slots.Length - 1];
            SlotKind = new int[slots];
            SlotTier = new int[slots];
            SlotTimer = new float[slots];
            for (int i = 0; i < slots; i++) SlotKind[i] = -1;
            RefundsLeft = castle.Refunds;
            Equip = new int[castle.SkillSlots];
            for (int i = 0; i < Equip.Length; i++) Equip[i] = -1;
        }

        int Cap(int[] byChapter) { return byChapter[Math.Min(Chapter, byChapter.Length) - 1]; }
        public int SlotsOpen { get { return Cap(Castle.Slots); } }

        static bool CanShop(World w)
        {
            Phase p = w.Cycle.Phase;
            return (p == Phase.Calm || p == Phase.None) && w.King.OnWall && w.King.Mode != KingMode.Downed;
        }

        public float Sum(Stat stat)
        {
            float total = 0f;
            for (int i = 0; i < Awake.Length; i++)
            {
                if (!Awake[i]) continue;
                Effect[] fx = Tree.Nodes[i].Effects;
                for (int e = 0; e < fx.Length; e++) if (fx[e].Stat == stat) total += fx[e].Add;
            }
            return total;
        }

        int CountBranch(string branch, int tier)
        {
            int n = 0;
            for (int i = 0; i < Awake.Length; i++)
                if (Awake[i] && Tree.Nodes[i].Branch == branch && (tier == 0 || Tree.Nodes[i].Tier == tier)) n++;
            return n;
        }

        /// <summary>이 노드의 조건이 지금 깨운 것들로 채워지는가. (검 각성 트리 상세의 규칙 1~4)</summary>
        bool Satisfied(int i)
        {
            NodeDef n = Tree.Nodes[i];
            for (int r = 0; r < n.Requires.Length; r++) if (!Awake[n.Requires[r]]) return false;
            if (n.Pair != null) return CountBranch(n.Pair[0], 0) >= n.Need && CountBranch(n.Pair[1], 0) >= n.Need;
            if (n.Kind != "줄기" && n.Tier > 1 && CountBranch(n.Branch, n.Tier - 1) < 2) return false;
            return true;
        }

        public Buy Awaken(World w, string id)
        {
            int i = Tree.Index(id);
            NodeDef n = Tree.Nodes[i];
            if (!CanShop(w)) return Buy.NotNow;
            if (Awake[i]) return Buy.Taken;
            if (n.Tier > Cap(Castle.TreeTier) || !Satisfied(i)) return Buy.Locked;
            if (w.Economy.Star < n.Cost || w.Economy.Rune < n.RuneCost) return Buy.NoFunds;
            w.Economy.Star -= n.Cost;
            w.Economy.Rune -= n.RuneCost;
            Awake[i] = true;
            if (n.Skill != null && Array.IndexOf(Equip, i) < 0)
            {
                int empty = Array.IndexOf(Equip, -1);           // 빈 칸이 있으면 바로 장착한다
                if (empty >= 0) Equip[empty] = i;
            }
            Apply(w);
            return Buy.Ok;
        }

        /// <summary>기술을 칸에 장착한다. id가 null이면 칸을 비운다. 값이 들지 않는다.</summary>
        public Buy EquipSkill(World w, int slot, string id)
        {
            if (!CanShop(w)) return Buy.NotNow;
            if (slot < 0 || slot >= Equip.Length) return Buy.Locked;
            if (id == null) { Equip[slot] = -1; return Buy.Ok; }
            int i = Tree.Index(id);
            if (!Awake[i] || Tree.Nodes[i].Skill == null) return Buy.Missing;
            int old = Array.IndexOf(Equip, i);
            if (old >= 0) Equip[old] = Equip[slot];             // 이미 다른 칸에 있으면 자리를 바꾼다
            Equip[slot] = i;
            return Buy.Ok;
        }

        /// <summary>재배치. 값 없이, 횟수 제한 없이 되돌린다. 다른 노드가 기대고 있으면 안 된다.</summary>
        public Buy Unawaken(World w, string id)
        {
            int i = Tree.Index(id);
            if (!CanShop(w)) return Buy.NotNow;
            if (!Awake[i]) return Buy.Missing;
            Awake[i] = false;
            for (int j = 0; j < Awake.Length; j++)
                if (Awake[j] && !Satisfied(j))
                {
                    Awake[i] = true;
                    return Buy.Needed;
                }
            w.Economy.Star += Tree.Nodes[i].Cost;
            w.Economy.Rune += Tree.Nodes[i].RuneCost;
            int slot = Array.IndexOf(Equip, i);
            if (slot >= 0) Equip[slot] = -1;
            Apply(w);
            return Buy.Ok;
        }

        public Buy UpgradeKing(World w, Axis axis)
        {
            KingLevelDef[] levels = Castle.King[(int)axis];
            int lv = KingLevel[(int)axis];
            if (!CanShop(w)) return Buy.NotNow;
            if (lv >= levels.Length) return Buy.Maxed;
            if (lv >= Cap(Castle.KingLevel)) return Buy.Locked;
            if (w.Economy.Star < levels[lv].Cost) return Buy.NoFunds;
            w.Economy.Star -= levels[lv].Cost;
            KingLevel[(int)axis] = lv + 1;
            Apply(w);
            return Buy.Ok;
        }

        public Buy DowngradeKing(World w, Axis axis)
        {
            int lv = KingLevel[(int)axis];
            if (!CanShop(w)) return Buy.NotNow;
            if (lv == 0) return Buy.Missing;
            if (RefundsLeft <= 0) return Buy.NoRefunds;
            RefundsLeft--;
            w.Economy.Star += Castle.King[(int)axis][lv - 1].Cost;
            KingLevel[(int)axis] = lv - 1;
            Apply(w);
            return Buy.Ok;
        }

        public Buy BuildFacility(World w, int slot, string id)
        {
            int kind = Castle.Facility(id);
            if (!CanShop(w)) return Buy.NotNow;
            if (slot < 0 || slot >= SlotsOpen) return Buy.Locked;
            if (SlotKind[slot] >= 0) return Buy.Taken;
            int cost = Castle.Facilities[kind].Costs[0];
            if (w.Economy.Iron < cost) return Buy.NoFunds;
            w.Economy.Iron -= cost;
            SlotKind[slot] = kind;
            SlotTier[slot] = 1;
            SlotTimer[slot] = Castle.Facilities[kind].Interval;
            Apply(w);
            return Buy.Ok;
        }

        /// <summary>철거. 쓴 흑철을 모두 돌려받는다. 장마다 정해진 횟수만.</summary>
        public Buy Demolish(World w, int slot)
        {
            if (!CanShop(w)) return Buy.NotNow;
            if (slot < 0 || slot >= SlotKind.Length || SlotKind[slot] < 0) return Buy.Missing;
            if (RefundsLeft <= 0) return Buy.NoRefunds;
            RefundsLeft--;
            int[] costs = Castle.Facilities[SlotKind[slot]].Costs;
            for (int t = 0; t < SlotTier[slot]; t++) w.Economy.Iron += costs[t];
            SlotKind[slot] = -1;
            SlotTier[slot] = 0;
            Apply(w);
            return Buy.Ok;
        }

        public Buy UpgradeWall(World w)
        {
            if (!CanShop(w)) return Buy.NotNow;
            if (WallLevel >= Castle.WallCost.Length) return Buy.Maxed;
            if (w.Economy.Iron < Castle.WallCost[WallLevel]) return Buy.NoFunds;
            w.Economy.Iron -= Castle.WallCost[WallLevel];
            WallLevel++;
            Apply(w);
            return Buy.Ok;
        }

        /// <summary>성벽 수리. 흑철 하나에 정해진 양. 가득 찼으면 쓰지 않는다.</summary>
        public Buy Repair(World w)
        {
            if (!CanShop(w)) return Buy.NotNow;
            if (w.Siege.WallHp >= w.Siege.WallMax) return Buy.Maxed;
            if (w.Economy.Iron < 1) return Buy.NoFunds;
            w.Economy.Iron--;
            w.Siege.WallHp = Math.Min(w.Siege.WallMax, w.Siege.WallHp + w.Defs.King.RepairPerIron);
            return Buy.Ok;
        }

        public int Count(string facility)
        {
            int kind = Castle.Facility(facility), n = 0;
            for (int s = 0; s < SlotKind.Length; s++) if (SlotKind[s] == kind) n++;
            return n;
        }

        /// <summary>고른 것들을 왕·곶·성벽의 값으로 다시 적는다. 살 때마다, 그리고 스냅숏을 불러온 뒤에 부른다.</summary>
        public void Apply(World w, bool restoring = false)
        {
            KingDef k = w.Defs.King;
            King king = w.King;
            float speed = 0f, hp = k.Hp, revive = k.Revive;
            int carry = w.Cape != null ? w.Cape.Def.CarryLimit : 0;
            for (int a = 0; a < 4; a++)
            {
                if (KingLevel[a] == 0) continue;
                KingLevelDef l = Castle.King[a][KingLevel[a] - 1];      // 단계의 값은 그 단계까지의 합이다
                if (l.Speed > 0f) speed = l.Speed;
                if (l.Hp > 0f) hp = l.Hp;
                if (l.Carry > 0) carry = l.Carry;
            }
            for (int lv = 0; lv < KingLevel[(int)Axis.Heart]; lv++)
                if (Castle.King[(int)Axis.Heart][lv].Revive > 0f) revive = Castle.King[(int)Axis.Heart][lv].Revive;
            king.SpeedMul = 1f + speed;
            if (!restoring) king.Hp = Math.Max(1f, Math.Min(hp, king.Hp + hp - king.MaxHp));    // 최대 체력이 오른 만큼 지금 체력도 오른다
            king.MaxHp = hp;
            king.ReviveTime = revive;
            king.MomentumSpeed = Sum(Stat.MomentumSpeed);
            king.MomentumMax = Sum(Stat.MomentumTime);
            king.ParryWallHeal = Sum(Stat.ParryWallHeal);
            if (w.Cape != null)
            {
                int nodes = Math.Min((int)Sum(Stat.DigNodes), w.Cape.Def.PowerByNodes.Length - 1);
                w.Cape.DigPower = w.Cape.Def.PowerByNodes[nodes];
                w.Cape.CarryLimit = carry;
            }

            Siege siege = w.Siege;
            float wall = WallLevel > 0 ? Castle.WallHp[WallLevel - 1] : k.WallHp;
            if (!restoring) siege.WallHp = Math.Max(1f, Math.Min(wall, siege.WallHp + wall - siege.WallMax));   // 성벽도 같다
            siege.WallMax = wall;
            siege.FallMul = 1f + Sum(Stat.FallDamage);
            for (int l = 0; l < k.Lanes; l++) siege.ThornDps[l] = siege.ThornSlow[l] = 0f;
            float alarm = 0f;
            for (int s = 0; s < SlotKind.Length; s++)
            {
                if (SlotKind[s] < 0) continue;
                FacilityDef f = Castle.Facilities[SlotKind[s]];
                for (int l = 0; l < k.Lanes; l++)
                {
                    if (!Covers(s, l, k.Lanes)) continue;
                    siege.ThornDps[l] += f.Dps;                                 // 피해는 겹치고
                    siege.ThornSlow[l] = Math.Max(siege.ThornSlow[l], f.Slow);  // 늦추는 것은 겹치지 않는다
                }
                alarm = Math.Max(alarm, f.AlarmBonus);
            }
            w.Cycle.AlarmBonus = alarm;
        }

        public const int Zones = 3;

        /// <summary>칸 s의 시설이 레인 l에 닿는가. 구역은 셋이고 레인을 고르게 나눈다. 칸은 왼쪽 구역부터 돌아가며 놓인다.</summary>
        public bool Covers(int slot, int lane, int lanes)
        {
            if (SlotKind[slot] < 0) return false;
            if (!Castle.Facilities[SlotKind[slot]].ZoneOnly) return true;
            return lane * Zones / lanes == slot % Zones;
        }

        /// <summary>시설의 틱. 가시 난간이 턱밑 띠의 적을 깎고, 발리스타가 가장 높은 적을 쏜다.</summary>
        public void Step(World w, float dt)
        {
            Siege siege = w.Siege;
            KingDef k = w.Defs.King;
            {
                for (int i = 0; i < siege.Enemies.Count; i++)
                {
                    Enemy e = siege.Enemies[i];
                    if (siege.ThornDps[e.Lane] <= 0f) continue;
                    bool inBand = e.State == EnemyState.Mounting || e.State == EnemyState.Raising || (e.State == EnemyState.Climbing && e.Height >= k.CliffHeight - k.ReachBelow);
                    if (inBand) siege.Damage(i, siege.ThornDps[e.Lane] * dt);
                }
            }
            for (int s = 0; s < SlotKind.Length; s++)
            {
                if (SlotKind[s] < 0) continue;
                FacilityDef f = Castle.Facilities[SlotKind[s]];
                if (f.Interval <= 0f) continue;
                SlotTimer[s] -= dt;
                if (SlotTimer[s] > 0.0005f) continue;
                int best = -1;
                float top = -1f;
                for (int i = 0; i < siege.Enemies.Count; i++)
                {
                    Enemy e = siege.Enemies[i];
                    if (e.State == EnemyState.Dead || e.State == EnemyState.Grounded || e.State == EnemyState.Ladder) continue;
                    if (!Covers(s, e.Lane, k.Lanes)) continue;
                    float h = e.State == EnemyState.OnWall ? k.CliffHeight + 1f : e.Height;
                    if (h > top) { top = h; best = i; }
                }
                if (best < 0)
                {
                    SlotTimer[s] = 0f;                  // 쏠 것이 없으면 장전한 채로 기다린다
                    continue;
                }
                siege.Damage(best, f.Damage, true);
                SlotTimer[s] = f.Interval;
                Shots++;
            }
        }

        public void Write(BinaryWriter w)
        {
            for (int i = 0; i < Awake.Length; i++) w.Write(Awake[i]);
            for (int a = 0; a < 4; a++) w.Write(KingLevel[a]);
            for (int s = 0; s < Equip.Length; s++) w.Write(Equip[s]);
            for (int s = 0; s < SlotKind.Length; s++) { w.Write(SlotKind[s]); w.Write(SlotTier[s]); w.Write(SlotTimer[s]); }
            w.Write(WallLevel); w.Write(RefundsLeft); w.Write(Chapter); w.Write(Shots);
        }

        public void Read(BinaryReader r)
        {
            for (int i = 0; i < Awake.Length; i++) Awake[i] = r.ReadBoolean();
            for (int a = 0; a < 4; a++) KingLevel[a] = r.ReadInt32();
            for (int s = 0; s < Equip.Length; s++) Equip[s] = r.ReadInt32();
            for (int s = 0; s < SlotKind.Length; s++) { SlotKind[s] = r.ReadInt32(); SlotTier[s] = r.ReadInt32(); SlotTimer[s] = r.ReadSingle(); }
            WallLevel = r.ReadInt32(); RefundsLeft = r.ReadInt32(); Chapter = r.ReadInt32(); Shots = r.ReadInt32();
        }
    }
}

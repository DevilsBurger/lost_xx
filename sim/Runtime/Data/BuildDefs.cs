using System;
using System.Collections.Generic;

namespace LostRegalia.Sim
{
    /// <summary>노드와 강화가 고치는 값. 데이터의 "stat" 이름과 짝을 이룬다.</summary>
    public enum Stat
    {
        DigNodes = 0,       // 채굴 위력 노드의 수. 타격당 강도는 lands.json의 power_by_nodes에서 찾는다
        LightM,             // 빛의 반경(미터). 해무가 들어오면 쓴다
        MomentumSpeed,      // 여세: 처치한 뒤의 이동 속도 증가
        MomentumTime,       // 여세가 이어지는 시간
        ParryWallHeal,      // 받아치기에 성공하면 회복되는 성벽 내구도
        FallDamage,         // 낙하 피해 증가
        Count
    }

    public enum Axis { Legs = 0, Back = 1, Eyes = 2, Heart = 3 }

    public struct Effect
    {
        public Stat Stat;
        public float Add;
    }

    public enum Delivery : byte { None = 0, Line, Slam, Guard }
    public enum MineShape : byte { None = 0, Line, Box, Circle }

    /// <summary>능동 기술의 값. 노드의 stats와 skill에서 읽는다.</summary>
    public sealed class SkillDef
    {
        public Delivery Delivery;
        public float Damage, Cooldown, Range, Knock;
        public int Pierce;
        public float Width, Air, Hold, Reduce, Window;
        public MineShape MineShape;
        public int MineA, MineB, MinePower;     // 직선: 타일 수 / 상자: 폭, 깊이 / 원: 반지름(타일)
    }

    /// <summary>각성 트리의 노드 하나. data/trees/*.json.</summary>
    public sealed class NodeDef
    {
        public string Id, Name, Branch, Kind;
        public int Tier, Cost, RuneCost;
        public int[] Requires = new int[0];
        public string[] Pair;           // 교차 노드: 두 가지의 이름
        public int Need;                // 교차 노드: 가지마다 깨워야 하는 수
        public Effect[] Effects = new Effect[0];
        public SkillDef Skill;          // 능동 기술이면
    }

    public sealed class TreeDef
    {
        public NodeDef[] Nodes;
        readonly Dictionary<string, int> index = new Dictionary<string, int>();
        static readonly string[] StatNames = { "dig_nodes", "light_m", "momentum_speed", "momentum_time", "parry_wall_heal", "fall_damage" };

        public int Index(string id)
        {
            int i;
            if (!index.TryGetValue(id, out i)) throw new ArgumentException("없는 노드: " + id);
            return i;
        }

        public static TreeDef Load(string json)
        {
            var list = J.Arr(J.Obj(MiniJson.Parse(json))["nodes"]);
            var t = new TreeDef { Nodes = new NodeDef[list.Count] };
            for (int i = 0; i < list.Count; i++) t.index[J.Str(J.Obj(list[i])["id"])] = i;
            for (int i = 0; i < list.Count; i++)
            {
                var o = J.Obj(list[i]);
                var n = new NodeDef();
                n.Id = J.Str(o["id"]);
                n.Name = J.Str(o["name"]);
                n.Branch = J.Str(o["branch"]);
                n.Kind = J.Str(o["kind"]);
                n.Tier = J.Int(o["tier"]);
                n.Cost = J.Int(o["cost"]);
                if (o.ContainsKey("rune")) n.RuneCost = J.Int(o["rune"]);
                var req = J.Arr(o["requires"]);
                n.Requires = new int[req.Count];
                for (int r = 0; r < req.Count; r++) n.Requires[r] = t.Index(J.Str(req[r]));
                if (o.ContainsKey("pair"))
                {
                    var pair = J.Arr(o["pair"]);
                    n.Pair = new[] { J.Str(pair[0]), J.Str(pair[1]) };
                    n.Need = J.Int(o["need"]);
                }
                if (o.ContainsKey("effects"))
                {
                    var fx = J.Arr(o["effects"]);
                    n.Effects = new Effect[fx.Count];
                    for (int e = 0; e < fx.Count; e++)
                    {
                        var f = J.Obj(fx[e]);
                        int s = Array.IndexOf(StatNames, J.Str(f["stat"]));
                        if (s < 0) throw new FormatException("알 수 없는 효과: " + J.Str(f["stat"]));
                        n.Effects[e] = new Effect { Stat = (Stat)s, Add = J.Num(f["add"]) };
                    }
                }
                if (o.ContainsKey("skill"))
                {
                    var s = J.Obj(o["skill"]);
                    var st = J.Obj(o["stats"]);
                    var def = new SkillDef();
                    string delivery = J.Str(s["delivery"]);
                    def.Delivery = delivery == "line" ? Delivery.Line : delivery == "slam" ? Delivery.Slam : delivery == "guard" ? Delivery.Guard : Delivery.None;
                    if (def.Delivery == Delivery.None) throw new FormatException("알 수 없는 전달 방식: " + delivery);
                    def.Damage = J.Num(st["피해"]);
                    def.Cooldown = J.Num(st["재사용"]);
                    def.Range = J.Num(st["사거리"]);
                    def.Knock = J.Num(st["밀어냄"]);
                    def.Pierce = s.ContainsKey("pierce") ? J.Int(s["pierce"]) : 0;
                    def.Width = s.ContainsKey("width") ? J.Num(s["width"]) : 0f;
                    def.Air = s.ContainsKey("air") ? J.Num(s["air"]) : 0f;
                    def.Hold = s.ContainsKey("hold") ? J.Num(s["hold"]) : 0f;
                    def.Reduce = s.ContainsKey("reduce") ? J.Num(s["reduce"]) : 0f;
                    def.Window = s.ContainsKey("window") ? J.Num(s["window"]) : 0f;
                    var m = J.Obj(s["mine"]);
                    string shape = J.Str(m["shape"]);
                    def.MinePower = J.Int(m["power"]);
                    if (shape == "line") { def.MineShape = MineShape.Line; def.MineA = J.Int(m["tiles"]); }
                    else if (shape == "box") { def.MineShape = MineShape.Box; def.MineA = J.Int(m["wide"]); def.MineB = J.Int(m["deep"]); }
                    else { def.MineShape = MineShape.Circle; def.MineA = J.Int(m["tiles"]); }
                    n.Skill = def;
                }
                t.Nodes[i] = n;
            }
            return t;
        }
    }

    public sealed class KingLevelDef
    {
        public int Cost;
        public float Speed, LightM, Hp, Revive;     // 0이면 바꾸지 않는다
        public int Carry;
    }

    public sealed class FacilityDef
    {
        public string Id, Name;
        public int[] Costs;
        public float AlarmBonus, Interval, Damage, Dps, Slow;
        public bool ZoneOnly;           // 참이면 그 칸이 있는 구역의 레인에만 닿는다. 거짓이면 성벽 전체
    }

    /// <summary>성과 왕 강화의 값. data/castle/castle.json.</summary>
    public sealed class CastleDef
    {
        public int[] Slots, FacilityTier, KingLevel, TreeTier;      // 장마다의 상한(0이 1장)
        public int Refunds, SkillSlots;
        public int[] WallCost;
        public float[] WallHp;
        public KingLevelDef[][] King = new KingLevelDef[4][];
        public FacilityDef[] Facilities;
        static readonly string[] AxisNames = { "legs", "back", "eyes", "heart" };

        public int Facility(string id)
        {
            for (int i = 0; i < Facilities.Length; i++) if (Facilities[i].Id == id) return i;
            throw new ArgumentException("없는 시설: " + id);
        }

        static int[] Ints(object o)
        {
            var a = J.Arr(o);
            var r = new int[a.Count];
            for (int i = 0; i < a.Count; i++) r[i] = J.Int(a[i]);
            return r;
        }

        static float Opt(Dictionary<string, object> o, string key)
        {
            return o.ContainsKey(key) ? J.Num(o[key]) : 0f;
        }

        public static CastleDef Load(string json)
        {
            var o = J.Obj(MiniJson.Parse(json));
            var c = new CastleDef();
            var caps = J.Obj(o["caps_by_chapter"]);
            c.Slots = Ints(caps["slots"]);
            c.FacilityTier = Ints(caps["facility_tier"]);
            c.KingLevel = Ints(caps["king_level"]);
            c.TreeTier = Ints(caps["tree_tier"]);
            c.Refunds = J.Int(o["refunds_per_chapter"]);
            c.SkillSlots = J.Int(o["skill_slots"]);
            var ups = J.Arr(J.Obj(o["wall"])["upgrades"]);
            c.WallCost = new int[ups.Count];
            c.WallHp = new float[ups.Count];
            for (int i = 0; i < ups.Count; i++)
            {
                c.WallCost[i] = J.Int(J.Obj(ups[i])["cost"]);
                c.WallHp[i] = J.Num(J.Obj(ups[i])["hp"]);
            }
            var king = J.Obj(o["king"]);
            for (int a = 0; a < 4; a++)
            {
                var levels = J.Arr(king[AxisNames[a]]);
                c.King[a] = new KingLevelDef[levels.Count];
                for (int i = 0; i < levels.Count; i++)
                {
                    var l = J.Obj(levels[i]);
                    c.King[a][i] = new KingLevelDef
                    {
                        Cost = J.Int(l["cost"]), Speed = Opt(l, "speed"), LightM = Opt(l, "light_m"),
                        Hp = Opt(l, "hp"), Revive = Opt(l, "revive"), Carry = (int)Opt(l, "carry")
                    };
                }
            }
            var fs = J.Arr(o["facilities"]);
            c.Facilities = new FacilityDef[fs.Count];
            for (int i = 0; i < fs.Count; i++)
            {
                var f = J.Obj(fs[i]);
                c.Facilities[i] = new FacilityDef
                {
                    Id = J.Str(f["id"]), Name = J.Str(f["name"]), Costs = Ints(f["cost"]),
                    AlarmBonus = Opt(f, "alarm_bonus"), Interval = Opt(f, "interval"), Damage = Opt(f, "damage"),
                    Dps = Opt(f, "dps"), Slow = Opt(f, "slow"),
                    ZoneOnly = f.ContainsKey("cover") && J.Str(f["cover"]) == "zone"
                };
            }
            return c;
        }
    }
}

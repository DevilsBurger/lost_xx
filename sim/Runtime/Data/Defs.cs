using System;
using System.Collections.Generic;

namespace LostRegalia.Sim
{
    public enum Size : byte { Small = 0, Medium = 1, Large = 2 }

    /// <summary>적 하나의 정의. data/combat/enemies.json의 한 항목.</summary>
    public sealed class EnemyDef
    {
        public string Id;
        public string Name;
        public string Route;
        public Size Size;
        public float Cost;
        public float Hp;
        public float Climb;       // 0이면 오르지 않는 적
        public float Grip;
        public float WallDps;
        public float KingDamage;

        public bool Climbs { get { return Climb > 0f; } }
        public bool Perches { get { return Route == "바위턱"; } }
    }

    /// <summary>왕과 전장의 규칙 값. data/combat/king.json.</summary>
    public sealed class KingDef
    {
        public float[] BasicTimes, BasicDamage, BasicKnock;
        public float ChargeTime, ChargeRecover, ChargeDamage, ChargeKnock;
        public float SlamDamage, SlamKnock;
        public float ReachBelow;
        public float Speed;

        public float CliffHeight, LedgeHeight, LaneGap, MountTime;
        public int Lanes;

        public float FallCoef, GripRegen, GripRegenDelay, ReclimbDelay;
        public float FrozenGrip, FrozenKnockTaken;
        public float[] CollideDamage = new float[3];
        public float[] CollideKnock = new float[3];

        public float WallHp;
    }

    /// <summary>
    /// 규칙 데이터 전체. 파일을 직접 읽지 않는다. 호스트(Unity나 테스트)가 JSON 글자를 넘겨 준다.
    /// </summary>
    public sealed class Defs
    {
        public KingDef King;
        public EnemyDef[] Enemies;
        readonly Dictionary<string, int> index = new Dictionary<string, int>();

        public int EnemyIndex(string id)
        {
            int i;
            if (!index.TryGetValue(id, out i)) throw new ArgumentException("없는 적: " + id);
            return i;
        }

        public static Defs Load(string kingJson, string enemiesJson)
        {
            var d = new Defs();
            d.King = LoadKing(J.Obj(MiniJson.Parse(kingJson)));
            var list = J.Arr(MiniJson.Parse(enemiesJson));
            d.Enemies = new EnemyDef[list.Count];
            for (int i = 0; i < list.Count; i++)
            {
                var o = J.Obj(list[i]);
                var e = new EnemyDef();
                e.Id = J.Str(o["id"]);
                e.Name = J.Str(o["name"]);
                e.Route = J.Str(o["route"]);
                e.Size = ParseSize(J.Str(o["size"]));
                e.Cost = J.Num(o["cost"]);
                e.Hp = J.Num(o["hp"]);
                e.Climb = J.Num(o["climb"]);
                e.Grip = J.Num(o["grip"]);
                e.WallDps = J.Num(o["wall_dps"]);
                e.KingDamage = J.Num(o["king_damage"]);
                d.Enemies[i] = e;
                d.index[e.Id] = i;
            }
            return d;
        }

        static Size ParseSize(string s)
        {
            if (s == "소") return Size.Small;
            if (s == "중") return Size.Medium;
            if (s == "대") return Size.Large;
            throw new FormatException("알 수 없는 크기: " + s);
        }

        static KingDef LoadKing(Dictionary<string, object> o)
        {
            var k = new KingDef();
            var basic = J.Obj(o["basic"]);
            k.BasicTimes = J.Nums(basic["times"]);
            k.BasicDamage = J.Nums(basic["damage"]);
            k.BasicKnock = J.Nums(basic["knock"]);
            var ch = J.Obj(o["charged"]);
            k.ChargeTime = J.Num(ch["charge"]);
            k.ChargeRecover = J.Num(ch["recover"]);
            k.ChargeDamage = J.Num(ch["damage"]);
            k.ChargeKnock = J.Num(ch["knock"]);
            var slam = J.Obj(o["slam"]);
            k.SlamDamage = J.Num(slam["damage"]);
            k.SlamKnock = J.Num(slam["knock"]);
            k.ReachBelow = J.Num(o["reach_below"]);
            k.Speed = J.Num(o["speed"]);

            var cliff = J.Obj(o["cliff"]);
            k.CliffHeight = J.Num(cliff["height"]);
            k.LedgeHeight = J.Num(cliff["ledge_height"]);
            k.LaneGap = J.Num(cliff["lane_gap"]);
            k.Lanes = J.Int(cliff["lanes"]);
            k.MountTime = J.Num(cliff["mount_time"]);

            var fall = J.Obj(o["fall"]);
            k.FallCoef = J.Num(fall["coef"]);
            k.GripRegen = J.Num(fall["grip_regen"]);
            k.GripRegenDelay = J.Num(fall["grip_regen_delay"]);
            k.ReclimbDelay = J.Num(fall["reclimb_delay"]);
            k.FrozenGrip = J.Num(fall["frozen_grip"]);
            k.FrozenKnockTaken = J.Num(fall["frozen_knock_taken"]);
            var cd = J.Obj(fall["collide_damage"]);
            var ck = J.Obj(fall["collide_knock"]);
            string[] sizes = { "소", "중", "대" };
            for (int i = 0; i < 3; i++)
            {
                k.CollideDamage[i] = J.Num(cd[sizes[i]]);
                k.CollideKnock[i] = J.Num(ck[sizes[i]]);
            }
            k.WallHp = J.Num(J.Obj(o["wall"])["hp"]);
            return k;
        }
    }
}

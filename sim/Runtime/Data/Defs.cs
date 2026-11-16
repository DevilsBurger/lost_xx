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

    /// <summary>습격 하나의 편성. data/waves/ch*.json의 한 항목.</summary>
    public sealed class WaveDef
    {
        public bool Elite;
        public bool Final;                                      // 총공세
        public List<KeyValuePair<string, int>> Units = new List<KeyValuePair<string, int>>();
    }

    /// <summary>장 하나의 습격들. 정규 습격이 차례로, 총공세가 따로.</summary>
    public sealed class ChapterDef
    {
        public int Chapter;                                     // 1부터
        public WaveDef[] Waves;
        public WaveDef Onslaught;

        public static ChapterDef Load(string wavesJson)
        {
            var o = J.Obj(MiniJson.Parse(wavesJson));
            var c = new ChapterDef { Chapter = J.Int(o["chapter"]) };
            var regular = new List<WaveDef>();
            var list = J.Arr(o["waves"]);
            for (int i = 0; i < list.Count; i++)
            {
                var w = J.Obj(list[i]);
                var def = new WaveDef();
                def.Elite = w.ContainsKey("elite");
                def.Final = w["n"] is string;                   // 번호 대신 이름이 붙은 것이 총공세
                var counts = J.Obj(w["units"]);
                var ids = new List<string>(counts.Keys);
                ids.Sort(StringComparer.Ordinal);               // 사전의 순회 순서에 기대지 않는다
                for (int u = 0; u < ids.Count; u++)
                    def.Units.Add(new KeyValuePair<string, int>(ids[u], J.Int(counts[ids[u]])));
                if (def.Final) c.Onslaught = def; else regular.Add(def);
            }
            c.Waves = regular.ToArray();
            return c;
        }
    }

    /// <summary>왕과 전장의 규칙 값. data/combat/king.json.</summary>
    public sealed class KingDef
    {
        public float[] BasicTimes, BasicDamage, BasicKnock;
        public float ChargeTime, ChargeRecover, ChargeDamage, ChargeKnock;
        public float SlamDamage, SlamKnock;
        public float ReachBelow;
        public float Speed;

        public float Hp, RegenOnWall, HitIFrame, Revive;
        public float DashDistance, DashTime, DashIFrame, DashCooldown;
        public float BasicRadius, BasicArc, ComboGrace, ChargeRadius;
        public float ParryWindow, ParryDamage, ParryRadius, ParryKnock, ParryCooldown;
        public float WallLength, WallWidth;
        public float ReturnMinAway, ReturnMaxAway, ReturnMaxBonus, ReturnDuration, ReturnShockRadius, ReturnShockKnock;
        public float[] SpawnWindow, Calm, Alarm;                // 장마다(0이 1장)
        public float FlawlessBonus;
        public float EnemyReach, EnemyInterval, EnemyStand;     // 성벽 위의 적이 왕을 치는 거리와 간격, 서는 자리

        /// <summary>레인의 x 자리. 레인들은 성벽 가운데에 모여 있다.</summary>
        public float LaneX(int lane)
        {
            return WallLength * 0.5f + (lane - (Lanes - 1) * 0.5f) * LaneGap;
        }

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
            var wall = J.Obj(o["wall"]);
            k.WallHp = J.Num(wall["hp"]);
            k.WallLength = J.Num(wall["length"]);
            k.WallWidth = J.Num(wall["width"]);

            k.Hp = J.Num(o["hp"]);
            k.RegenOnWall = J.Num(o["regen_on_wall"]);
            k.HitIFrame = J.Num(o["hit_iframe"]);
            k.Revive = J.Num(J.Obj(o["downed"])["revive"]);
            var dash = J.Obj(o["dash"]);
            k.DashDistance = J.Num(dash["distance"]);
            k.DashTime = J.Num(dash["time"]);
            k.DashIFrame = J.Num(dash["iframe"]);
            k.DashCooldown = J.Num(dash["cooldown"]);
            k.BasicRadius = J.Num(basic["radius"]);
            k.BasicArc = J.Num(basic["arc"]);
            k.ComboGrace = J.Num(basic["combo_grace"]);
            k.ChargeRadius = J.Num(ch["radius"]);
            var parry = J.Obj(o["parry"]);
            k.ParryWindow = J.Num(parry["window"]);
            k.ParryDamage = J.Num(parry["damage"]);
            k.ParryRadius = J.Num(parry["radius"]);
            k.ParryKnock = J.Num(parry["knock"]);
            k.ParryCooldown = J.Num(parry["cooldown"]);
            var ret = J.Obj(o["return"]);
            k.ReturnMinAway = J.Num(ret["min_away"]);
            k.ReturnMaxAway = J.Num(ret["max_away"]);
            k.ReturnMaxBonus = J.Num(ret["max_bonus"]);
            k.ReturnDuration = J.Num(ret["duration"]);
            k.ReturnShockRadius = J.Num(ret["shock_radius"]);
            k.ReturnShockKnock = J.Num(ret["shock_knock"]);
            var assault = J.Obj(o["assault"]);
            k.SpawnWindow = J.Nums(assault["spawn_window_by_chapter"]);
            k.Calm = J.Nums(assault["calm_by_chapter"]);
            k.Alarm = J.Nums(assault["alarm_by_chapter"]);
            k.FlawlessBonus = J.Num(assault["flawless_bonus"]);
            var melee = J.Obj(o["enemy_melee"]);
            k.EnemyReach = J.Num(melee["reach"]);
            k.EnemyInterval = J.Num(melee["interval"]);
            k.EnemyStand = J.Num(melee["stand"]);
            return k;
        }
    }
}

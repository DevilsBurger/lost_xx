using System;
using System.Collections.Generic;

namespace LostRegalia.Sim
{
    public enum Rock : byte
    {
        Raw = 0,        // 생성 중에만 쓴다. 아직 정해지지 않은 땅
        Sea = 1,
        Ridge = 2,      // 바위산. 부술 수 없다
        Open = 3,       // 빈터, 판 자리
        Soil = 4,       // 흙
        Soft = 5,       // 연암
        Hard = 6,       // 경암
        Black = 7       // 흑암
    }

    public enum Ore : byte { None = 0, Star = 1, Iron = 2, Rune = 3 }

    public enum Feature : byte { None = 0, Entrance, Dike, Crater, Vault, Coffin, Spring, OldRoad }

    public sealed class OreSpec
    {
        public int Total, PerTile;
        public int[] Split;             // 구간별 천분율
    }

    public sealed class VeinRule
    {
        public int ClusterMin, ClusterMax;
        public int[] HostWeight = new int[4];
    }

    /// <summary>땅 하나의 규칙. data/cape/lands.json의 lands 한 항목. 비율은 천분율 정수로 바꿔 둔다.</summary>
    public sealed class LandDef
    {
        public int Id;
        public string Name;
        public int DigNodes, Ridge, Pockets, Springs, Boulders, Crystals, Corruption, TidalShare;
        public int[][] Mix;             // [구간][암반 4종] 천분율
        public OreSpec[] Ores = new OreSpec[4];     // Ore 값으로 찾는다
        public int[] Vaults, Channels, Fog;
        public Rock VaultWall;
        public bool SeaEnd;
    }

    /// <summary>곶의 규칙 전체. data/cape/lands.json.</summary>
    public sealed class CapeDef
    {
        public int Width, Length, Bands, TileM;
        public int ApproachM, GateHalfM;                // 성벽에서 땅 어귀까지의 길, 뒷문의 반폭
        public int[] Hardness = new int[4];
        public float Swing;
        public int[] PowerByNodes;
        public int MinGap, EntranceRows, EntranceCol0, EntranceCol1, VaultSize, CraterSize, CraterRow0, CraterRow1, CoffinSize;
        public int RoadMin, RoadMax, RoadTurn, ChannelWander, RetryLimit;
        public VeinRule[] Veins = new VeinRule[4];
        public LandDef[] Lands;
        public int CarryLimit, CarryFree, CarrySlowPermille, CarrySlowMaxPermille;
        public float DigReach, DigArc, KingRadius, PickupReach;

        public int BandRows { get { return Length / Bands; } }

        static readonly string[] RockNames = { "흙", "연암", "경암", "흑암" };
        static readonly string[] OreNames = { null, "star", "iron", "rune" };

        static int Permille(object o) { return (int)Math.Round((double)o * 1000.0); }

        static Rock ParseRock(string s)
        {
            for (int i = 0; i < 4; i++) if (RockNames[i] == s) return (Rock)(4 + i);
            throw new FormatException("알 수 없는 암반: " + s);
        }

        public static CapeDef Load(string json)
        {
            var o = J.Obj(MiniJson.Parse(json));
            var c = new CapeDef();
            var grid = J.Obj(o["grid"]);
            c.Width = J.Int(grid["width"]);
            c.Length = J.Int(grid["length"]);
            c.Bands = J.Int(grid["bands"]);
            c.TileM = J.Int(grid["tile_m"]);
            c.ApproachM = J.Int(grid["approach_m"]);
            c.GateHalfM = J.Int(grid["gate_half_m"]);
            var hard = J.Obj(o["hardness"]);
            for (int i = 0; i < 4; i++) c.Hardness[i] = J.Int(hard[RockNames[i]]);
            var dig = J.Obj(o["dig"]);
            c.Swing = J.Num(dig["swing"]);
            var pw = J.Nums(dig["power_by_nodes"]);
            c.PowerByNodes = new int[pw.Length];
            for (int i = 0; i < pw.Length; i++) c.PowerByNodes[i] = (int)pw[i];
            c.DigReach = J.Num(dig["reach_m"]);
            c.DigArc = J.Num(dig["arc"]);
            c.KingRadius = J.Num(dig["king_radius_m"]);
            c.PickupReach = J.Num(dig["pickup_reach_m"]);
            var carry = J.Obj(o["carry"]);
            c.CarryLimit = J.Int(carry["limit"]);
            c.CarryFree = J.Int(carry["free"]);
            c.CarrySlowPermille = Permille(carry["slow_per_piece"]);
            c.CarrySlowMaxPermille = Permille(carry["slow_max"]);

            var st = J.Obj(o["structures"]);
            c.MinGap = J.Int(st["min_gap"]);
            var en = J.Obj(st["entrance"]);
            c.EntranceRows = J.Int(en["rows"]);
            c.EntranceCol0 = J.Int(J.Arr(en["cols"])[0]);
            c.EntranceCol1 = J.Int(J.Arr(en["cols"])[1]);
            c.VaultSize = J.Int(J.Obj(st["vault"])["size"]);
            var cr = J.Obj(st["crater"]);
            c.CraterSize = J.Int(cr["size"]);
            c.CraterRow0 = J.Int(J.Arr(cr["rows"])[0]);
            c.CraterRow1 = J.Int(J.Arr(cr["rows"])[1]);
            c.CoffinSize = J.Int(J.Obj(st["coffin"])["size"]);
            var road = J.Obj(st["old_road"]);
            c.RoadMin = J.Int(J.Arr(road["length"])[0]);
            c.RoadMax = J.Int(J.Arr(road["length"])[1]);
            c.RoadTurn = Permille(road["turn"]);
            c.ChannelWander = J.Int(st["channel_wander"]);
            c.RetryLimit = J.Int(o["retry_limit"]);

            var veins = J.Obj(o["veins"]);
            for (int k = 1; k <= 3; k++)
            {
                var v = J.Obj(veins[OreNames[k]]);
                var r = new VeinRule();
                r.ClusterMin = J.Int(J.Arr(v["cluster"])[0]);
                r.ClusterMax = J.Int(J.Arr(v["cluster"])[1]);
                var hw = J.Obj(v["host_weight"]);
                for (int i = 0; i < 4; i++) r.HostWeight[i] = J.Int(hw[RockNames[i]]);
                c.Veins[k] = r;
            }

            var lands = J.Arr(o["lands"]);
            c.Lands = new LandDef[lands.Count];
            for (int n = 0; n < lands.Count; n++)
            {
                var l = J.Obj(lands[n]);
                var d = new LandDef();
                d.Id = J.Int(l["id"]);
                d.Name = J.Str(l["name"]);
                d.DigNodes = J.Int(l["dig_nodes"]);
                d.Ridge = Permille(l["ridge"]);
                d.Pockets = J.Int(l["pockets"]);
                var mix = J.Arr(l["mix"]);
                d.Mix = new int[mix.Count][];
                for (int b = 0; b < mix.Count; b++)
                {
                    var m = J.Obj(mix[b]);
                    d.Mix[b] = new int[4];
                    for (int i = 0; i < 4; i++)
                        if (m.ContainsKey(RockNames[i])) d.Mix[b][i] = Permille(m[RockNames[i]]);
                }
                for (int k = 1; k <= 3; k++)
                {
                    var s = J.Obj(l[OreNames[k]]);
                    var spec = new OreSpec { Total = J.Int(s["total"]), PerTile = J.Int(s["per_tile"]) };
                    var split = J.Arr(s["split"]);
                    spec.Split = new int[split.Count];
                    for (int i = 0; i < split.Count; i++) spec.Split[i] = Permille(split[i]);
                    d.Ores[k] = spec;
                }
                d.Vaults = Ints(l["vaults"]);
                d.VaultWall = ParseRock(J.Str(l["vault_wall"]));
                d.Springs = J.Int(l["springs"]);
                d.Boulders = J.Int(l["boulders"]);
                d.Crystals = J.Int(l["crystals"]);
                d.Corruption = J.Int(l["corruption"]);
                d.Channels = Ints(l["channels"]);
                d.TidalShare = Permille(l["tidal_share"]);
                var fog = J.Arr(l["fog"]);
                d.Fog = new int[fog.Count];
                for (int i = 0; i < fog.Count; i++) d.Fog[i] = Permille(fog[i]);
                d.SeaEnd = (bool)l["sea_end"];
                c.Lands[n] = d;
            }
            return c;
        }

        static int[] Ints(object o)
        {
            var a = J.Arr(o);
            var r = new int[a.Count];
            for (int i = 0; i < a.Count; i++) r[i] = J.Int(a[i]);
            return r;
        }
    }
}

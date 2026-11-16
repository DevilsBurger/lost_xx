using System;
using System.Collections.Generic;
using System.IO;
using LostRegalia.Sim;

/// <summary>
/// 수치를 바꿔 가며 견주는 조정 도구. 테스트가 아니다. 통과와 실패가 없고 표만 낸다.
///   dotnet run --project sim/Tests~/Tests -c Release -- tune [late|carry|bolt|fort|all]
/// 데이터를 읽은 뒤 메모리에서만 값을 바꾼다. data/의 파일은 건드리지 않는다.
/// </summary>
static partial class Program
{
    sealed class Kit
    {
        public Defs Defs;
        public CapeDef Cape;
        public ChapterDef Ch;
        public TreeDef Tree;
        public CastleDef Castle;
    }

    static Kit Load(string root, Action<Kit> tweak = null)
    {
        Func<string, string> read = rel => File.ReadAllText(Path.Combine(root, "data", rel));
        var kit = new Kit
        {
            Defs = Defs.Load(read("combat/king.json"), read("combat/enemies.json")),
            Cape = CapeDef.Load(read("cape/lands.json")),
            Ch = ChapterDef.Load(read("waves/ch1.json")),
            Tree = TreeDef.Load(read("trees/sword.json")),
            Castle = CastleDef.Load(read("castle/castle.json"))
        };
        string b = Environment.GetEnvironmentVariable("TUNE_BASE");     // 고른 묶음 위에서 다음 것을 견줄 때
        if (b != null && b.Contains("A")) Scale(kit, 1.5f, 0.5f);
        if (b != null && b.Contains("C")) kit.Defs.Enemies[kit.Defs.EnemyIndex("crossbow")].Hp = 35f;
        if (tweak != null) tweak(kit);
        return kit;
    }

    /// <summary>
    /// 습격마다 late초 늦게 돌아오는 왕으로 한 장을 치르고, 습격 하나에 성벽이 깎인 양을 잰다.
    /// 성벽은 무너지지 않게 아주 크게 둔다. 값의 모양만 본다.
    /// </summary>
    static float[] Losses(Kit kit, ulong seed, float late, Action<World> setup, out World world)
    {
        var w = new World(kit.Defs, seed);
        w.OpenShop(kit.Tree, kit.Castle);
        w.Cycle.Begin(w, kit.Ch);
        if (setup != null) setup(w);
        w.Siege.WallMax = w.Siege.WallHp = 50000f;
        var loss = new List<float>();
        int guard = 30 * 60 * 80;
        while (guard-- > 0 && w.Cycle.Phase != Phase.Victory && w.Cycle.Phase != Phase.Fallen)
        {
            bool fighting = w.Cycle.Phase == Phase.Assault || w.Cycle.Phase == Phase.Onslaught;
            if (late > 0f)
            {
                if (!fighting && w.King.OnWall && w.Cycle.TimeLeft < w.Cycle.LastCalm - 5f) w.King.Leave();
                if (fighting && !w.King.OnWall && w.Cycle.AssaultTicks >= late * World.TickRate) w.King.Return();
            }
            if (w.Cycle.Wave >= kit.Ch.Waves.Length) w.Cycle.DeliverFragment();
            w.Step(Bot(w));
            for (int i = 0; i < w.Events.Count; i++)
                if (w.Events[i].Kind == EventKind.AssaultEnd) loss.Add(w.Events[i].B);
        }
        world = w;
        return loss.ToArray();
    }

    static string Row(Kit kit, float[] lates, Action<World> setup, bool detail = false)
    {
        var sb = new System.Text.StringBuilder();
        foreach (float late in lates)
        {
            double sum = 0, n = 0, last = 0, taken = 0;
            for (ulong seed = 11; seed <= 13; seed++)
            {
                World w;
                float[] loss = Losses(kit, seed, late, setup, out w);
                for (int i = 0; i < loss.Length && i < 12; i++) { sum += loss[i]; n++; }
                last += loss.Length > 12 ? loss[12] : 0;
                taken += w.King.DamageTaken;
            }
            sb.Append((sum / n).ToString("0").PadLeft(6));
            if (detail) sb.Append("(" + (last / 3).ToString("0") + "," + (taken / 3).ToString("0") + ")");
        }
        return sb.ToString();
    }

    static void Scale(Kit kit, float climb, float wallDps)
    {
        foreach (EnemyDef e in kit.Defs.Enemies)
        {
            e.Climb *= climb;
            e.WallDps *= wallDps;
        }
    }

    static int Tune(string root, string[] args)
    {
        string what = args.Length > 1 ? args[1] : "all";
        if (what == "late" || what == "all") TuneLate(root);
        if (what == "carry" || what == "all") TuneCarry(root);
        if (what == "bolt" || what == "all") TuneBolt(root);
        if (what == "fort" || what == "all") TuneFort(root);
        if (what == "empty" || what == "all") TuneEmpty(root);
        return 0;
    }

    static void TuneLate(string root)
    {
        float[] lates = { 0f, 10f, 20f, 30f, 40f, 50f, 60f };
        Console.WriteLine("[늦는 값] 습격이 시작되고 N초 뒤에 돌아올 때, 정규 습격 하나에 성벽이 깎이는 양 (시드 셋의 평균. 검기 장착)");
        Console.WriteLine("  " + "".PadRight(34) + "   0초   10초   20초   30초   40초   50초   60초");
        Action<World> skills = w => { w.Economy.Star = 100; w.Build.Awaken(w, "F1"); };
        var variants = new List<KeyValuePair<string, Action<Kit>>>
        {
            new KeyValuePair<string, Action<Kit>>("지금 (등반 ×1, 성벽 피해 ×1)", k => { }),
            new KeyValuePair<string, Action<Kit>>("등반 ×1.25", k => Scale(k, 1.25f, 1f)),
            new KeyValuePair<string, Action<Kit>>("등반 ×1.5", k => Scale(k, 1.5f, 1f)),
            new KeyValuePair<string, Action<Kit>>("등반 ×2", k => Scale(k, 2f, 1f)),
            new KeyValuePair<string, Action<Kit>>("등반 ×1.5, 성벽 피해 ×0.5", k => Scale(k, 1.5f, 0.5f)),
            new KeyValuePair<string, Action<Kit>>("등반 ×2, 성벽 피해 ×0.5", k => Scale(k, 2f, 0.5f)),
            new KeyValuePair<string, Action<Kit>>("스폰 창 35 → 20초", k => k.Defs.King.SpawnWindow[0] = 20f),
            new KeyValuePair<string, Action<Kit>>("스폰 창 20초, 성벽 피해 ×0.5", k => { k.Defs.King.SpawnWindow[0] = 20f; Scale(k, 1f, 0.5f); }),
            new KeyValuePair<string, Action<Kit>>("등반 ×1.5, 스폰 창 25초, 피해 ×0.5", k => { k.Defs.King.SpawnWindow[0] = 25f; Scale(k, 1.5f, 0.5f); }),
        };
        foreach (var v in variants)
            Console.WriteLine("  " + v.Key.PadRight(34 - KoreanExtra(v.Key)) + Row(Load(root, v.Value), lates, skills));
    }

    /// <summary>한글은 칸을 둘 차지한다. 표를 맞추려고 센다.</summary>
    static int KoreanExtra(string s)
    {
        int n = 0;
        foreach (char c in s) if (c >= 0x1100) n++;
        return n;
    }

    static void CampaignRow(string name, Kit kit, float linger, float again, Action<World> shop = null)
    {
        double star = 0, wall = 0, wins = 0, fl = 0, sec = 0, n = 0, taken = 0;
        CampaignAgain = again;
        for (ulong seed = 1; seed <= 10; seed++)
        {
            float away;
            int flawless;
            var trips = new List<Trip>();
            World w = Campaign(kit.Defs, kit.Cape, kit.Ch, seed, 2, linger, trips, out away, out flawless, shop, kit.Tree, kit.Castle);
            foreach (Trip t in trips) star += t.Star;
            n += trips.Count;
            wall += w.Siege.WallMax - w.Siege.WallHp; fl += flawless; sec += away; taken += w.King.DamageTaken;
            if (w.Cycle.Phase == Phase.Victory) wins++;
        }
        CampaignAgain = 30f;
        Console.WriteLine("  " + name.PadRight(40 - KoreanExtra(name)) + (star / 10).ToString("0").PadLeft(5) + (n / 10).ToString("0.0").PadLeft(7) + (sec / 600).ToString("0.0").PadLeft(7)
                          + ("-" + (wall / 10).ToString("0")).PadLeft(8) + (fl / 10).ToString("0.0").PadLeft(7) + (taken / 10).ToString("0").PadLeft(7) + wins.ToString("0").PadLeft(5));
    }

    static void TuneCarry(string root)
    {
        Kit kit = Load(root);
        Console.WriteLine("[운반] 시드 7, 채굴 위력 2, 종이 울리면 돌아서는 왕. 나갔다 올 때마다: 가져온 것 / 비운 시간 / 돌아왔을 때 습격까지 남은 시간");
        var trips = new List<Trip>();
        float away;
        int flawless;
        Campaign(kit.Defs, kit.Cape, kit.Ch, 7, 2, 0f, trips, out away, out flawless);
        var sb = new System.Text.StringBuilder("  ");
        foreach (Trip t in trips) sb.Append((t.Star + t.Iron) + "/" + t.Away.ToString("0") + "/" + t.Left.ToString("0") + "  ");
        Console.WriteLine(sb.ToString());

        Console.WriteLine("[한 번 더 나갈까] 채굴 위력 2, 시드 열 개, 수리 없음. '다시'는 고요가 그만큼 넘게 남았으면 또 나간다는 뜻");
        Console.WriteLine("  " + "".PadRight(40) + "성광석  나간 수  비운 분    성벽  흠없음  왕 피해  승리");
        Action<World> f1 = w => { if (!w.Build.Awake[w.Build.Tree.Index("F1")]) { w.Economy.Star += 8; w.Build.Awaken(w, "F1"); } };
        var packs = new List<KeyValuePair<string, Action<Kit>>>
        {
            new KeyValuePair<string, Action<Kit>>("지금", k => { }),
            new KeyValuePair<string, Action<Kit>>("등반 ×1.5", k => Scale(k, 1.5f, 1f)),
            new KeyValuePair<string, Action<Kit>>("등반 ×1.25, 경보 10초", k => { Scale(k, 1.25f, 1f); k.Defs.King.Alarm[0] = 10f; }),
            new KeyValuePair<string, Action<Kit>>("등반 ×1.5, 성벽 피해 ×0.5", k => Scale(k, 1.5f, 0.5f)),
        };
        foreach (var pack in packs)
        {
            Console.WriteLine("  -- " + pack.Key);
            foreach (float again in new[] { 30f, 15f, 0f })
                foreach (float linger in new[] { 0f, 10f, 20f, 30f })
                    CampaignRow("다시 " + again.ToString("0") + "초, 종 뒤 " + linger.ToString("0") + "초 더", Load(root, pack.Value), linger, again, f1);
        }
    }

    static void TuneEmpty(string root)
    {
        Console.WriteLine("[비워 둔 성벽] 첫 습격과 열두째 습격을 통째로 비울 때. 습격이 시작되고 60초 / 90초 / 120초에 성벽이 깎인 양, 함락까지 걸린 시간");
        var packs = new List<KeyValuePair<string, Action<Kit>>>
        {
            new KeyValuePair<string, Action<Kit>>("지금", k => { }),
            new KeyValuePair<string, Action<Kit>>("등반 ×1.5", k => Scale(k, 1.5f, 1f)),
            new KeyValuePair<string, Action<Kit>>("등반 ×1.5, 성벽 피해 ×0.5", k => Scale(k, 1.5f, 0.5f)),
            new KeyValuePair<string, Action<Kit>>("등반 ×1.25, 성벽 피해 ×0.5", k => Scale(k, 1.25f, 0.5f)),
        };
        foreach (var pack in packs)
            foreach (int wave in new[] { 0, 11 })
            {
                Kit kit = Load(root, pack.Value);
                var w = new World(kit.Defs, 7);
                w.King.Leave();
                w.StartAssault(kit.Ch.Waves[wave].Units, kit.Defs.King.SpawnWindow[0]);
                var at = new float[3];
                int fall = -1;
                for (int i = 1; i <= 30 * 400; i++)
                {
                    w.Step();
                    float lost = kit.Defs.King.WallHp - w.Siege.WallHp;
                    if (i == 1800) at[0] = lost;
                    if (i == 2700) at[1] = lost;
                    if (i == 3600) at[2] = lost;
                    if (fall < 0 && w.Siege.WallHp <= 0f) fall = i;
                }
                Console.WriteLine("  " + (pack.Key + ", 습격 " + (wave + 1)).PadRight(40 - KoreanExtra(pack.Key + ", 습격 ")) + at[0].ToString("0").PadLeft(6) + at[1].ToString("0").PadLeft(6)
                                  + at[2].ToString("0").PadLeft(6) + "   함락 " + (fall / 30f).ToString("0") + "초");
            }
    }

    static void TuneBolt(string root)
    {
        Console.WriteLine("[검기와 석궁수] 성벽을 떠나지 않는 왕, 시설 없음, 받아치기 없음. 한 장 동안 왕이 받은 피해 / 쓰러짐 / 날아온 화살");
        float[] zero = { 0f };
        foreach (float hp in new[] { 25f, 35f, 45f })
            foreach (float cd in new[] { 6f, 9f, 12f })
            {
                var sb = new System.Text.StringBuilder("  석궁수 체력 " + hp + ", 검기 재사용 " + cd + "초:");
                foreach (string[] ids in new[] { new string[0], new[] { "F1" } })
                {
                    Kit kit = Load(root, k =>
                    {
                        k.Defs.Enemies[k.Defs.EnemyIndex("crossbow")].Hp = hp;
                        k.Tree.Nodes[k.Tree.Index("F1")].Skill.Cooldown = cd;
                    });
                    World w;
                    Losses(kit, 11, 0f, v => { v.Economy.Star = 100; foreach (string id in ids) v.Build.Awaken(v, id); }, out w);
                    sb.Append((ids.Length == 0 ? "  기본기만 " : "  검기 ") + w.King.DamageTaken.ToString("0") + "/" + w.King.Downs + "/" + w.Siege.Bolts);
                }
                Console.WriteLine(sb.ToString());
            }
    }

    static void TuneFort(string root)
    {
        float[] lates = { 30f, 40f, 50f, 60f, 75f };
        Console.WriteLine("[시설] 습격이 시작되고 N초 뒤에 돌아올 때 정규 습격 하나에 깎이는 양. 끝의 칸은 한 번도 돌아오지 않을 때의 한 장(성벽 1,000, 수리 없음)");
        Console.WriteLine("  " + "".PadRight(46) + "  30초   40초   50초   60초   75초   돌아오지 않음");
        var rules = new List<KeyValuePair<string, Action<Kit>>>
        {
            new KeyValuePair<string, Action<Kit>>("지금 (성벽 전체, 난간 6, 발리스타 40 / 4초)", k => { }),
            new KeyValuePair<string, Action<Kit>>("둘 다 구역만", k => Fort(k, true, 6f, 40f)),
            new KeyValuePair<string, Action<Kit>>("난간 구역 6, 발리스타 전체 8초", k => { Fort(k, true, 6f, 40f); Ballista(k, false, 8f); }),
            new KeyValuePair<string, Action<Kit>>("난간 구역 8, 발리스타 전체 8초", k => { Fort(k, true, 8f, 40f); Ballista(k, false, 8f); }),
            new KeyValuePair<string, Action<Kit>>("난간 구역 8, 발리스타 전체 6초", k => { Fort(k, true, 8f, 40f); Ballista(k, false, 6f); }),
            new KeyValuePair<string, Action<Kit>>("난간 구역 10, 발리스타 전체 8초", k => { Fort(k, true, 10f, 40f); Ballista(k, false, 8f); }),
        };
        var kits = new List<KeyValuePair<string, string[]>>
        {
            new KeyValuePair<string, string[]>("없음", new string[0]),
            new KeyValuePair<string, string[]>("발리스타(가운데)", new[] { null, "ballista" }),
            new KeyValuePair<string, string[]>("가시 난간(가운데)", new[] { null, "thorns" }),
            new KeyValuePair<string, string[]>("발리스타 + 난간", new[] { "ballista", "thorns" }),
            new KeyValuePair<string, string[]>("난간 셋", new[] { "thorns", "thorns", "thorns" }),
            new KeyValuePair<string, string[]>("난간 + 발리스타 + 난간", new[] { "thorns", "ballista", "thorns" }),
        };
        foreach (var rule in rules)
        {
            Console.WriteLine("  -- " + rule.Key);
            foreach (var kitDef in kits)
            {
                Kit kit = Load(root, rule.Value);
                Action<World> setup = w =>
                {
                    w.Economy.Iron = 100;
                    w.Economy.Star = 100;
                    w.Build.Awaken(w, "F1");
                    for (int s = 0; s < kitDef.Value.Length; s++) if (kitDef.Value[s] != null) w.Build.BuildFacility(w, s, kitDef.Value[s]);
                };
                int flawless;
                float worst;
                World never = Chapter(kit.Defs, kit.Ch, 11, 9999f, out flawless, out worst, w => { w.OpenShop(kit.Tree, kit.Castle); setup(w); });
                Console.WriteLine("  " + ("   " + kitDef.Key).PadRight(46 - KoreanExtra(kitDef.Key)) + Row(kit, lates, setup) + "   "
                                  + (never.Cycle.Phase == Phase.Victory ? "-" + (kit.Defs.King.WallHp - never.Siege.WallHp).ToString("0") : "함락(" + (never.Cycle.Wave + 1) + ")"));
            }
        }
    }

    static void Ballista(Kit k, bool zone, float interval)
    {
        FacilityDef f = k.Castle.Facilities[k.Castle.Facility("ballista")];
        f.ZoneOnly = zone;
        f.Interval = interval;
    }

    static void Fort(Kit k, bool zone, float thornDps, float ballista)
    {
        foreach (FacilityDef f in k.Castle.Facilities)
        {
            if (f.Dps > 0f) { f.Dps = thornDps; f.ZoneOnly = zone; }
            if (f.Damage > 0f) { f.Damage = ballista; f.ZoneOnly = zone; }
        }
    }
}

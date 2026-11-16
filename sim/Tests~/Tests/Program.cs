using System;
using System.Collections.Generic;
using System.IO;
using LostRegalia.Sim;

/// <summary>
/// 시뮬레이션 코어의 테스트. 외부 패키지 없이 돈다.
///   dotnet run --project sim/Tests~/Tests
/// 하나라도 실패하면 종료 코드 1.
/// </summary>
static class Program
{
    static int failed, passed;

    static void Check(bool ok, string what)
    {
        if (ok) { passed++; return; }
        failed++;
        Console.WriteLine("  실패: " + what);
    }

    static void Section(string name) { Console.WriteLine("[" + name + "]"); }

    static string Root()
    {
        string dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "data", "combat", "king.json")))
            dir = Path.GetDirectoryName(dir);
        if (dir == null) throw new Exception("저장소의 data/ 폴더를 찾지 못했다");
        return dir;
    }

    static List<KeyValuePair<string, int>> Wave(Dictionary<string, object> wave)
    {
        var list = new List<KeyValuePair<string, int>>();
        foreach (var kv in J.Obj(wave["units"])) list.Add(new KeyValuePair<string, int>(kv.Key, J.Int(kv.Value)));
        return list;
    }

    /// <summary>테스트용 왕. 가장 급한 레인으로 가서 강공격을 한다. 본 게임의 코드가 아니다.</summary>
    sealed class Bot
    {
        float cooldown;
        int lane = 3;

        public void Step(World w)
        {
            cooldown -= World.Dt;
            if (cooldown > 0f) return;
            KingDef k = w.Defs.King;
            var es = w.Siege.Enemies;
            int best = -1;
            float score = -1f;
            for (int i = 0; i < es.Count; i++)
            {
                float s = -1f;
                if (es[i].State == EnemyState.OnWall) s = 100f;
                else if (es[i].State == EnemyState.Mounting) s = 50f;
                else if (es[i].State == EnemyState.Climbing && es[i].Height >= k.CliffHeight - k.ReachBelow) s = es[i].Height;
                if (s > score) { score = s; best = i; }
            }
            if (best < 0) return;
            int target = es[best].Lane;
            float travel = Math.Abs(target - lane) * k.LaneGap / k.Speed;
            lane = target;
            for (int i = 0; i < es.Count; i++)
            {
                if (es[i].Lane != lane) continue;
                bool inBand = es[i].State == EnemyState.Mounting ||
                              (es[i].State == EnemyState.Climbing && es[i].Height >= k.CliffHeight - k.ReachBelow);
                if (inBand) w.Siege.Knock(i, k.ChargeKnock);                          // 밀어냄이 먼저
                if (es[i].State == EnemyState.OnWall || (inBand && es[i].State != EnemyState.Dead && es[i].State != EnemyState.Grounded))
                    w.Siege.Damage(i, k.ChargeDamage);
            }
            cooldown = travel + k.ChargeTime + k.ChargeRecover;
        }
    }

    static World Run(Defs defs, ulong seed, List<KeyValuePair<string, int>> units, float window, bool withBot, int maxTicks, out int ticks)
    {
        var w = new World(defs, seed);
        w.StartAssault(units, window);
        var bot = withBot ? new Bot() : null;
        ticks = 0;
        while (ticks < maxTicks && w.Siege.Active)
        {
            if (bot != null) bot.Step(w);
            w.Step();
            ticks++;
        }
        return w;
    }

    static int Main()
    {
        string root = Root();
        string kingJson = File.ReadAllText(Path.Combine(root, "data", "combat", "king.json"));
        string enemiesJson = File.ReadAllText(Path.Combine(root, "data", "combat", "enemies.json"));
        Defs defs = Defs.Load(kingJson, enemiesJson);
        KingDef k = defs.King;
        var expect = J.Obj(MiniJson.Parse(File.ReadAllText(Path.Combine(root, "data", "expect", "combat.json"))));
        var waves = J.Arr(J.Obj(MiniJson.Parse(File.ReadAllText(Path.Combine(root, "data", "waves", "ch1.json"))))["waves"]);

        Section("데이터 읽기");
        Check(defs.Enemies.Length == 15, "적 15종");
        Check(defs.Enemies[defs.EnemyIndex("climber")].Hp == 30f, "등반병 체력 30");
        Check(defs.Enemies[defs.EnemyIndex("shield")].Grip == 12f, "방패병 붙잡기 12");
        Check(k.Lanes == 6 && k.CliffHeight == 18f, "절벽 18m, 레인 6줄");
        Check(!defs.Enemies[defs.EnemyIndex("siege")].Climbs, "공성병은 오르지 않는다");

        Section("추락 규칙이 문서의 표와 같은가");
        foreach (var kv in J.Obj(expect["fall"]))
            Check(FallRules.FallDamage(k, float.Parse(kv.Key)) == J.Int(kv.Value), "낙하 피해 " + kv.Key + "m");
        string[] sizes = { "small", "medium", "large" };
        string[] codes = { "dies", "falls", "falls_with_passive", "falls_if_frozen", "holds" };
        int compared = 0;
        foreach (var kv in J.Obj(expect["enemies"]))
        {
            EnemyDef e = defs.Enemies[defs.EnemyIndex(kv.Key)];
            var x = J.Obj(kv.Value);
            Check(FallRules.ChargedHitsToDrop(k, e.Grip, false) == J.Int(x["charged_to_drop"]), e.Name + ": 강공격 횟수");
            Check(FallRules.ChargedHitsToDrop(k, e.Grip, true) == J.Int(x["charged_to_drop_frozen"]), e.Name + ": 빙결 시 강공격 횟수");
            var col = J.Obj(x["collide"]);
            for (int s = 0; s < 3; s++)
                Check(codes[(int)FallRules.Collide(k, (Size)s, e)] == J.Str(col[sizes[s]]), e.Name + ": 덮치기 " + sizes[s]);
            compared++;
        }
        Check(compared == 11, "오르는 적 11종을 모두 대조");

        Section("연쇄 추락");
        {
            // 같은 레인에 등반병 셋. 위에서부터 17m, 12m, 6m
            Func<float, World> lane = mul =>
            {
                var w = new World(defs, 1);
                w.Siege.CollideKnockMul = mul;
                int c = defs.EnemyIndex("climber");
                foreach (float h in new[] { 17f, 12f, 6f })
                    w.Siege.Enemies.Add(new Enemy { Def = c, Lane = 2, Height = h, Hp = 30f, Grip = 6f, SinceKnock = 999f });
                w.Siege.Knock(0, k.ChargeKnock);
                return w;
            };
            World a = lane(1f);
            Check(a.Siege.Enemies[0].State == EnemyState.Dead, "맨 위는 떨어져 죽는다");
            Check(a.Siege.Enemies[1].State == EnemyState.Climbing && a.Siege.Enemies[1].Hp == 20f, "기본으로는 아래가 버틴다(피해 10만 받는다)");
            Check(a.Siege.Drops == 1, "기본으로는 연쇄가 없다");
            World b = lane(1.5f);
            Check(b.Siege.Enemies[1].State == EnemyState.Dead, "'굴러떨어지는 것들': 12m의 적도 떨어져 죽는다");
            Check(b.Siege.Enemies[2].State == EnemyState.Grounded, "6m의 적은 떨어지지만 살아남는다");
            Check(b.Siege.Drops == 3 && b.Siege.FallKills == 2, "셋이 떨어지고 둘이 죽는다");
        }

        Section("올라서는 순간");
        {
            var w = new World(defs, 1);
            int sh = defs.EnemyIndex("shield");
            w.Siege.Enemies.Add(new Enemy { Def = sh, Lane = 0, Height = 18f, Hp = 45f, Grip = 12f, State = EnemyState.Mounting, Timer = 2f });
            w.Siege.Knock(0, k.ChargeKnock);
            Check(w.Siege.Enemies[0].State == EnemyState.Dead, "올라서는 방패병은 강공격 한 번에 떨어진다");
        }

        Section("결정성과 되감기");
        var wave1 = Wave(J.Obj(waves[0]));
        var wave12 = Wave(J.Obj(waves[11]));
        int t1, t2, t3;
        ulong h1 = Run(defs, 7, wave12, 35f, true, 6000, out t1).StateHash();
        ulong h2 = Run(defs, 7, wave12, 35f, true, 6000, out t2).StateHash();
        ulong h3 = Run(defs, 8, wave12, 35f, true, 6000, out t3).StateHash();
        Check(h1 == h2 && t1 == t2, "같은 시드는 같은 결과");
        Check(h1 != h3, "다른 시드는 다른 결과");
        {
            var w = new World(defs, 7);
            w.StartAssault(wave12, 35f);
            for (int i = 0; i < 600; i++) w.Step();           // 20초
            byte[] snap = w.Snapshot();
            for (int i = 0; i < 900; i++) w.Step();
            ulong after = w.StateHash();
            w.Restore(snap);
            Check(w.Tick == 600, "되감으면 틱이 돌아간다");
            for (int i = 0; i < 900; i++) w.Step();
            Check(w.StateHash() == after, "되감은 뒤 다시 돌리면 같은 상태에 닿는다");
            Console.WriteLine("  스냅숏 크기: " + snap.Length + "바이트 (적 " + w.Siege.Enemies.Count + "기)");
        }

        Section("습격 한 번");
        {
            int ticks;
            World alone = Run(defs, 7, wave1, 35f, false, 30 * 120, out ticks);
            float lostAlone = k.WallHp - alone.Siege.WallHp;
            World kept = Run(defs, 7, wave1, 35f, true, 30 * 120, out ticks);
            float lostKept = k.WallHp - kept.Siege.WallHp;
            Check(lostAlone > 300f, "아무도 없으면 성벽이 크게 깎인다");
            Check(lostKept < lostAlone * 0.2f, "지키면 훨씬 덜 깎인다");
            Check(!kept.Siege.Active && kept.Siege.Kills == 10, "지키면 열 기를 모두 처치한다");
            Console.WriteLine("  1장 첫 습격: 비워 두면 2분 동안 성벽 -" + lostAlone.ToString("0") + ", 지키면 -" + lostKept.ToString("0")
                              + " (" + (ticks / 30f).ToString("0") + "초에 끝, 추락사 " + kept.Siege.FallKills + ")");
            World last = Run(defs, 7, wave12, 35f, true, 30 * 180, out ticks);
            Console.WriteLine("  1장 열두째 습격: 지키면 성벽 -" + (k.WallHp - last.Siege.WallHp).ToString("0")
                              + " (" + (ticks / 30f).ToString("0") + "초, 처치 " + last.Siege.Kills + ", 추락사 " + last.Siege.FallKills + ")");
        }

        Section("성능");
        {
            // 적 200기를 한꺼번에 올려 놓고 틱 하나에 걸리는 시간을 잰다
            var w = new World(defs, 3);
            w.StartAssault(new List<KeyValuePair<string, int>> { new KeyValuePair<string, int>("climber", 200) }, 5f);
            for (int i = 0; i < 300; i++) w.Step();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            const int N = 3000;
            for (int i = 0; i < N; i++) w.Step();
            double us = sw.Elapsed.TotalMilliseconds * 1000.0 / N;
            Check(us < 500.0, "적 200기일 때 틱 하나가 0.5ms 안에 끝난다");
            Console.WriteLine("  적 200기, 틱 하나에 " + us.ToString("0.0") + "마이크로초 (틱 예산 33,333마이크로초)");
        }

        Console.WriteLine();
        Console.WriteLine(failed == 0 ? "통과 " + passed + "개. 실패 없음." : "통과 " + passed + "개, 실패 " + failed + "개.");
        return failed == 0 ? 0 : 1;
    }
}

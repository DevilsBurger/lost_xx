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

    /// <summary>
    /// 테스트용 왕. 본 게임의 코드가 아니다. 상태를 읽고 입력만 만든다. 받아치기와 대시는 쓰지 않는다.
    /// 가장 급한 적의 레인으로 걸어가 강공격을 모아 친다.
    /// </summary>
    static InputFrame Bot(World w)
    {
        KingDef k = w.Defs.King;
        var es = w.Siege.Enemies;
        var f = new InputFrame { AimY = -1f };
        int best = -1;
        float score = -1f;
        for (int i = 0; i < es.Count; i++)
        {
            float s = -1f;
            if (es[i].State == EnemyState.OnWall) s = 100f;
            else if (es[i].State == EnemyState.Mounting) s = 50f;
            else if (es[i].State == EnemyState.Climbing) s = es[i].Height;
            if (s > score) { score = s; best = i; }
        }
        if (best < 0) return f;
        float dx = k.LaneX(es[best].Lane) - w.King.X;
        float dy = 1.5f - w.King.Y;
        if (Math.Abs(dx) > 0.3f) f.MoveX = Math.Sign(dx);
        if (Math.Abs(dy) > 0.2f) f.MoveY = Math.Sign(dy);
        bool reachable = es[best].State != EnemyState.Climbing || es[best].Height >= k.CliffHeight - k.ReachBelow;
        bool ready = w.King.Charge >= k.ChargeTime && Math.Abs(dx) <= 1.5f && reachable;
        if (!ready) f.Held = Buttons.Attack;        // 모은다. 다 모았고 닿으면 뗀다
        return f;
    }

    static World Run(Defs defs, ulong seed, List<KeyValuePair<string, int>> units, float window, bool withBot, int maxTicks, out int ticks)
    {
        var w = new World(defs, seed);
        w.King.OnWall = withBot;                    // 지키지 않는 판에서는 왕이 성벽에 없다
        w.StartAssault(units, window);
        ticks = 0;
        while (ticks < maxTicks && w.Siege.Active)
        {
            if (withBot) w.Step(Bot(w)); else w.Step();
            ticks++;
        }
        return w;
    }

    static void Steps(World w, int n, InputFrame f)
    {
        for (int i = 0; i < n; i++) w.Step(f);
    }

    static void Tap(World w, Buttons b)
    {
        w.Step(new InputFrame { Held = b });
        w.Step(new InputFrame());
    }

    static void Put(World w, string id, int lane, EnemyState state, float height)
    {
        EnemyDef d = w.Defs.Enemies[w.Defs.EnemyIndex(id)];
        w.Siege.Enemies.Add(new Enemy { Def = w.Defs.EnemyIndex(id), Lane = lane, Height = height, Hp = d.Hp, Grip = d.Grip, SinceKnock = 999f, State = state });
    }

    static void KingTests(Defs defs)
    {
        KingDef k = defs.King;
        float mid = k.WallLength * 0.5f;
        Func<World> at = () =>
        {
            var w = new World(defs, 1);
            w.King.X = k.LaneX(2);
            w.King.Y = 2f;
            return w;
        };

        Section("왕: 이동과 대시");
        {
            var w = new World(defs, 1);
            Check(w.King.X == mid && w.King.Hp == k.Hp, "성벽 가운데에서 가득 찬 체력으로 시작한다");
            Steps(w, 30, new InputFrame { MoveX = 1f });
            Check(Math.Abs(w.King.X - (mid + k.Speed)) < 0.01f, "1초에 6m를 걷는다");
            Steps(w, 30, new InputFrame { MoveX = 3f, MoveY = 4f });
            Check(Math.Abs(w.King.X - (mid + k.Speed + k.Speed * 0.6f)) < 0.01f, "입력이 1을 넘어도 속도는 같다");
            Steps(w, 300, new InputFrame { MoveX = 1f, MoveY = -1f });
            Check(w.King.X == k.WallLength && w.King.Y == 0f, "성벽 밖으로 나가지 않는다");

            w = new World(defs, 1);
            w.Step(new InputFrame { MoveX = -1f, Held = Buttons.Dash });
            Check(w.King.Mode == KingMode.Dashing && w.King.IFrame > 0f, "대시하는 동안 무적이다");
            Steps(w, 5, new InputFrame { Held = Buttons.Dash });
            Check(Math.Abs(w.King.X - (mid - k.DashDistance)) < 0.01f && w.King.Mode == KingMode.Normal, "대시는 7m를 간다");
            float x = w.King.X;
            Tap(w, Buttons.Dash);
            Check(w.King.X == x, "재사용 대기 중에는 대시가 나가지 않는다");
            Steps(w, 60, new InputFrame());
            w.Step(new InputFrame { Held = Buttons.Dash });
            Check(w.King.Mode == KingMode.Dashing && w.King.DashDY == -1f, "2초 뒤에 다시 나간다. 서 있으면 조준한 쪽으로");
        }

        Section("왕: 연타");
        {
            var w = at();
            Put(w, "porter", 2, EnemyState.OnWall, k.CliffHeight);
            float hp = w.Siege.Enemies[0].Hp;
            Tap(w, Buttons.Attack);
            Check(w.Siege.Enemies[0].Hp == hp - k.BasicDamage[0], "첫 타 12");
            Tap(w, Buttons.Attack);
            Check(w.Siege.Enemies[0].Hp == hp - k.BasicDamage[0], "동작이 끝나기 전에는 다음 타가 나가지 않는다");
            Steps(w, 12, new InputFrame());
            Check(w.Siege.Enemies[0].Hp == hp - 24f && w.King.Swings == 2, "미리 누른 한 번은 동작이 끝나면 나간다");
            Steps(w, 10, new InputFrame());
            Tap(w, Buttons.Attack);
            Check(w.Siege.Enemies[0].Hp == hp - 44f, "셋째 타 20");
            Steps(w, 60, new InputFrame());
            Tap(w, Buttons.Attack);
            Check(w.Siege.Enemies[0].Hp == hp - 56f, "쉬었다 치면 첫 타로 돌아간다");

            w = at();
            Put(w, "porter", 2, EnemyState.OnWall, k.CliffHeight);
            for (int i = 0; i < 63; i++) w.Step(new InputFrame { Held = i % 2 == 0 ? Buttons.Attack : Buttons.None });
            Check(w.King.Swings == 6, "두 틱에 한 번씩 눌러 대면 2.1초에 여섯 타(연타 두 바퀴)");

            w = at();
            Put(w, "climber", 2, EnemyState.Climbing, 16f);
            Put(w, "climber", 2, EnemyState.Climbing, 14f);
            Put(w, "climber", 4, EnemyState.Climbing, 17f);
            Tap(w, Buttons.Attack);
            Check(w.Siege.Enemies[0].Hp == 18f, "턱밑 띠의 적에게 닿는다");
            Check(w.Siege.Enemies[1].Hp == 30f, "띠 아래의 적에게는 닿지 않는다");
            Check(w.Siege.Enemies[2].Hp == 30f, "두 레인 옆의 적에게는 닿지 않는다");
            w.Step(new InputFrame { AimY = 1f, Held = Buttons.Attack });
            Steps(w, 12, new InputFrame { AimY = 1f });
            Check(w.Siege.Enemies[0].Hp == 18f, "등 뒤는 베지 못한다");
        }

        Section("왕: 강공격");
        {
            var w = at();
            Put(w, "climber", 2, EnemyState.Climbing, 17f);
            Put(w, "climber", 2, EnemyState.Climbing, 10f);
            Steps(w, 17, new InputFrame { Held = Buttons.Attack });
            Steps(w, 1, new InputFrame());
            Check(w.Siege.Enemies[0].Hp == 18f, "덜 모으고 떼면 연타가 나간다");
            Steps(w, 30, new InputFrame());
            Steps(w, 19, new InputFrame { Held = Buttons.Attack });
            Check(w.Siege.Drops == 0, "누르고 있는 동안에는 나가지 않는다");
            Steps(w, 1, new InputFrame());
            Check(w.Siege.Enemies[0].State == EnemyState.Dead && w.Siege.FallKills == 1, "0.6초를 모아 떼면 등반병이 떨어져 죽는다");
            Check(w.Siege.Enemies[1].Hp == 20f, "떨어진 적이 아래의 적을 덮친다");

            w = at();
            Put(w, "climber", 2, EnemyState.OnWall, k.CliffHeight);
            Put(w, "shield", 2, EnemyState.OnWall, k.CliffHeight);
            Steps(w, 19, new InputFrame { Held = Buttons.Attack, AimY = 1f });
            Steps(w, 1, new InputFrame());
            Check(w.Siege.Enemies[0].State == EnemyState.Dead && w.Siege.FallKills == 1, "성벽 위의 등반병은 강공격에 벼랑 밖으로 떨어진다");
            Check(w.Siege.Enemies[1].State == EnemyState.OnWall && w.Siege.Enemies[1].Hp == 45f - k.ChargeDamage, "방패병은 버티고 피해만 받는다. 강공격은 방향을 가리지 않는다");
        }

        Section("왕: 맞기, 받아치기, 쓰러짐");
        {
            var w = at();
            Put(w, "climber", 2, EnemyState.OnWall, k.CliffHeight);
            Steps(w, 35, new InputFrame());
            Check(w.King.Hp == k.Hp && w.Siege.WallHp == k.WallHp, "왕이 곁에 있으면 적은 성벽 대신 왕을 노린다");
            Steps(w, 2, new InputFrame());
            Check(w.King.Hp < k.Hp && w.King.Hp >= k.Hp - 8f && w.King.DamageTaken == 8f, "1.2초마다 한 번 친다(등반병 8)");

            w = at();
            Put(w, "climber", 2, EnemyState.OnWall, k.CliffHeight);
            Put(w, "climber", 3, EnemyState.OnWall, k.CliffHeight);
            Steps(w, 37, new InputFrame());
            Check(w.King.DamageTaken == 8f, "둘이 함께 쳐도 한 번만 맞는다(피격 후 무적)");

            w = at();
            Put(w, "climber", 2, EnemyState.OnWall, k.CliffHeight);
            Put(w, "shield", 3, EnemyState.Climbing, 17f);
            w.King.X += 1.5f;
            w.King.Y = 1f;
            Steps(w, 33, new InputFrame());
            w.Step(new InputFrame { Held = Buttons.Guard });
            Steps(w, 4, new InputFrame());
            Check(w.King.Parries == 1 && w.King.DamageTaken == 0f, "맞는 순간에 받아치면 피해가 없다");
            Check(w.Siege.Enemies[0].State == EnemyState.Dead, "반격파가 친 적을 벼랑 밖으로 떨어뜨린다");
            Check(w.Siege.Enemies[1].Grip == 12f - k.ParryKnock && w.Siege.Enemies[1].Hp == 45f - k.ParryDamage, "반격파는 옆 레인의 턱밑 띠에도 닿는다");

            w = at();
            Put(w, "climber", 2, EnemyState.OnWall, k.CliffHeight);
            w.Step(new InputFrame { Held = Buttons.Guard });
            Steps(w, 40, new InputFrame());
            Check(w.King.Parries == 0 && w.King.DamageTaken == 8f, "너무 일찍 누르면 맞는다");

            w = at();
            Put(w, "porter", 2, EnemyState.OnWall, k.CliffHeight);
            w.King.Hp = 10f;
            Steps(w, 37, new InputFrame { MoveX = 1f });
            w.King.X = k.LaneX(2);
            Steps(w, 40, new InputFrame());
            Check(w.King.Mode == KingMode.Downed && w.King.Downs == 1, "체력이 다하면 쓰러진다");
            float wall = w.Siege.WallHp;
            Steps(w, 60, new InputFrame { MoveX = 1f, Held = Buttons.Attack });
            Check(w.King.X == k.LaneX(2) && w.King.Swings == 0, "쓰러져 있는 동안에는 조작이 없다");
            Check(w.Siege.WallHp < wall, "그동안 적은 성벽을 깎는다");
            Steps(w, 125, new InputFrame());
            Check(w.King.Mode == KingMode.Normal && w.King.Hp >= k.Hp - 16f && w.King.Downs == 1, "6초 뒤에 가득 찬 체력으로 일어난다");

            w = at();
            w.King.OnWall = false;
            Put(w, "climber", 2, EnemyState.OnWall, k.CliffHeight);
            Steps(w, 60, new InputFrame { MoveX = 1f });
            Check(w.King.DamageTaken == 0f && w.Siege.WallHp < k.WallHp, "성벽을 비우면 적은 성벽을 깎는다");
        }

        Section("왕: 입력으로 되감기");
        {
            var w = new World(defs, 5);
            w.StartAssault(new List<KeyValuePair<string, int>> { new KeyValuePair<string, int>("climber", 12) }, 20f);
            for (int i = 0; i < 900; i++) w.Step(Bot(w));
            byte[] snap = w.Snapshot();
            for (int i = 0; i < 600; i++) w.Step(Bot(w));
            ulong after = w.StateHash();
            w.Restore(snap);
            for (int i = 0; i < 600; i++) w.Step(Bot(w));
            Check(w.StateHash() == after, "왕이 싸우는 도중의 스냅숏으로 되돌려도 같은 상태에 닿는다");
        }
    }

    /// <summary>장 하나를 처음부터 끝까지. late초만큼 습격마다 늦게 돌아온다(0이면 성벽을 떠나지 않는다).</summary>
    static World Chapter(Defs defs, ChapterDef ch, ulong seed, float late, out int flawless, out float worst)
    {
        var w = new World(defs, seed);
        w.Cycle.Begin(w, ch);
        flawless = 0;
        worst = 0f;
        int guard = 30 * 60 * 60;
        while (guard-- > 0 && w.Cycle.Phase != Phase.Victory && w.Cycle.Phase != Phase.Fallen)
        {
            bool fighting = w.Cycle.Phase == Phase.Assault || w.Cycle.Phase == Phase.Onslaught;
            if (late > 0f)
            {
                // 고요가 시작되면 나가고, 습격이 시작된 뒤 late초에 돌아온다
                if (!fighting && w.King.OnWall && w.Cycle.TimeLeft < w.Cycle.LastCalm - 5f) w.King.Leave();
                if (fighting && !w.King.OnWall && w.Cycle.AssaultTicks >= late * World.TickRate) w.King.Return();
            }
            if (w.Cycle.Wave >= ch.Waves.Length) w.Cycle.DeliverFragment();
            w.Step(Bot(w));
            for (int i = 0; i < w.Events.Count; i++)
                if (w.Events[i].Kind == EventKind.AssaultEnd)
                {
                    flawless += w.Events[i].C;
                    worst = Math.Max(worst, w.Events[i].B);
                }
        }
        return w;
    }

    static void CycleTests(Defs defs, ChapterDef ch)
    {
        KingDef k = defs.King;

        Section("왕의 귀환");
        {
            var w = new World(defs, 1);
            w.King.Leave();
            Steps(w, 30 * 5, new InputFrame { MoveX = 1f });
            w.King.Return();
            Check(w.King.ReturnBonus == 0f && !w.King.ShockReady && w.King.X == k.WallLength * 0.5f, "10초 안에 돌아오면 아무 일도 없다. 비운 동안에는 성벽 위에서 움직이지 않는다");
            w.King.Leave();
            Steps(w, 30 * 35, new InputFrame());
            w.King.Return();
            Check(Math.Abs(w.King.ReturnBonus - 0.3f) < 0.01f, "35초를 비우면 +30%");
            w.King.Leave();
            Steps(w, 30 * 90, new InputFrame());
            w.King.Return();
            Check(Math.Abs(w.King.ReturnBonus - k.ReturnMaxBonus) < 0.001f, "60초를 넘게 비워도 +60%가 끝이다");

            w.King.X = k.LaneX(2);
            w.King.Y = 2f;
            Put(w, "shield", 2, EnemyState.Climbing, 17f);
            Put(w, "shield", 3, EnemyState.Climbing, 17f);
            Put(w, "porter", 2, EnemyState.OnWall, k.CliffHeight);
            float hp = w.Siege.Enemies[2].Hp;
            Tap(w, Buttons.Attack);
            Check(w.Siege.Enemies[0].State == EnemyState.Dead && w.Siege.Enemies[1].State == EnemyState.Dead,
                  "돌아온 첫 일격의 충격파(8 × 1.6 = 12.8)가 옆 레인의 방패병까지 떨어뜨린다");
            Check(Math.Abs(w.Siege.Enemies[2].Hp - (hp - k.BasicDamage[0] * 1.6f)) < 0.01f, "피해도 60% 는다");
            Check(!w.King.ShockReady, "충격파는 한 번뿐이다");
            Steps(w, 30 * 5, new InputFrame());
            Check(w.King.ReturnBonus == 0f, "5초 뒤에 꺼진다");
        }

        Section("주기");
        {
            Check(ch.Waves.Length == 12 && ch.Onslaught != null && ch.Waves[5].Elite, "1장: 정규 습격 12, 총공세 1, 여섯째는 엘리트");
            var w = new World(defs, 3);
            w.Cycle.Begin(w, ch);
            Check(w.Cycle.Phase == Phase.Calm && w.Cycle.TimeLeft == 100f, "고요 100초로 시작한다");
            int bell = -1, start = -1;
            for (int i = 0; i < 30 * 101 && start < 0; i++)
            {
                w.Step(Bot(w));
                for (int e = 0; e < w.Events.Count; e++)
                {
                    if (w.Events[e].Kind == EventKind.Bell) bell = w.Tick;
                    if (w.Events[e].Kind == EventKind.AssaultStart) start = w.Tick;
                }
            }
            Check(Math.Abs(bell - 30 * 85) <= 2, "85초에 종이 울린다");
            Check(Math.Abs(start - 30 * 100) <= 2 && w.Cycle.Phase == Phase.Assault, "100초에 습격이 시작된다");
            Check(w.Siege.Queue.Count + w.Siege.Enemies.Count == 10, "첫 습격은 등반병 10");
            SimEvent end = default(SimEvent);
            bool ended = false;
            for (int i = 0; i < 30 * 120 && !ended; i++)
            {
                w.Step(Bot(w));
                for (int e = 0; e < w.Events.Count; e++)
                    if (w.Events[e].Kind == EventKind.AssaultEnd) { end = w.Events[e]; ended = true; }
            }
            Check(ended && end.A == 1 && end.B == 0 && end.C == 1, "적이 다 사라지면 습격이 끝난다. 성벽이 깎이지 않았으면 흠 없는 방어");
            Check(w.Cycle.Phase == Phase.Calm && w.Cycle.TimeLeft == 110f && w.Cycle.Wave == 1, "흠 없는 방어 뒤의 고요는 10초 길다");
            Check(w.Siege.Enemies.Count == 0, "습격이 끝나면 죽은 적을 치운다");

            // 스냅숏: 고요 한가운데에서 떠서 다음 습격이 끝난 뒤까지 돌린 것과 견준다
            Steps(w, 30 * 40, new InputFrame());
            byte[] snap = w.Snapshot();
            for (int i = 0; i < 30 * 150; i++) w.Step(Bot(w));
            ulong after = w.StateHash();
            int wave = w.Cycle.Wave;
            w.Restore(snap);
            Check(w.Cycle.Wave == 1 && w.Cycle.Phase == Phase.Calm, "되감으면 주기도 돌아간다");
            for (int i = 0; i < 30 * 150; i++) w.Step(Bot(w));
            Check(w.StateHash() == after && w.Cycle.Wave == wave && wave == 2, "되감은 뒤 다시 돌리면 같은 습격을 같은 결과로 치른다");

            // 함락
            w = new World(defs, 3);
            w.Cycle.Begin(w, ch);
            w.King.Leave();
            bool fell = false;
            int guard = 30 * 600;
            while (guard-- > 0 && w.Cycle.Phase != Phase.Fallen)
            {
                w.Step();
                for (int e = 0; e < w.Events.Count; e++) fell |= w.Events[e].Kind == EventKind.Fallen;
            }
            Check(w.Cycle.Phase == Phase.Fallen && fell && w.Cycle.Wave == 0, "성벽을 비워 두면 첫 습격에 함락된다");
            int tick = w.Tick;
            w.Step();
            Check(w.Tick == tick, "함락된 뒤에는 틱이 나아가지 않는다");
            Console.WriteLine("  비워 두면 " + (tick / 30f).ToString("0") + "초에 함락 (첫 습격이 시작되고 " + (tick / 30f - 100f).ToString("0") + "초)");
        }

        Section("1장을 처음부터 끝까지");
        {
            int flawless;
            float worst;
            World w = Chapter(defs, ch, 11, 0f, out flawless, out worst);
            Check(w.Cycle.Phase == Phase.Victory && w.Cycle.Wave == 12, "성벽을 떠나지 않으면 열두 습격과 총공세를 막고 이긴다");
            Console.WriteLine("  떠나지 않는 왕: " + (w.Tick / 30f / 60f).ToString("0.0") + "분, 성벽 -" + (k.WallHp - w.Siege.WallHp).ToString("0")
                              + ", 흠 없는 방어 " + flawless + "/13, 왕이 받은 피해 " + w.King.DamageTaken.ToString("0") + ", 쓰러짐 " + w.King.Downs
                              + ", 물러난 석궁수 " + w.Cycle.Retreated);
            ulong h = w.StateHash();
            Check(Chapter(defs, ch, 11, 0f, out flawless, out worst).StateHash() == h, "같은 시드의 한 장은 같은 결과로 끝난다");

            Console.WriteLine("  습격마다 늦게 돌아오는 왕 (수리 없음. 성벽 1,000으로 한 장을 버틴다)");
            foreach (float late in new[] { 10f, 20f, 30f, 40f, 50f })
            {
                w = Chapter(defs, ch, 11, late, out flawless, out worst);
                Console.WriteLine("    " + late.ToString("0") + "초 늦음: " + (w.Cycle.Phase == Phase.Victory ? "승리" : "함락(습격 " + (w.Cycle.Wave + 1) + ")")
                                  + ", 성벽 -" + (k.WallHp - w.Siege.WallHp).ToString("0") + ", 한 습격에 가장 크게 -" + worst.ToString("0")
                                  + ", 흠 없는 방어 " + flawless + "/13, 왕이 받은 피해 " + w.King.DamageTaken.ToString("0") + ", 쓰러짐 " + w.King.Downs);
            }
        }
    }

    static string Ascii(Land L)
    {
        var sb = new System.Text.StringBuilder();
        for (int y = L.H - 1; y >= 0; y--)
        {
            for (int x = 0; x < L.W; x++)
            {
                int i = L.Index(x, y);
                char c;
                if (L.Feat[i] == Feature.Crater) c = 'O';
                else if (L.Feat[i] == Feature.Vault) c = 'V';
                else if (L.Feat[i] == Feature.Entrance) c = 'E';
                else if (L.OreKind[i] == Ore.Star) c = '*';
                else if (L.OreKind[i] == Ore.Iron) c = 'i';
                else if (L.OreKind[i] == Ore.Rune) c = 'r';
                else c = "?~# .:xX"[(int)L.Rocks[i]];
                sb.Append(c);
            }
            sb.Append('\n');
        }
        return sb.ToString();
    }

    static void LandTests(CapeDef cape)
    {
        Section("곶: 땅 만들기");
        Check(cape.Lands.Length == 5 && cape.Width == 24 && cape.Length == 36, "땅 다섯, 24 × 36타일");
        Check(cape.Lands[0].Ores[(int)Ore.Star].Total == 170 && cape.Lands[0].Ores[(int)Ore.Iron].Total == 60, "첫 땅의 매장량 170 / 60");
        const int Seeds = 200;
        int bad = 0, retries = 0, worst = 0;
        string first = null;
        var mix = new long[5, 3, 4];
        var rocks = new long[5, 3];
        long ridge = 0, land = 0, passes = 0;
        for (int li = 0; li < cape.Lands.Length; li++)
            for (ulong seed = 1; seed <= Seeds; seed++)
            {
                Land L = LandGen.GenerateValid(cape, cape.Lands[li], seed);
                var errors = LandGen.Validate(L);
                if (errors.Count > 0) { bad++; if (first == null) first = "땅 " + (li + 1) + " 시드 " + seed + ": " + errors[0]; }
                retries += L.Attempt;
                worst = Math.Max(worst, L.Attempt);
                passes += L.Passes;
                for (int i = 0; i < L.Rocks.Length; i++)
                {
                    if (L.Rocks[i] != Rock.Sea) land++;
                    if (L.Rocks[i] == Rock.Ridge) ridge++;
                    if (Land.IsRock(L.Rocks[i]) && !L.Reserved[i])
                    {
                        mix[li, L.BandOf(i / L.W), (int)L.Rocks[i] - 4]++;
                        rocks[li, L.BandOf(i / L.W)]++;
                    }
                    if (L.Rocks[i] == Rock.Raw) bad++;
                }
            }
        Check(bad == 0, "시드 200개 × 다섯 땅이 모두 검증을 통과한다" + (first != null ? " (" + first + ")" : ""));
        Console.WriteLine("  다시 만든 횟수: 1,000개 가운데 합계 " + retries + ", 가장 많이 " + worst + "번. 바위산 " + (100.0 * ridge / land).ToString("0.0")
                          + "%, 뚫은 고개 평균 " + (passes / 1000.0).ToString("0.0") + "타일");
        bool close = true;
        for (int li = 0; li < 5; li++)
            for (int b = 0; b < 3; b++)
                for (int k = 0; k < 4; k++)
                    if (Math.Abs(1000.0 * mix[li, b, k] / rocks[li, b] - cape.Lands[li].Mix[b][k]) > 40.0) close = false;
        Check(close, "암반의 비율이 규칙에서 4%p 안에 있다");
        Console.WriteLine("  첫 땅의 암반(흙/연암/경암): 구간 1 " + Pct(mix, rocks, 0, 0) + ", 구간 2 " + Pct(mix, rocks, 0, 1) + ", 구간 3 " + Pct(mix, rocks, 0, 2));

        Land a = LandGen.GenerateValid(cape, cape.Lands[0], 7), b2 = LandGen.GenerateValid(cape, cape.Lands[0], 7), c2 = LandGen.GenerateValid(cape, cape.Lands[0], 8);
        Check(Ascii(a) == Ascii(b2), "같은 시드는 같은 땅");
        Check(Ascii(a) != Ascii(c2), "다른 시드는 다른 땅");
        Check(a.At(a.EntranceX, 0) == Rock.Open && a.BandOf(a.CraterY) == 2, "어귀는 빈터, 낙성지는 셋째 구간");
        if (Environment.GetEnvironmentVariable("SHOW_LAND") != null) Console.WriteLine(Ascii(a));
    }

    static string Pct(long[,,] mix, long[,] rocks, int land, int band)
    {
        return (100.0 * mix[land, band, 0] / rocks[land, band]).ToString("0") + "/" + (100.0 * mix[land, band, 1] / rocks[land, band]).ToString("0")
               + "/" + (100.0 * mix[land, band, 2] / rocks[land, band]).ToString("0");
    }

    /// <summary>시험용 땅. 전부 한 가지 암반이고 어귀의 빈터만 열려 있다.</summary>
    static World FlatWorld(Defs defs, CapeDef cape, Rock rock)
    {
        var w = new World(defs, 1);
        var L = new Land(cape, cape.Lands[0]);
        for (int i = 0; i < L.Rocks.Length; i++) L.Rocks[i] = rock;
        for (int y = 0; y < cape.EntranceRows; y++)
            for (int x = cape.EntranceCol0; x <= cape.EntranceCol1; x++) L.Rocks[L.Index(x, y)] = Rock.Open;
        L.EntranceX = L.W / 2 - 1;
        for (int i = 0; i < L.Rocks.Length; i++) L.Hp[i] = (ushort)L.HardnessOf(i);
        w.Cape = new Cape(cape, L);
        return w;
    }

    /// <summary>왕을 곶의 한 자리에 세운다.</summary>
    static void OnCape(World w, float x, float y)
    {
        w.King.Leave();
        w.King.CX = x;
        w.King.CY = y;
    }

    static void CapeTests(Defs defs, CapeDef cape)
    {
        KingDef k = defs.King;
        var up = new InputFrame { MoveY = 1f, AimY = 1f };

        Section("곶: 오가는 길");
        {
            var w = FlatWorld(defs, cape, Rock.Soil);
            w.King.X = 8f;
            Steps(w, 10, new InputFrame { MoveY = 1f });
            Check(w.King.OnWall && w.King.Y == k.WallWidth, "뒷문이 아닌 곳에서는 성벽 안쪽으로 나갈 수 없다");
            w = FlatWorld(defs, cape, Rock.Soil);
            int ticks = 0;
            while (ticks < 600 && (w.King.OnWall || w.King.CY < 0f)) { w.Step(up); ticks++; }
            Check(!w.King.OnWall && Math.Abs(ticks / 30f - 5f) < 0.3f, "성벽 가운데에서 땅의 어귀까지 5초");
            Steps(w, 60, up);
            Check(w.King.CY < 2 * cape.TileM && w.King.CY > 2 * cape.TileM - 0.6f, "암반은 지나갈 수 없다");
            Steps(w, 30 * 20, new InputFrame { MoveX = -1f });
            Check(w.King.CX >= cape.EntranceCol0 * cape.TileM + cape.KingRadius - 0.01f, "빈터 밖으로 나가지 못한다");
            ticks = 0;
            w.King.CX = w.Cape.GateX;
            while (ticks < 600 && !w.King.OnWall) { w.Step(new InputFrame { MoveY = -1f }); ticks++; }
            Check(w.King.OnWall && w.King.Y == k.WallWidth && w.King.ReturnBonus > 0f, "길 끝에서 뒷문으로 돌아온다. 10초 넘게 비웠으므로 왕의 귀환이 켜진다");
        }

        Section("곶: 채굴");
        {
            Func<Rock, int, float> dig = (rock, power) =>
            {
                var w = FlatWorld(defs, cape, rock);
                w.Cape.DigPower = power;
                OnCape(w, 23f, 3.6f);
                int n = 0;
                while (n < 3000 && w.Cape.TilesDug < 3) { w.Step(new InputFrame { AimY = 1f, Held = Buttons.Attack }); n++; }
                Land L = w.Cape.Land;
                bool row = L.At(10, 2) == Rock.Open && L.At(11, 2) == Rock.Open && L.At(12, 2) == Rock.Open;
                if (Environment.GetEnvironmentVariable("SHOW_DIG") != null) Console.WriteLine("  " + rock + " " + power + ": " + n + "틱, 줄 " + row + ", 타격 " + w.Cape.Swings);
                return row ? (n - 1) / 30f : -1f;
            };
            Check(Math.Abs(dig(Rock.Soil, 1) - 3.6f) < 0.35f, "흙 세 타일을 3.6초에 판다");
            Check(Math.Abs(dig(Rock.Soft, 1) - 9f) < 0.5f, "연암 세 타일을 9초에");
            Check(Math.Abs(dig(Rock.Hard, 1) - 24f) < 1.0f, "경암 세 타일을 24초에");
            Check(Math.Abs(dig(Rock.Soil, 2) - 1.8f) < 0.35f, "채굴 위력 Ⅰ이면 절반");

            var w2 = FlatWorld(defs, cape, Rock.Soil);
            OnCape(w2, 23f, 3.6f);
            Steps(w2, 30, new InputFrame { AimY = -1f, Held = Buttons.Attack });
            Check(w2.Cape.Land.Hp[w2.Cape.Land.Index(11, 2)] == 12, "등 뒤의 암반은 깎이지 않는다");
            w2.Cape.Land.Rocks[w2.Cape.Land.Index(11, 2)] = Rock.Ridge;
            Steps(w2, 30 * 30, new InputFrame { AimY = 1f, Held = Buttons.Attack });
            Check(w2.Cape.Land.At(11, 2) == Rock.Ridge && w2.Cape.Land.At(10, 2) == Rock.Open, "바위산은 부서지지 않는다");
        }

        Section("곶: 운반과 입고");
        {
            var w = FlatWorld(defs, cape, Rock.Soil);
            Land L = w.Cape.Land;
            foreach (int x in new[] { 10, 11, 12 })
            {
                L.OreKind[L.Index(x, 2)] = x == 12 ? Ore.Iron : Ore.Star;
                L.OreAmount[L.Index(x, 2)] = 7;
            }
            OnCape(w, 23f, 3.6f);
            Steps(w, 30 * 4, new InputFrame { AimY = 1f, Held = Buttons.Attack });
            Check(w.Cape.CarryCount(w.King) == 16 && w.Cape.MinedStar == 14 && w.Cape.MinedIron == 7, "캔 것은 왕을 따라온다. 한도는 16");
            Check(w.Cape.Piles.Count == 1 && w.Cape.Piles[0].Count == 5, "들지 못한 다섯은 그 자리에 남는다");
            float x0 = w.King.CX;
            Steps(w, 30, new InputFrame { MoveX = 1f });
            Check(Math.Abs(w.King.CX - x0 - k.Speed * 0.7f) < 0.02f, "16개를 끌면 30% 느리다");
            w.Step(new InputFrame { Held = Buttons.Drop });
            w.Step(new InputFrame());
            Check(w.Cape.CarryCount(w.King) == 0 && w.Cape.Piles.Count == 2, "내려놓으면 더미가 된다");
            x0 = w.King.CX;
            Steps(w, 30, new InputFrame { MoveX = -1f });
            Check(Math.Abs(x0 - w.King.CX - k.Speed) < 0.02f, "빈손은 제 속도");
            w.King.CX = w.Cape.Piles[1].X;
            w.King.CY = w.Cape.Piles[1].Y;
            w.Step(new InputFrame { Held = Buttons.Interact });
            w.Step(new InputFrame());
            Check(w.Cape.CarryCount(w.King) == 16 && w.Cape.Piles.Count == 1, "다가가서 주우면 다시 따라온다");
            int star = w.King.Star, iron = w.King.Iron;
            w.King.CX = w.Cape.GateX;
            w.King.CY = 1f;
            int n = 0;
            bool deposit = false;
            while (n++ < 600 && !w.King.OnWall)
            {
                w.Step(new InputFrame { MoveY = -1f });
                for (int e = 0; e < w.Events.Count; e++) deposit |= w.Events[e].Kind == EventKind.Deposit && w.Events[e].A == star;
            }
            Check(w.King.OnWall && w.Economy.Star == star && w.Economy.Iron == iron && w.Cape.CarryCount(w.King) == 0 && deposit, "뒷문을 지나면 성의 자원이 된다");
            Check(Math.Abs(n / 30f - (cape.ApproachM + 1f) / (k.Speed * 0.7f)) < 0.5f, "무거우면 돌아오는 길도 길다(29m에 6.9초)");
        }

        Section("곶: 대파편과 되감기");
        {
            var w = new World(defs, 9);
            w.OpenLand(cape, 0, 7);
            w.Cycle.Begin(w, ChapterDef.Load(File.ReadAllText(Path.Combine(Root(), "data", "waves", "ch1.json"))));
            Land L = w.Cape.Land;
            OnCape(w, L.CraterX * 2f + 1f, L.CraterY * 2f + 1f);
            w.Step(new InputFrame { Held = Buttons.Interact });
            Check(w.King.HasFragment && L.FragmentTaken, "낙성지에서 대파편을 든다");
            byte[] snap = w.Snapshot();
            w.King.CX = w.Cape.GateX;
            w.King.CY = -20f;
            Steps(w, 150, new InputFrame { MoveY = -1f });
            Check(w.King.OnWall && w.Cycle.FragmentHome && w.Cycle.Next.Final, "성에 가져오면 다음 습격이 총공세다");
            w.Restore(snap);
            Check(!w.King.OnWall && w.King.HasFragment && !w.Cycle.FragmentHome, "되감으면 대파편은 다시 왕의 손에 있다");

            w = new World(defs, 9);
            w.OpenLand(cape, 0, 7);
            var bot = new Miner();
            for (int i = 0; i < 30 * 60; i++) w.Step(bot.Think(w, false));
            snap = w.Snapshot();
            int dug = w.Cape.TilesDug;
            var bot2 = new Miner();
            for (int i = 0; i < 30 * 30; i++) w.Step(bot2.Think(w, false));
            ulong after = w.StateHash();
            w.Restore(snap);
            Check(w.Cape.TilesDug == dug && dug > 0, "되감으면 판 자리도 돌아간다");
            bot2 = new Miner();
            for (int i = 0; i < 30 * 30; i++) w.Step(bot2.Think(w, false));
            Check(w.StateHash() == after, "되감은 뒤 다시 캐면 같은 상태에 닿는다");
            Console.WriteLine("  스냅숏 크기: " + snap.Length + "바이트 (땅 하나 포함)");
        }
    }

    /// <summary>
    /// 테스트용 광부. 본 게임의 코드가 아니다. 가장 싸게 닿는 광맥으로 파 들어가고, 가득 차거나 부르면 돌아온다.
    /// 땅 전체를 안다(안개가 없다). tools/cape_gen.py의 광부와 같은 셈법이다.
    /// </summary>
    sealed class Miner
    {
        readonly List<int> path = new List<int>();
        int plannedAt = -1;
        bool plannedHome;
        public bool Exhausted;

        void Plan(World w, int start, bool home)
        {
            Land L = w.Cape.Land;
            CapeDef c = w.Cape.Def;
            path.Clear();
            plannedAt = w.Cape.TilesDug;
            plannedHome = home;
            int n = L.W * L.H;
            var dist = new int[n];
            var prev = new int[n];
            for (int i = 0; i < n; i++) { dist[i] = int.MaxValue; prev[i] = -1; }
            var pq = new PriorityQueue<int, int>();
            dist[start] = 0;
            pq.Enqueue(start, 0);
            int goal = -1, entrance = L.Index(L.EntranceX, L.EntranceY);
            while (pq.Count > 0)
            {
                int p, d;
                pq.TryDequeue(out p, out d);
                if (d > dist[p]) continue;
                if (p != start && (home ? p == entrance : L.OreKind[p] != Ore.None)) { goal = p; break; }
                for (int k = 0; k < 4; k++)
                {
                    int qx = p % L.W + LandGen.DX[k], qy = p / L.W + LandGen.DY[k];
                    if (!L.Inside(qx, qy) || !L.Passable(qx, qy)) continue;
                    int q = L.Index(qx, qy);
                    int cost = 330 + (L.Rocks[q] == Rock.Open ? 0 : (L.Hp[q] + w.Cape.DigPower - 1) / w.Cape.DigPower * (int)(c.Swing * 1000));
                    if (d + cost >= dist[q]) continue;
                    dist[q] = d + cost;
                    prev[q] = p;
                    pq.Enqueue(q, d + cost);
                }
            }
            if (goal < 0) { if (!home) Exhausted = true; return; }
            for (int p = goal; p != start; p = prev[p]) path.Insert(0, p);
        }

        public InputFrame Think(World w, bool home)
        {
            King king = w.King;
            Cape cape = w.Cape;
            Land L = cape.Land;
            var f = new InputFrame();
            if (cape.CarryCount(king) >= cape.Def.CarryLimit || Exhausted) home = true;
            float gx = cape.GateX - king.CX;
            if (king.CY < 0.6f && (home || king.CY < -0.5f))
            {
                // 길 위. 가운데로 붙어서 오르내린다
                if (Math.Abs(gx) > 0.3f) f.MoveX = Math.Sign(gx);
                if (Math.Abs(gx) < 1.2f) f.MoveY = home ? -1f : 1f;
                return f;
            }
            int m = cape.Def.TileM;
            int tx = (int)Math.Floor(king.CX / m), ty = Math.Max(0, (int)Math.Floor(king.CY / m));
            int here = L.Index(tx, ty);
            if (home && here == L.Index(L.EntranceX, L.EntranceY))
            {
                if (Math.Abs(gx) > 0.3f) f.MoveX = Math.Sign(gx);
                if (Math.Abs(gx) < 1.2f) f.MoveY = -1f;
                return f;
            }
            if (path.Count > 0 && path[0] == here) path.RemoveAt(0);
            if (path.Count == 0 || plannedAt != cape.TilesDug || plannedHome != home) Plan(w, here, home);
            if (path.Count == 0) return f;
            int next = path[0];
            float dx = next % L.W * m + m * 0.5f - king.CX, dy = next / L.W * m + m * 0.5f - king.CY;
            float d = (float)Math.Sqrt(dx * dx + dy * dy);
            if (d > 0.05f) { f.MoveX = dx / d; f.MoveY = dy / d; f.AimX = dx / d; f.AimY = dy / d; }
            if (L.Rocks[next] != Rock.Open) f.Held = Buttons.Attack;
            return f;
        }
    }

    sealed class Trip
    {
        public int Wave, Star, Iron;
        public float Away;
    }

    /// <summary>
    /// 캐고, 종이 울리면 돌아와 싸우는 한 장. 종이 울리고 linger초 뒤에 돌아서기 시작한다.
    /// </summary>
    static World Campaign(Defs defs, CapeDef cape, ChapterDef ch, ulong seed, int power, float linger, List<Trip> trips, out float away, out int flawless)
    {
        var w = new World(defs, seed);
        w.OpenLand(cape, 0, seed);
        w.Cape.DigPower = power;
        w.Cycle.Begin(w, ch);
        var miner = new Miner();
        away = 0f;
        flawless = 0;
        float leftAt = 0f;
        int guard = 30 * 60 * 90;
        while (guard-- > 0 && w.Cycle.Phase != Phase.Victory && w.Cycle.Phase != Phase.Fallen)
        {
            Phase ph = w.Cycle.Phase;
            bool fighting = ph == Phase.Assault || ph == Phase.Onslaught;
            float alarm = defs.King.Alarm[0];
            bool call = (ph == Phase.Alarm && w.Cycle.TimeLeft <= alarm - linger) || (fighting && w.Cycle.AssaultTicks >= (linger - alarm) * World.TickRate);
            if (w.Cycle.Wave >= ch.Waves.Length) w.Cycle.DeliverFragment();
            InputFrame f;
            bool wasOut = !w.King.OnWall;
            if (wasOut)
            {
                f = miner.Think(w, call);
                away += World.Dt;
            }
            else if (fighting || w.Siege.Threat) f = Bot(w);
            else if (!miner.Exhausted && ph == Phase.Calm && w.Cycle.TimeLeft > 30f)
            {
                // 고요다. 뒷문으로 간다
                float dx = defs.King.WallLength * 0.5f - w.King.X;
                f = new InputFrame { MoveX = Math.Abs(dx) > 0.5f ? Math.Sign(dx) : 0f, MoveY = 1f };
                leftAt = w.Tick / 30f;
            }
            else f = Bot(w);
            w.Step(f);
            for (int i = 0; i < w.Events.Count; i++)
            {
                SimEvent e = w.Events[i];
                if (e.Kind == EventKind.AssaultEnd) flawless += e.C;
                if (e.Kind == EventKind.Deposit && trips != null)
                    trips.Add(new Trip { Wave = w.Cycle.Wave, Star = e.A, Iron = e.B, Away = w.Tick / 30f - leftAt });
            }
        }
        return w;
    }

    static void CampaignTests(Defs defs, CapeDef cape, ChapterDef ch)
    {
        KingDef k = defs.King;
        Section("캐고 돌아와 싸우는 1장");
        var trips = new List<Trip>();
        float away;
        int flawless;
        World w = Campaign(defs, cape, ch, 7, 2, 0f, trips, out away, out flawless);
        Check(w.Cycle.Phase == Phase.Victory, "종이 울리면 돌아오는 왕은 1장을 이긴다");
        Check(w.Economy.Star + w.Cape.Land.OreLeft(Ore.Star) + w.King.Star + PileStar(w) == 170, "성광석은 생기지도 사라지지도 않는다(170)");
        ulong h = w.StateHash();
        Check(Campaign(defs, cape, ch, 7, 2, 0f, null, out away, out flawless).StateHash() == h, "같은 시드의 한 장은 같은 결과로 끝난다");
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < trips.Count && i < 14; i++) sb.Append(trips[i].Star + "+" + trips[i].Iron + " ");
        Console.WriteLine("  시드 7, 채굴 위력 2: 나갔다 올 때마다 가져온 것(성광석+흑철): " + sb + (trips.Count > 14 ? "…" : ""));

        Console.WriteLine("  시드 열 개의 평균 (종이 울리면 바로 돌아선다)");
        foreach (int power in new[] { 1, 2 })
        {
            double star = 0, iron = 0, sec = 0, wall = 0, mins = 0, fl = 0, wins = 0, calms = 0;
            for (ulong seed = 1; seed <= 10; seed++)
            {
                w = Campaign(defs, cape, ch, seed, power, 0f, null, out away, out flawless);
                star += w.Economy.Star; iron += w.Economy.Iron; sec += away; wall += k.WallHp - w.Siege.WallHp;
                mins += w.Tick / 30.0 / 60.0; fl += flawless; calms += w.Cycle.Wave + 1;
                if (w.Cycle.Phase == Phase.Victory) wins++;
            }
            Console.WriteLine("    채굴 위력 " + power + ": 성광석 " + (star / 10).ToString("0") + "/170, 흑철 " + (iron / 10).ToString("0") + "/60, 성벽을 비운 시간 "
                              + (sec / 10 / 60).ToString("0.0") + "분, 비운 1초당 성광석 " + (star / sec).ToString("0.000") + " · 흑철 " + (iron / sec).ToString("0.000")
                              + ", 고요 한 번에 성광석 " + (star / calms).ToString("0.0") + ", 한 장 " + (mins / 10).ToString("0.0") + "분, 성벽 -" + (wall / 10).ToString("0")
                              + ", 흠 없는 방어 " + (fl / 10).ToString("0.0") + "/13, 승리 " + wins + "/10");
        }
        Console.WriteLine("  종이 울린 뒤에도 더 캐는 왕 (채굴 위력 2, 시드 열 개)");
        foreach (float linger in new[] { 15f, 30f, 45f, 60f })
        {
            double star = 0, wall = 0, wins = 0, fl = 0;
            for (ulong seed = 1; seed <= 10; seed++)
            {
                w = Campaign(defs, cape, ch, seed, 2, linger, null, out away, out flawless);
                star += w.Economy.Star; wall += k.WallHp - w.Siege.WallHp; fl += flawless;
                if (w.Cycle.Phase == Phase.Victory) wins++;
            }
            Console.WriteLine("    종이 울리고 " + linger.ToString("0") + "초 더: 성광석 " + (star / 10).ToString("0") + ", 성벽 -" + (wall / 10).ToString("0")
                              + ", 흠 없는 방어 " + (fl / 10).ToString("0.0") + "/13, 승리 " + wins + "/10");
        }
    }

    static int PileStar(World w)
    {
        int n = 0;
        for (int i = 0; i < w.Cape.Piles.Count; i++) n += w.Cape.Piles[i].Star;
        return n;
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

        CapeDef cape = CapeDef.Load(File.ReadAllText(Path.Combine(root, "data", "cape", "lands.json")));
        LandTests(cape);
        KingTests(defs);
        ChapterDef ch1 = ChapterDef.Load(File.ReadAllText(Path.Combine(root, "data", "waves", "ch1.json")));
        CycleTests(defs, ch1);
        CapeTests(defs, cape);
        CampaignTests(defs, cape, ch1);

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
                              + " (" + (ticks / 30f).ToString("0") + "초, 처치 " + last.Siege.Kills + ", 추락사 " + last.Siege.FallKills
                              + ", 왕이 받은 피해 " + last.King.DamageTaken.ToString("0") + ", 휘두른 횟수 " + last.King.Swings + ")");
            World all = Run(defs, 7, Wave(J.Obj(waves[waves.Count - 1])), 35f, true, 30 * 120, out ticks);
            Console.WriteLine("  1장 총공세: 지키면 성벽 -" + (k.WallHp - all.Siege.WallHp).ToString("0")
                              + " (" + (ticks / 30f).ToString("0") + "초, 처치 " + all.Siege.Kills + ", 왕이 받은 피해 " + all.King.DamageTaken.ToString("0")
                              + ", 쓰러짐 " + all.King.Downs + "). 바위턱의 석궁수 " + (20 - all.Siege.Kills) + "기는 칼이 닿지 않아 남는다");
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

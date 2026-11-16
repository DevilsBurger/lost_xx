using System;
using System.Collections.Generic;
using System.IO;
using LostRegalia.Sim;

/// <summary>
/// 시뮬레이션 코어의 테스트. 외부 패키지 없이 돈다.
///   dotnet run --project sim/Tests~/Tests
/// 하나라도 실패하면 종료 코드 1.
/// </summary>
static partial class Program
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
    /// 테스트용 왕. 본 게임의 코드가 아니다. 상태를 읽고 입력만 만든다. 대시는 쓰지 않는다.
    /// 가장 급한 적의 레인으로 걸어가 강공격을 모아 친다. 기술을 장착했으면 쓴다:
    /// 검기는 바위턱의 석궁수에게, 내려찍기는 닿는 적이 둘 이상이거나 사다리가 있을 때.
    /// BotParry가 켜져 있으면 석궁의 화살을 빠짐없이 받아친다(사람은 그러지 못한다).
    /// </summary>
    static bool BotParry;

    static InputFrame Bot(World w)
    {
        KingDef k = w.Defs.King;
        King king = w.King;
        var es = w.Siege.Enemies;
        var f = new InputFrame { AimY = -1f };
        int best = -1, archer = -1, near = 0;
        float score = -1f, soon = 0f;
        bool ladderNear = false;
        for (int i = 0; i < es.Count; i++)
        {
            float s = -1f;
            EnemyState st = es[i].State;
            if (st == EnemyState.OnWall) s = 100f;
            else if (st == EnemyState.Ladder) s = 70f;
            else if (st == EnemyState.Raising) s = 60f;
            else if (st == EnemyState.Mounting) s = 50f;
            else if (st == EnemyState.Climbing) s = es[i].Height;
            else if (st == EnemyState.Perched && w.Defs.Enemies[es[i].Def].KingDamage > 0f)
            {
                if (archer < 0 || es[i].Timer > es[archer].Timer) archer = i;
                soon = Math.Max(soon, es[i].Timer);
            }
            if (s > score) { score = s; best = i; }
            bool band = st == EnemyState.OnWall || st == EnemyState.Mounting || st == EnemyState.Raising || st == EnemyState.Ladder
                        || (st == EnemyState.Climbing && es[i].Height >= k.CliffHeight - k.ReachBelow);
            if (band && Math.Abs(k.LaneX(es[i].Lane) - king.X) <= 3.5f)
            {
                near++;
                if (st == EnemyState.Ladder || st == EnemyState.Raising) ladderNear = true;
            }
        }
        if (BotParry && soon >= k.RangedInterval - 0.1f && king.ParryCd <= 0f && king.Busy <= 0f && king.GuardSlot < 0 && king.SlamSlot < 0)
        {
            f.Held = Buttons.Guard;
            return f;
        }
        bool tap = w.Tick % 2 == 0;                 // 누른 순간이 생기도록 한 틱 걸러 누른다
        if (w.Build != null && king.Busy <= 0f && king.GuardSlot < 0 && king.SlamSlot < 0)
            for (int s = 0; s < w.Build.Equip.Length; s++)
            {
                if (w.Build.Equip[s] < 0 || king.SkillCd[s] > 0f) continue;
                SkillDef sk = w.Build.Tree.Nodes[w.Build.Equip[s]].Skill;
                Buttons button = s == 0 ? Buttons.Skill1 : s == 1 ? Buttons.Skill2 : Buttons.Skill3;
                if (sk.Delivery == Delivery.Line && archer >= 0)
                {
                    f.AimX = k.LaneX(es[archer].Lane) - king.X;
                    f.AimY = -(k.CliffHeight - es[archer].Height) - king.Y;
                    if (Math.Sqrt(f.AimX * f.AimX + f.AimY * f.AimY) <= sk.Range && tap) f.Held = button;
                    if (f.Held != Buttons.None) return f;
                    f.AimX = 0f; f.AimY = -1f;
                }
                else if (sk.Delivery == Delivery.Slam && (near >= 2 || ladderNear) && tap)
                {
                    f.Held = button;
                    return f;
                }
            }
        if (best < 0) return f;
        float dx = k.LaneX(es[best].Lane) - king.X;
        float dy = 1.5f - king.Y;
        if (Math.Abs(dx) > 0.3f) f.MoveX = Math.Sign(dx);
        if (Math.Abs(dy) > 0.2f) f.MoveY = Math.Sign(dy);
        bool reachable = es[best].State != EnemyState.Climbing || es[best].Height >= k.CliffHeight - k.ReachBelow;
        bool ready = king.Charge >= k.ChargeTime && Math.Abs(dx) <= 1.5f && reachable;
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
    static World Chapter(Defs defs, ChapterDef ch, ulong seed, float late, out int flawless, out float worst, Action<World> setup = null)
    {
        var w = new World(defs, seed);
        w.Cycle.Begin(w, ch);
        if (setup != null) setup(w);
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

            // 장을 끝내는 때
            {
                var v = new World(defs, 3);
                v.Cycle.Begin(v, ch);
                Check(v.Cycle.Next.Count == 10, "첫 습격은 열 기");
                v.Cycle.Wave = 12;
                WaveDef over = v.Cycle.Next;
                Check(over.Count == 13 && !over.Final, "열두 습격을 넘기면 열두째 편성이 9% 거세진다(12기 → 13기)");
                v.Cycle.Wave = 16;
                Check(v.Cycle.Next.Count > over.Count, "넘길수록 더 거세진다");
                v.Cycle.Wave = 12;
                v.Cycle.DeliverFragment();
                Check(v.Cycle.Next == ch.Onslaught && v.Cycle.Next.Count == 20, "열두 습격 뒤에 부른 총공세는 정해진 편성(20기)");
                v.Cycle.Wave = 6;
                int early = v.Cycle.Next.Count;
                v.Cycle.Wave = 16;
                int late = v.Cycle.Next.Count;
                Check(early < 20 && late > 20 && v.Cycle.Next.Final, "일찍 부르면 작고(" + early + "기), 늦게 부르면 크다(" + late + "기)");
            }

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
    /// <summary>테스트용 광부의 솜씨. 1이면 쉬지 않는다. 0.55면 캐는 동안 틱의 45%를 헤맨다(사람 보정, 곶 생성 규칙 9장).</summary>
    static float MinerSkill = 1f;

    sealed class Miner
    {
        readonly List<int> path = new List<int>();
        int plannedAt = -1, plannedPiles;
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
            var pile = new bool[n];                 // 들지 못해 남겨 둔 더미도 가지러 간다
            for (int i = 0; i < w.Cape.Piles.Count; i++)
                pile[L.Index((int)(w.Cape.Piles[i].X / c.TileM), (int)(w.Cape.Piles[i].Y / c.TileM))] = true;
            if (!home && pile[start]) { path.Add(start); return; }
            while (pq.Count > 0)
            {
                int p, d;
                pq.TryDequeue(out p, out d);
                if (d > dist[p]) continue;
                if (p != start && (home ? p == entrance : (L.OreKind[p] != Ore.None || pile[p]))) { goal = p; break; }
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
            if (!home && MinerSkill < 1f && (int)((w.Tick + 1) * MinerSkill) == (int)(w.Tick * MinerSkill)) return f;     // 헤매는 틱
            if (path.Count > 0 && path[0] == here) path.RemoveAt(0);
            if (path.Count == 0 || plannedAt != cape.TilesDug || plannedHome != home || plannedPiles != cape.Piles.Count) Plan(w, here, home);
            plannedPiles = cape.Piles.Count;
            if (!home && path.Count == 1 && path[0] == here)
            {
                // 더미 위에 섰다. 줍는다
                path.Clear();
                if (w.Tick % 2 == 0) f.Held = Buttons.Interact;
                return f;
            }
            if (path.Count == 0) return f;
            int next = path[0];
            float dx = next % L.W * m + m * 0.5f - king.CX, dy = next / L.W * m + m * 0.5f - king.CY;
            float d = (float)Math.Sqrt(dx * dx + dy * dy);
            if (d > 0.05f) { f.MoveX = dx / d; f.MoveY = dy / d; f.AimX = dx / d; f.AimY = dy / d; }
            if (L.Rocks[next] != Rock.Open) f.Held = Buttons.Attack;
            return f;
        }
    }

    /// <summary>테스트용 왕이 성벽에 있을 때, 고요가 이만큼 넘게 남았으면 (다시) 캐러 나간다.</summary>
    static float CampaignAgain = 30f;

    /// <summary>테스트용 왕이 대파편을 가져오는 때. 치른 습격이 이 수에 닿으면. 0이면 땅을 다 캤을 때.</summary>
    static int CampaignLeaveAt = 12;

    sealed class Trip
    {
        public int Wave, Star, Iron;
        public float Away, Left;        // 비운 시간, 돌아왔을 때 습격까지 남은 시간(습격 중이면 음수: 시작하고 지난 시간)
    }

    /// <summary>
    /// 캐고, 종이 울리면 돌아와 싸우는 한 장. 종이 울리고 linger초 뒤에 돌아서기 시작한다.
    /// </summary>
    static World Campaign(Defs defs, CapeDef cape, ChapterDef ch, ulong seed, int power, float linger, List<Trip> trips, out float away, out int flawless,
                          Action<World> shop = null, TreeDef tree = null, CastleDef castle = null)
    {
        var w = new World(defs, seed);
        w.OpenLand(cape, 0, seed);
        if (tree != null) w.OpenShop(tree, castle);
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
            if (CampaignLeaveAt > 0 ? w.Cycle.Wave >= CampaignLeaveAt : (miner.Exhausted && w.King.OnWall)) w.Cycle.DeliverFragment();
            InputFrame f;
            bool wasOut = !w.King.OnWall;
            if (shop != null && !wasOut && ph == Phase.Calm) shop(w);
            if (wasOut)
            {
                f = miner.Think(w, call);
                away += World.Dt;
            }
            else if (fighting || w.Siege.Threat) f = Bot(w);
            else if (!miner.Exhausted && ph == Phase.Calm && w.Cycle.TimeLeft > CampaignAgain)
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
                    trips.Add(new Trip { Wave = w.Cycle.Wave, Star = e.A, Iron = e.B, Away = w.Tick / 30f - leftAt,
                                         Left = (w.Cycle.Phase == Phase.Calm || w.Cycle.Phase == Phase.Alarm) ? w.Cycle.TimeLeft : -w.Cycle.AssaultTicks / 30f });
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

    static World ShopWorld(Defs defs, CapeDef cape, TreeDef tree, CastleDef castle, int star, int iron)
    {
        var w = new World(defs, 1);
        w.OpenLand(cape, 0, 7);
        w.OpenShop(tree, castle);
        w.Economy.Star = star;
        w.Economy.Iron = iron;
        return w;
    }

    static void ShopTests(Defs defs, CapeDef cape, ChapterDef ch, TreeDef tree, CastleDef castle)
    {
        KingDef k = defs.King;
        Section("사기와 되돌리기");
        {
            Check(tree.Nodes.Length == 69 && castle.Facilities.Length == 3 && castle.King[0].Length == 2, "노드 69, 시설 3종, 왕 강화 2단계까지");
            var w = ShopWorld(defs, cape, tree, castle, 20, 0);
            Check(w.Build.Awaken(w, "S1") == Buy.Ok && w.Economy.Star == 12 && w.Cape.DigPower == 2, "채굴 위력 Ⅰ: 성광석 8, 타격당 강도 2");
            Check(w.Build.Awaken(w, "S1") == Buy.Taken, "이미 깨운 노드는 다시 못 산다");
            Check(w.Build.Awaken(w, "P3") == Buy.Locked, "2단계 노드는 1장에서 잠겨 있다");
            Check(w.Build.Awaken(w, "P2") == Buy.Ok && w.Build.Awaken(w, "F2") == Buy.NoFunds, "모자라면 못 산다");
            Check(w.Build.Unawaken(w, "S1") == Buy.Ok && w.Economy.Star == 12 && w.Cape.DigPower == 1, "재배치는 값 없이 전액을 돌려받는다");
            w.Build.Chapter = 2;
            w.Economy.Star = 100;
            w.Build.Awaken(w, "P1");
            Check(w.Build.Awaken(w, "P3") == Buy.Ok, "2장: 1단계 둘을 깨웠으면 2단계가 열린다");
            Check(w.Build.Unawaken(w, "P2") == Buy.Needed, "다른 노드가 기대고 있으면 되돌릴 수 없다");

            w = ShopWorld(defs, cape, tree, castle, 100, 0);
            Check(w.Build.UpgradeKing(w, Axis.Back) == Buy.Ok && w.Cape.CarryLimit == 23 && w.Economy.Star == 88, "등 1: 운반 23");
            Check(w.Build.UpgradeKing(w, Axis.Back) == Buy.Ok && w.Cape.CarryLimit == 30 && w.Economy.Star == 66, "등 2: 운반 30");
            Check(w.Build.UpgradeKing(w, Axis.Back) == Buy.Maxed, "프로토타입은 2단계까지");
            Check(w.Build.UpgradeKing(w, Axis.Heart) == Buy.Ok && w.King.MaxHp == 120f && w.King.Hp == 120f, "심장 1: 체력 120");
            Check(w.Build.UpgradeKing(w, Axis.Heart) == Buy.Ok && w.King.MaxHp == 140f && w.King.ReviveTime == 5f, "심장 2: 체력 140, 재기 5초");
            Check(w.Build.UpgradeKing(w, Axis.Legs) == Buy.Ok, "다리 1");
            float x = w.King.X;
            Steps(w, 30, new InputFrame { MoveX = 1f });
            Check(Math.Abs(w.King.X - x - k.Speed * 1.1f) < 0.01f, "다리 1: 10% 빠르다");
            Check(w.Build.DowngradeKing(w, Axis.Legs) == Buy.Ok && w.Economy.Star == 32 && w.Build.RefundsLeft == 2, "왕 강화 되돌리기: 전액 환급, 남은 횟수 2");

            w = ShopWorld(defs, cape, tree, castle, 0, 40);
            Check(w.Build.BuildFacility(w, 3, "ballista") == Buy.Locked, "1장의 칸은 셋");
            Check(w.Build.BuildFacility(w, 0, "ballista") == Buy.Ok && w.Build.BuildFacility(w, 0, "thorns") == Buy.Taken && w.Economy.Iron == 30, "시설: 흑철 10, 칸 하나에 하나");
            Check(w.Build.UpgradeWall(w) == Buy.Ok && w.Siege.WallMax == 1150f && w.Siege.WallHp == 1150f && w.Economy.Iron == 18, "성벽 강화 Ⅰ: 흑철 12, 1,150");
            w.Siege.WallHp = 1000f;
            Check(w.Build.Repair(w) == Buy.Ok && w.Siege.WallHp == 1050f && w.Economy.Iron == 17, "수리: 흑철 1에 50");
            w.Siege.WallHp = 1140f;
            w.Build.Repair(w);
            Check(w.Siege.WallHp == 1150f && w.Build.Repair(w) == Buy.Maxed && w.Economy.Iron == 16, "가득 찬 성벽에는 쓰지 않는다");
            Check(w.Build.Demolish(w, 0) == Buy.Ok && w.Economy.Iron == 26 && w.Build.Count("ballista") == 0, "철거하면 전액을 돌려받는다");
            w.Build.Demolish(w, 0);
            w.Build.BuildFacility(w, 0, "thorns"); w.Build.Demolish(w, 0);
            w.Build.BuildFacility(w, 0, "thorns"); w.Build.Demolish(w, 0);
            w.Build.BuildFacility(w, 0, "thorns");
            Check(w.Build.Demolish(w, 0) == Buy.NoRefunds, "되돌리기는 장마다 세 번");

            w = ShopWorld(defs, cape, tree, castle, 100, 100);
            w.Cycle.Begin(w, ch);
            Steps(w, 30 * 90, new InputFrame());
            Check(w.Cycle.Phase == Phase.Alarm && w.Build.Awaken(w, "S1") == Buy.NotNow && w.Build.Repair(w) == Buy.NotNow, "종이 울린 뒤에는 살 수 없다");
            w = ShopWorld(defs, cape, tree, castle, 100, 100);
            w.King.Leave();
            Check(w.Build.BuildFacility(w, 0, "ballista") == Buy.NotNow, "성벽을 비운 동안에는 살 수 없다");
        }

        Section("노드와 시설의 효과");
        {
            var w = ShopWorld(defs, cape, tree, castle, 100, 100);
            w.Build.Awaken(w, "P2");
            Put(w, "shield", 2, EnemyState.Climbing, 6f);
            w.Siege.Knock(0, 99f);
            Check(Math.Abs(w.Siege.Enemies[0].Hp - (45f - 16f * 1.3f)) < 0.01f, "무게: 낙하 피해 +30% (6m에서 16 → 20.8)");

            w = ShopWorld(defs, cape, tree, castle, 100, 100);
            w.Build.Awaken(w, "F2");
            w.King.X = k.LaneX(2); w.King.Y = 2f;
            Put(w, "crawler", 2, EnemyState.OnWall, k.CliffHeight);
            Tap(w, Buttons.Attack);
            float x = w.King.X;
            Steps(w, 30, new InputFrame { MoveX = 1f });
            Check(w.Siege.Kills == 1 && Math.Abs(w.King.X - x - k.Speed * 1.2f) < 0.25f, "여세: 처치한 뒤 20% 빠르다");
            Steps(w, 45, new InputFrame());
            x = w.King.X;
            Steps(w, 30, new InputFrame { MoveX = 1f });
            Check(Math.Abs(w.King.X - x - k.Speed) < 0.01f, "여세는 2초 뒤에 꺼진다");

            w = ShopWorld(defs, cape, tree, castle, 100, 100);
            w.Build.Awaken(w, "I2");
            w.King.X = k.LaneX(2); w.King.Y = 2f;
            w.Siege.WallHp = 900f;
            Put(w, "climber", 2, EnemyState.OnWall, k.CliffHeight);
            Steps(w, 33, new InputFrame());
            w.Step(new InputFrame { Held = Buttons.Guard });
            Steps(w, 4, new InputFrame());
            Check(w.King.Parries == 1 && w.Siege.WallHp == 905f, "굳건함: 받아치면 성벽 +5");

            w = ShopWorld(defs, cape, tree, castle, 0, 100);
            w.Build.BuildFacility(w, 0, "ballista");
            w.King.Leave();
            Put(w, "climber", 1, EnemyState.Climbing, 3f);
            Put(w, "climber", 4, EnemyState.Climbing, 9f);
            Steps(w, 30 * 8 + 2, new InputFrame());
            Check(w.Siege.Enemies[1].State == EnemyState.Dead && w.Siege.Enemies[0].Hp == 30f && w.Build.Shots == 1, "발리스타: 8초마다 가장 높은 적에게 40. 칸이 어디든 성벽 전체에 닿는다");
            Steps(w, 30 * 8, new InputFrame());
            Check(w.Siege.Enemies[0].State == EnemyState.Dead && w.Build.Shots == 2, "다음 발은 그다음으로 높은 적에게");
            Steps(w, 30 * 20, new InputFrame());
            Put(w, "climber", 1, EnemyState.Climbing, 3f);
            Steps(w, 3, new InputFrame());
            Check(w.Build.Shots == 3, "쏠 것이 없으면 장전한 채로 기다렸다가 바로 쏜다");

            Func<bool, int, float[]> thorn = (on, lane) =>
            {
                var v = ShopWorld(defs, cape, tree, castle, 0, 100);
                if (on) v.Build.BuildFacility(v, 1, "thorns");
                v.King.Leave();
                Put(v, "porter", lane, EnemyState.Climbing, 14.9f);
                int n = 0;
                while (n < 3000 && v.Siege.Enemies[0].State == EnemyState.Climbing) { v.Step(); n++; }
                return new[] { n / 30f, v.Siege.Enemies[0].Hp };
            };
            float[] plain = thorn(false, 2), thorny = thorn(true, 2), outside = thorn(true, 4);
            Check(Math.Abs(plain[0] - 5.17f) < 0.2f && plain[1] == 220f, "난간이 없으면 짐꾼 거인은 턱밑 띠 3m를 5초에 오른다");
            Check(Math.Abs(thorny[0] - (0.17f + 3f / 0.42f)) < 0.3f, "가시 난간: 턱밑 띠에서 30% 느리다(7.1초)");
            Check(Math.Abs(thorny[1] - (220f - 6f * 3f / 0.42f)) < 2f, "가시 난간: 그동안 초당 6을 깎는다(-43)");
            Check(Math.Abs(outside[0] - plain[0]) < 0.01f && outside[1] == 220f, "가시 난간은 그 칸이 있는 구역의 레인 둘에만 닿는다");

            w = ShopWorld(defs, cape, tree, castle, 0, 100);
            w.Build.BuildFacility(w, 2, "watchtower");
            w.Cycle.Begin(w, ch);
            int bell = -1;
            for (int i = 0; i < 30 * 90 && bell < 0; i++)
            {
                w.Step();
                for (int e = 0; e < w.Events.Count; e++) if (w.Events[e].Kind == EventKind.Bell) bell = w.Tick;
            }
            Check(Math.Abs(bell - 30 * 80) <= 2, "망루: 종이 20초 전에 울린다");

            w = ShopWorld(defs, cape, tree, castle, 100, 100);
            byte[] snap = w.Snapshot();
            w.Build.UpgradeKing(w, Axis.Heart);
            w.Build.UpgradeWall(w);
            w.Build.Awaken(w, "S1");
            w.Restore(snap);
            Check(w.King.MaxHp == 100f && w.King.Hp == 100f && w.Siege.WallMax == 1000f && w.Siege.WallHp == 1000f && w.Cape.DigPower == 1 && w.Economy.Star == 100,
                  "되감으면 산 것과 쓴 자원이 함께 돌아간다");
        }
    }

    /// <summary>테스트용 살림. 순서대로 살 수 있는 만큼 산다. 흑철은 수리가 먼저다.</summary>
    static Action<World> Policy(params string[] order)
    {
        return w =>
        {
            Build b = w.Build;
            while (w.Siege.WallHp <= w.Siege.WallMax - w.Defs.King.RepairPerIron && b.Repair(w) == Buy.Ok) { }
            foreach (string item in order)
            {
                Buy r;
                if (item == "legs") r = b.UpgradeKing(w, Axis.Legs);
                else if (item == "back") r = b.UpgradeKing(w, Axis.Back);
                else if (item == "heart") r = b.UpgradeKing(w, Axis.Heart);
                else if (item == "wall") r = b.WallLevel > 0 ? Buy.Maxed : b.UpgradeWall(w);
                else if (item == "ballista" || item == "thorns" || item == "watchtower")
                {
                    if (b.Count(item) > 0) continue;
                    int slot = Array.IndexOf(b.SlotKind, -1);
                    r = b.BuildFacility(w, slot, item);
                }
                else r = b.Awake[b.Tree.Index(item)] ? Buy.Taken : b.Awaken(w, item);
                if (r == Buy.NoFunds) break;            // 순서를 지킨다. 다음 것을 먼저 사지 않는다
            }
        };
    }

    static void EconomyTests(Defs defs, CapeDef cape, ChapterDef ch, TreeDef tree, CastleDef castle)
    {
        KingDef k = defs.King;
        Section("사면서 치르는 1장");
        string[] iron = { "ballista", "thorns", "watchtower", "wall" };
        var plans = new List<KeyValuePair<string, string[]>>
        {
            new KeyValuePair<string, string[]>("아무것도 사지 않음", new string[0]),
            new KeyValuePair<string, string[]>("채굴 위력 먼저", new[] { "S1", "back", "back", "legs", "legs", "P2", "F2", "I2", "heart" }),
            new KeyValuePair<string, string[]>("등 먼저", new[] { "back", "back", "S1", "legs", "legs", "P2", "F2", "I2", "heart" }),
            new KeyValuePair<string, string[]>("다리 먼저", new[] { "legs", "legs", "S1", "back", "back", "P2", "F2", "I2", "heart" }),
            new KeyValuePair<string, string[]>("검기와 내려찍기 먼저", new[] { "F1", "P1", "S1", "back", "back", "legs", "P2", "heart", "heart" }),
        };
        float away;
        int flawless;
        foreach (var plan in plans)
        {
            var order = new List<string>(plan.Value);
            if (plan.Value.Length > 0) order.AddRange(iron);
            double star = 0, ironGot = 0, sec = 0, wall = 0, wins = 0, left = 0, calms = 0;
            int bought = 0;
            for (ulong seed = 1; seed <= 10; seed++)
            {
                var trips = new List<Trip>();
                World w = Campaign(defs, cape, ch, seed, 1, 0f, trips, out away, out flawless, Policy(order.ToArray()), tree, castle);
                foreach (Trip t in trips) { star += t.Star; ironGot += t.Iron; }
                sec += away; wall += w.Siege.WallMax - w.Siege.WallHp; left += w.Economy.Star; calms += w.Cycle.Wave + 1;
                if (w.Cycle.Phase == Phase.Victory) wins++;
                else if (Environment.GetEnvironmentVariable("SHOW_LOSS") != null)
                    Console.WriteLine("      시드 " + seed + ": " + w.Cycle.Phase + " 습격 " + w.Cycle.Wave + " 틱 " + w.Tick + " 성벽 " + w.Siege.WallHp + " 왕 " + (w.King.OnWall ? "성벽 " + w.King.X + "," + w.King.Y : "곶 " + w.King.CX + "," + w.King.CY) + " 끎 " + w.Cape.CarryCount(w.King) + " 남은 적 " + w.Siege.Enemies.Count + " 위협 " + w.Siege.Threat + " 모드 " + w.King.Mode);
                if (seed == 1)
                {
                    for (int i = 0; i < w.Build.Awake.Length; i++) if (w.Build.Awake[i]) bought++;
                    for (int a = 0; a < 4; a++) bought += w.Build.KingLevel[a];
                }
            }
            Console.WriteLine("    " + plan.Key + ": 성광석 " + (star / 10).ToString("0") + "/170 (고요 한 번에 " + (star / calms).ToString("0.0") + "), 흑철 " + (ironGot / 10).ToString("0")
                              + "/60, 비운 시간 " + (sec / 600).ToString("0.0") + "분, 남긴 성광석 " + (left / 10).ToString("0") + ", 성광석으로 산 것 " + bought
                              + "개(시드 1), 승리 " + wins + "/10");
        }
        {
            var trips = new List<Trip>();
            var order = new List<string>(plans[2].Value);
            order.AddRange(iron);
            World w = Campaign(defs, cape, ch, 3, 1, 0f, trips, out away, out flawless, Policy(order.ToArray()), tree, castle);
            int got = 0;
            foreach (Trip t in trips) got += t.Star;
            Check(w.Cycle.Phase == Phase.Victory && w.Build.KingLevel[(int)Axis.Back] == 2 && w.Cape.CarryLimit == 30, "사면서 치러도 이긴다. 등을 끝까지 올린다");
            int spent = 0;
            for (int i = 0; i < w.Build.Awake.Length; i++) if (w.Build.Awake[i]) spent += tree.Nodes[i].Cost;
            for (int a = 0; a < 4; a++) for (int l = 0; l < w.Build.KingLevel[a]; l++) spent += castle.King[a][l].Cost;
            Check(got == spent + w.Economy.Star, "가져온 성광석 = 쓴 것 + 남은 것");
            Check(got < 200, "성광석으로 살 수 있는 것 전부(200)는 살 수 없다");
        }

        Console.WriteLine("  기술이 바꾸는 것 (성벽을 떠나지 않는 왕, 시설 없음)");
        var loadouts = new List<KeyValuePair<string, string[]>>
        {
            new KeyValuePair<string, string[]>("기본기만", new string[0]),
            new KeyValuePair<string, string[]>("검기", new[] { "F1" }),
            new KeyValuePair<string, string[]>("내려찍기", new[] { "P1" }),
            new KeyValuePair<string, string[]>("검기 + 내려찍기", new[] { "F1", "P1" }),
            new KeyValuePair<string, string[]>("검기 + 내려찍기, 화살을 다 받아침", new[] { "F1", "P1" }),
        };
        foreach (var lo in loadouts)
        {
            BotParry = lo.Key.Contains("받아침");
            float worst;
            World w = Chapter(defs, ch, 11, 0f, out flawless, out worst, v =>
            {
                v.OpenShop(tree, castle);
                v.Economy.Star = 100;
                foreach (string id in lo.Value) v.Build.Awaken(v, id);
            });
            BotParry = false;
            Console.WriteLine("    " + lo.Key + ": " + (w.Cycle.Phase == Phase.Victory ? "승리" : "함락(" + (w.Cycle.Wave + 1) + ")") + ", 성벽 -" + (k.WallHp - w.Siege.WallHp).ToString("0")
                              + ", 왕이 받은 피해 " + w.King.DamageTaken.ToString("0") + ", 쓰러짐 " + w.King.Downs + ", 맞은 화살 포함 쏜 화살 " + w.Siege.Bolts
                              + ", 물러난 석궁수 " + w.Cycle.Retreated + ", 사다리 " + w.Siege.LaddersUp + "개 걸림, 휘두름 " + w.King.Swings + " · 기술 " + w.King.Casts
                              + ", 한 장 " + (w.Tick / 1800.0).ToString("0.0") + "분");
        }

        Console.WriteLine("  시설이 늦는 값을 얼마나 덜어 주는가 (습격마다 늦게 돌아오는 왕, 수리 없음)");
        var kits = new List<KeyValuePair<string, string[]>>
        {
            new KeyValuePair<string, string[]>("시설 없음", new string[0]),
            new KeyValuePair<string, string[]>("발리스타", new[] { "ballista" }),
            new KeyValuePair<string, string[]>("가시 난간", new[] { "thorns" }),
            new KeyValuePair<string, string[]>("발리스타 + 가시 난간", new[] { "ballista", "thorns" }),
        };
        foreach (var kit in kits)
        {
            var sb = new System.Text.StringBuilder("    " + kit.Key + ":");
            foreach (float late in new[] { 40f, 50f, 60f, 75f, 9999f })
            {
                float worst;
                World w = Chapter(defs, ch, 11, late, out flawless, out worst, v =>
                {
                    v.OpenShop(tree, castle);
                    v.Economy.Iron = 100;
                    for (int i = 0; i < kit.Value.Length; i++) v.Build.BuildFacility(v, i, kit.Value[i]);
                });
                sb.Append("  " + (late > 1000f ? "돌아오지 않음 " : late.ToString("0") + "초 ") + (w.Cycle.Phase == Phase.Victory ? "-" + (k.WallHp - w.Siege.WallHp).ToString("0") : "함락(" + (w.Cycle.Wave + 1) + ")"));
            }
            Console.WriteLine(sb.ToString());
        }
    }

    static void SkillTests(Defs defs, CapeDef cape, ChapterDef ch, TreeDef tree, CastleDef castle)
    {
        KingDef k = defs.King;
        var down = new InputFrame { AimY = -1f };
        Func<string[], World> with = ids =>
        {
            var w = ShopWorld(defs, cape, tree, castle, 100, 0);
            foreach (string id in ids) w.Build.Awaken(w, id);
            w.King.X = k.LaneX(2);
            w.King.Y = 2f;
            return w;
        };

        Section("기술: 장착");
        {
            var w = with(new[] { "F1", "I1", "P1" });
            Check(w.Build.Equip.Length == 2 && w.Build.Equip[0] == tree.Index("F1") && w.Build.Equip[1] == tree.Index("I1"), "깨우면 빈 칸에 들어간다. 칸은 둘, 기술은 셋");
            Check(w.Build.EquipSkill(w, 0, "P1") == Buy.Ok && w.Build.Equip[0] == tree.Index("P1"), "칸의 기술을 바꾼다");
            Check(w.Build.EquipSkill(w, 0, "I1") == Buy.Ok && w.Build.Equip[0] == tree.Index("I1") && w.Build.Equip[1] == tree.Index("P1"), "다른 칸에 있던 기술을 옮기면 자리를 바꾼다");
            Check(w.Build.EquipSkill(w, 1, "F2") == Buy.Missing, "기술이 아닌 노드는 장착할 수 없다");
            Check(w.Build.Unawaken(w, "P1") == Buy.Ok && w.Build.Equip[1] == -1, "되돌린 기술은 칸에서 빠진다");
        }

        Section("기술: 검기");
        {
            var w = with(new[] { "F1" });
            Put(w, "crossbow", 2, EnemyState.Perched, k.LedgeHeight);
            Put(w, "climber", 4, EnemyState.Climbing, 10f);
            Put(w, "climber", 3, EnemyState.Climbing, 0.5f);
            w.Step(new InputFrame { AimY = -1f, Held = Buttons.Skill1 });
            Check(w.Siege.Enemies[0].Hp == 5f && w.King.Casts == 1, "바위턱의 석궁수에 닿는다(피해 30. 체력 35에서 5가 남는다)");
            Check(w.Siege.Enemies[1].Hp == 30f, "옆 레인의 적은 맞지 않는다");
            Check(Math.Abs(w.King.SkillCd[0] - 6f) < 0.01f, "재사용 6초");
            Steps(w, 30 * 6, down);
            w.King.X = k.LaneX(3);
            w.Siege.Enemies.RemoveAt(2);
            Put(w, "climber", 3, EnemyState.Climbing, 0.4f);
            w.Step(new InputFrame { AimY = -1f, Held = Buttons.Skill1 });
            Check(w.King.Casts == 2 && w.Siege.Enemies[2].Hp == 30f, "사거리 18m 밖(벼랑 바닥)은 닿지 않는다");
            w.King.X = k.LaneX(2);
            Steps(w, 30, down);
            Put(w, "crossbow", 2, EnemyState.Perched, k.LedgeHeight);
            w.Step(new InputFrame { AimY = -1f, Held = Buttons.Skill1 });
            Check(w.King.Casts == 2, "재사용 대기 중에는 나가지 않는다");

            w = with(new[] { "F1" });
            foreach (float h in new[] { 16f, 13f, 10f, 7f }) Put(w, "shield", 2, EnemyState.Climbing, h);
            w.Step(new InputFrame { AimY = -1f, Held = Buttons.Skill1 });
            Check(w.Siege.Enemies[0].Hp == 36f && w.Siege.Enemies[2].Hp == 36f && w.Siege.Enemies[3].Hp == 45f, "가까운 셋까지 꿰뚫는다. 방패병은 위에서 오는 피해의 30%만 받는다");
            Check(w.Siege.Enemies[0].Grip == 10f, "밀어냄 2");

            w = with(new[] { "F1" });
            Put(w, "crossbow", 5, EnemyState.Perched, k.LedgeHeight);
            w.Step(new InputFrame { AimX = k.LaneX(5) - w.King.X, AimY = -6f - w.King.Y, Held = Buttons.Skill1 });
            Check(w.Siege.Enemies[0].Hp == 5f, "비스듬히 조준하면 먼 레인의 석궁수도 맞는다");
        }

        Section("기술: 내려찍기");
        {
            var w = with(new[] { "P1" });
            Put(w, "shield", 2, EnemyState.Climbing, 17f);
            Put(w, "climber", 2, EnemyState.OnWall, k.CliffHeight);
            Put(w, "climber", 4, EnemyState.Climbing, 17f);
            w.Step(new InputFrame { AimY = -1f, Held = Buttons.Skill1 });
            Steps(w, 13, down);
            Check(w.Siege.Enemies[0].Hp == 45f && w.King.DamageTaken == 0f, "뛰어오른 0.5초 동안에는 아직 치지 않는다");
            Steps(w, 3, down);
            Check(w.Siege.Enemies[0].State == EnemyState.Dead && w.Siege.Enemies[1].State == EnemyState.Dead, "방패병도 한 번에 떨어진다(밀어냄 14). 성벽 위의 적도 날아간다");
            Check(w.Siege.Enemies[2].Hp == 30f, "반경 4m 밖은 닿지 않는다");
            Check(w.Siege.FallKills == 2, "둘 다 추락사");
        }

        Section("기술: 왕의 방패");
        {
            var w = with(new[] { "I1" });
            Put(w, "climber", 2, EnemyState.OnWall, k.CliffHeight);
            Steps(w, 10, down);
            Steps(w, 30, new InputFrame { AimY = -1f, Held = Buttons.Skill1 });
            Check(Math.Abs(w.King.DamageTaken - 1.6f) < 0.01f, "자세를 잡고 있으면 앞에서 온 피해를 80% 줄인다(8 → 1.6)");
            Check(w.King.Swings == 0, "자세를 잡은 동안에는 치지 못한다");
            Steps(w, 40, new InputFrame { AimY = -1f, Held = Buttons.Skill1 });
            Check(w.King.GuardSlot == -1 && w.King.SkillCd[0] > 3f, "2초가 지나면 자세가 풀리고 재사용 대기가 시작된다");

            w = with(new[] { "I1" });
            Put(w, "climber", 2, EnemyState.OnWall, k.CliffHeight);
            Steps(w, 10, new InputFrame { AimY = 1f });
            Steps(w, 30, new InputFrame { AimY = 1f, Held = Buttons.Skill1 });
            Check(w.King.DamageTaken == 8f, "등 뒤에서 온 것은 막지 못한다");

            w = with(new[] { "I1" });
            Put(w, "climber", 2, EnemyState.OnWall, k.CliffHeight);
            Put(w, "shield", 3, EnemyState.Climbing, 17f);
            Steps(w, 30, down);
            Steps(w, 10, new InputFrame { AimY = -1f, Held = Buttons.Skill1 });
            Check(w.King.GuardParries == 1 && w.King.DamageTaken == 0f, "자세를 잡은 첫 0.35초에 맞으면 받아친다");
            Check(w.Siege.Enemies[0].State == EnemyState.Dead && w.Siege.Enemies[1].Hp == 0f || w.Siege.Enemies[1].Grip == 4f, "반격파는 반경 6m, 피해 45, 밀어냄 8. 옆 레인의 방패병에게도 닿는다");
        }

        Section("석궁수의 사격");
        {
            var w = with(new string[0]);
            Put(w, "crossbow", 0, EnemyState.Perched, k.LedgeHeight);
            Steps(w, 74, down);
            Check(w.King.DamageTaken == 0f, "2.5초가 되기 전에는 쏘지 않는다");
            Steps(w, 3, down);
            Check(w.King.DamageTaken == 10f && w.Siege.Bolts == 1, "2.5초마다 왕을 쏜다(10). 성벽 어디에 있든 닿는다");
            Steps(w, 76, down);
            Check(w.King.DamageTaken == 20f, "계속 쏜다");
            w.King.Leave();
            Steps(w, 200, down);
            Check(w.Siege.Bolts == 2, "왕이 성벽에 없으면 쏘지 않는다");

            w = with(new string[0]);
            Put(w, "crossbow", 0, EnemyState.Perched, k.LedgeHeight);
            Steps(w, 72, down);
            w.Step(new InputFrame { AimY = -1f, Held = Buttons.Guard });
            Steps(w, 6, down);
            Check(w.King.Parries == 1 && w.King.DamageTaken == 0f && w.Siege.Enemies[0].Hp == 35f, "화살도 받아칠 수 있다. 반격파는 바위턱에 닿지 않는다");
        }

        Section("사다리조");
        {
            var w = with(new string[0]);
            w.King.Leave();
            Put(w, "ladder", 2, EnemyState.Climbing, 14.5f);
            Put(w, "climber", 2, EnemyState.Climbing, 2f);
            Put(w, "climber", 3, EnemyState.Climbing, 2f);
            Steps(w, 40, down);
            Check(w.Siege.Enemies[0].State == EnemyState.Raising && w.Siege.Enemies[0].Height == 15f, "턱밑 띠에 닿으면 멈춰서 사다리를 건다");
            Steps(w, 90, down);
            Check(w.Siege.Enemies[0].State == EnemyState.Ladder && w.Siege.LaddersUp == 1 && w.Siege.Enemies[0].Hp == 60f, "3초 뒤에 사다리가 걸린다(체력 60, 붙잡기 12)");
            float h2 = w.Siege.Enemies[1].Height, h3 = w.Siege.Enemies[2].Height;
            Steps(w, 30, down);
            Check(Math.Abs(w.Siege.Enemies[1].Height - h2 - 1.8f) < 0.05f && Math.Abs(w.Siege.Enemies[2].Height - h3 - 0.9f) < 0.05f, "사다리가 걸린 레인은 두 배로 빨리 오른다");
            w.King.Return();
            w.King.X = k.LaneX(2);
            w.King.Y = 1.5f;
            for (int n = 0; n < 2; n++)
            {
                Steps(w, 19, new InputFrame { AimY = -1f, Held = Buttons.Attack });
                Steps(w, 10, down);
            }
            Check(w.Siege.LaddersDown == 1 && w.Siege.Enemies[0].State == EnemyState.Dead, "강공격 두 번에 사다리가 넘어간다");
            Check(w.Siege.Enemies[1].State != EnemyState.Climbing && w.Siege.Enemies[2].State == EnemyState.Climbing, "타고 있던 적은 떨어진다. 옆 레인은 그대로다");
            Check(w.Siege.Kills == (w.Siege.Enemies[1].State == EnemyState.Dead ? 1 : 0), "사다리는 처치로 세지 않는다");

            w = with(new string[0]);
            w.King.Leave();
            Put(w, "ladder", 2, EnemyState.Climbing, 14.9f);
            Steps(w, 30 * 5, down);
            Check(w.Siege.Enemies[0].State == EnemyState.Ladder && !w.Siege.Threat, "사다리만 남으면 습격은 끝난 것이다");
        }

        Section("대형 적");
        {
            var w = with(new string[0]);
            Put(w, "titan", 2, EnemyState.Climbing, 16f);
            Put(w, "shield", 2, EnemyState.Climbing, 9f);
            w.King.Y = 1.5f;
            int hits = 0;
            while (hits < 5 && w.Siege.Enemies[0].State == EnemyState.Climbing)
            {
                Steps(w, 19, new InputFrame { AimY = -1f, Held = Buttons.Attack });
                Steps(w, 9, down);
                hits++;
            }
            Check(hits == 3, "절벽 거수(붙잡기 24)는 강공격을 세 번 연달아 맞아야 떨어진다");
            Check(w.Siege.Enemies[0].State == EnemyState.Grounded, "턱밑에서 떨어져도 살아남는다(체력 320, 낙하 피해 115~146)");
            Check(w.Siege.Enemies[1].State == EnemyState.Dead, "떨어지며 아래의 방패병을 휩쓴다(피해 60)");
        }

        Section("기술: 곶에서");
        {
            Func<string, Rock, World> at = (id, rock) =>
            {
                var w = FlatWorld(defs, cape, rock);
                w.OpenShop(tree, castle);
                w.Economy.Star = 50;
                w.Build.Awaken(w, id);
                OnCape(w, 23f, 3f);
                w.Step(new InputFrame { AimY = 1f, Held = Buttons.Skill1 });
                return w;
            };
            World a = at("F1", Rock.Soil);
            Check(a.Cape.TilesDug == 5 && a.Cape.Land.At(11, 6) == Rock.Open && a.Cape.Land.At(11, 7) == Rock.Soil, "검기: 직선 다섯 타일을 한 번에 판다(흙)");
            a = at("F1", Rock.Soft);
            Check(a.Cape.TilesDug == 0 && a.Cape.Land.Hp[a.Cape.Land.Index(11, 4)] == 12, "검기: 연암은 강도 18만큼 깎인다(30 → 12)");
            a = at("I1", Rock.Soil);
            Check(a.Cape.TilesDug == 6 && a.Cape.Land.At(10, 3) == Rock.Open && a.Cape.Land.At(12, 2) == Rock.Open, "왕의 방패: 앞의 3 × 2타일");
            a = at("P1", Rock.Soil);
            Check(a.Cape.TilesDug == 5 && a.Cape.Land.At(11, 3) == Rock.Open && a.Cape.Land.At(9, 1) == Rock.Open, "내려찍기: 반경 2타일(열셋 가운데 빈터가 아닌 다섯)");
            Check(Math.Abs(a.King.SkillCd[0] - 8f) < 0.01f, "곶에서 써도 재사용 대기는 같다");
        }
    }

    static int PileStar(World w)
    {
        int n = 0;
        for (int i = 0; i < w.Cape.Piles.Count; i++) n += w.Cape.Piles[i].Star;
        return n;
    }

    static int Main(string[] args)
    {
        string root = Root();
        if (args.Length > 0 && args[0] == "tune") return Tune(root, args);
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
        TreeDef tree = TreeDef.Load(File.ReadAllText(Path.Combine(root, "data", "trees", "sword.json")));
        CastleDef castle = CastleDef.Load(File.ReadAllText(Path.Combine(root, "data", "castle", "castle.json")));
        ShopTests(defs, cape, ch1, tree, castle);
        SkillTests(defs, cape, ch1, tree, castle);
        EconomyTests(defs, cape, ch1, tree, castle);

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

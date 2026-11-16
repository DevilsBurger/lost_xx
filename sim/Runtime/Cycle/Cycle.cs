using System;
using System.Collections.Generic;
using System.IO;

namespace LostRegalia.Sim
{
    public enum Phase : byte
    {
        None = 0,       // 주기를 돌리지 않는다. 테스트가 습격을 손으로 시작할 때
        Calm = 1,       // 고요
        Alarm = 2,      // 경보. 종이 울렸다. 고요의 마지막 구간이다
        Assault = 3,    // 습격
        Onslaught = 4,  // 총공세
        Victory = 5,
        Fallen = 6      // 함락
    }

    public enum EventKind : byte
    {
        Bell,           // 종이 울렸다. A = 다음 습격의 번호(1부터), 총공세면 0
        AssaultStart,   // A = 습격의 번호, 총공세면 0
        AssaultEnd,     // A = 습격의 번호, B = 성벽이 깎인 양, C = 흠 없는 방어였으면 1
        Victory,
        Fallen,
        Deposit,        // 뒷문에 내려놓았다. A = 성광석, B = 흑철, C = 룬 수정
        FragmentTaken,  // 대파편을 들었다
        FragmentHome    // 대파편이 성에 닿았다. 다음 습격이 총공세다
    }

    /// <summary>시뮬레이션이 화면에 알리는 일. 소리와 효과는 이것으로 튼다. 상태가 아니므로 스냅숏에 들어가지 않는다.</summary>
    public struct SimEvent
    {
        public EventKind Kind;
        public int A, B, C;
    }

    /// <summary>
    /// 장 하나의 주기. 고요 → 경보 → 습격 → 고요 … 를 돌리고, 총공세와 승리, 함락을 정한다.
    /// </summary>
    public sealed class Cycle
    {
        public Phase Phase;
        public float TimeLeft;          // 고요와 경보: 습격까지 남은 시간
        public int Wave;                // 지금까지 끝낸 정규 습격의 수. 다음 습격은 Wave + 1번째
        public bool FragmentHome;       // 대파편을 성으로 가져왔는가. 다음 습격이 총공세가 된다
        public float WallAtStart;       // 습격이 시작될 때의 성벽 내구도
        public bool Flawless;           // 이번 습격에서 성벽이 아직 깎이지 않았는가
        public int AssaultTicks;        // 이번 습격이 이어진 틱
        public int FlawlessCount, Retreated;
        public float AlarmBonus;        // 망루가 앞당긴 경보. 상태가 아니다. Build.Apply가 적는다
        public float LastCalm;          // 마지막으로 시작한 고요의 길이

        ChapterDef chapter;

        /// <summary>장을 시작한다. 첫 고요부터.</summary>
        public void Begin(World w, ChapterDef def)
        {
            chapter = def;
            growth = w.Defs.King.BudgetGrowth;
            Wave = 0;
            FragmentHome = false;
            FlawlessCount = Retreated = 0;
            StartCalm(w, false);
        }

        /// <summary>저장을 새 World에 불러올 때. 상태는 스냅숏이, 편성은 데이터가 준다.</summary>
        public void Attach(ChapterDef def, float budgetGrowth)
        {
            chapter = def;
            growth = budgetGrowth;
        }

        float growth = 1f;

        /// <summary>
        /// 다음에 올 습격. 장을 끝내는 때는 왕이 정한다.
        /// 정해진 습격을 다 치르고도 대파편을 가져오지 않으면 마지막 편성이 습격마다 거세진다(위협 예산의 성장률만큼).
        /// 총공세는 그때까지 치른 습격의 수를 따른다. 일찍 부르면 작고, 늦게 부르면 크다.
        /// </summary>
        public WaveDef Next
        {
            get
            {
                if (chapter == null) return null;
                int regular = chapter.Waves.Length;
                if (FragmentHome) return Wave == regular ? chapter.Onslaught : chapter.Onslaught.Scaled((float)Math.Pow(growth, Wave - regular));
                if (Wave < regular) return chapter.Waves[Wave];
                return chapter.Waves[regular - 1].Scaled((float)Math.Pow(growth, Wave - regular + 1));
            }
        }

        /// <summary>대파편을 성으로 가져왔다. 고요 중이면 다음 습격이, 습격 중이면 그다음 습격이 총공세다.</summary>
        public void DeliverFragment()
        {
            FragmentHome = true;
        }

        float Of(float[] byChapter)
        {
            int i = chapter.Chapter - 1;
            return byChapter[i < byChapter.Length ? i : byChapter.Length - 1];
        }

        void StartCalm(World w, bool flawless)
        {
            Phase = Phase.Calm;
            LastCalm = Of(w.Defs.King.Calm) + (flawless ? w.Defs.King.FlawlessBonus : 0f);
            TimeLeft = LastCalm;
        }

        public void Step(World w, float dt)
        {
            if (Phase == Phase.None || Phase == Phase.Victory || Phase == Phase.Fallen) return;
            KingDef k = w.Defs.King;

            if (w.Siege.WallHp <= 0f)
            {
                Phase = Phase.Fallen;
                w.Emit(EventKind.Fallen, 0, 0, 0);
                return;
            }

            if (Phase == Phase.Calm || Phase == Phase.Alarm)
            {
                TimeLeft -= dt;
                if (Phase == Phase.Calm && TimeLeft <= Of(k.Alarm) + AlarmBonus)
                {
                    Phase = Phase.Alarm;
                    w.Emit(EventKind.Bell, FragmentHome ? 0 : Wave + 1, 0, 0);
                }
                if (TimeLeft <= 0f)
                {
                    WaveDef next = Next;
                    Phase = next.Final ? Phase.Onslaught : Phase.Assault;
                    WallAtStart = w.Siege.WallHp;
                    Flawless = true;
                    AssaultTicks = 0;
                    w.StartAssault(next.Units, Of(k.SpawnWindow));
                    w.Emit(EventKind.AssaultStart, next.Final ? 0 : Wave + 1, 0, 0);
                }
                return;
            }

            // 습격과 총공세
            AssaultTicks++;
            if (w.Siege.WallHp < WallAtStart) Flawless = false;
            if (w.Siege.Threat) return;
            Retreated += w.Siege.EndAssault();
            int lost = (int)(WallAtStart - w.Siege.WallHp + 0.5f);
            if (Phase == Phase.Onslaught)
            {
                w.Emit(EventKind.AssaultEnd, 0, lost, Flawless ? 1 : 0);
                Phase = Phase.Victory;
                w.Emit(EventKind.Victory, 0, 0, 0);
                return;
            }
            Wave++;
            if (Flawless) FlawlessCount++;
            w.Emit(EventKind.AssaultEnd, Wave, lost, Flawless ? 1 : 0);
            StartCalm(w, Flawless);
        }

        public void Write(BinaryWriter w)
        {
            w.Write((byte)Phase); w.Write(TimeLeft); w.Write(Wave); w.Write(FragmentHome);
            w.Write(WallAtStart); w.Write(Flawless); w.Write(AssaultTicks);
            w.Write(FlawlessCount); w.Write(Retreated); w.Write(LastCalm);
        }

        public void Read(BinaryReader r)
        {
            Phase = (Phase)r.ReadByte(); TimeLeft = r.ReadSingle(); Wave = r.ReadInt32(); FragmentHome = r.ReadBoolean();
            WallAtStart = r.ReadSingle(); Flawless = r.ReadBoolean(); AssaultTicks = r.ReadInt32();
            FlawlessCount = r.ReadInt32(); Retreated = r.ReadInt32(); LastCalm = r.ReadSingle();
        }
    }
}

using System.Collections.Generic;
using System.IO;

namespace LostRegalia.Sim
{
    /// <summary>
    /// 시뮬레이션의 뿌리. 고정 틱으로만 나아간다(초당 30틱).
    /// 상태 전체를 바이트로 떠 두었다가 되돌릴 수 있다. 저장과 되감기가 이것이다.
    /// </summary>
    public sealed class World
    {
        public const int TickRate = 30;
        public const float Dt = 1f / TickRate;
        const int SnapshotVersion = 6;

        public readonly Defs Defs;
        public readonly Siege Siege;
        public readonly King King;
        public readonly Cycle Cycle = new Cycle();
        public readonly Economy Economy = new Economy();
        public Build Build;                 // 없으면 사고 고르는 것이 없는 판이다
        public Cape Cape;                   // 없으면 곶이 없는 판이다(성벽만 시험할 때)
        /// <summary>이번 틱에 일어난 일. 틱이 시작될 때 비운다.</summary>
        public readonly List<SimEvent> Events = new List<SimEvent>();
        public int Tick;
        public Rng Rng;

        public World(Defs defs, ulong seed)
        {
            Defs = defs;
            Rng = new Rng(seed);
            Siege = new Siege(defs);
            King = new King(defs);
        }

        /// <summary>곶의 땅을 만들어 붙인다. 뒷문이 열린다.</summary>
        public void OpenLand(CapeDef cape, int landIndex, ulong seed)
        {
            Cape = new Cape(cape, LandGen.GenerateValid(cape, cape.Lands[landIndex], seed));
        }

        /// <summary>각성 트리와 성의 규칙을 붙인다. 살 수 있게 된다.</summary>
        public void OpenShop(TreeDef tree, CastleDef castle)
        {
            Build = new Build(tree, castle);
            Build.Apply(this);
        }

        /// <summary>입력 없이 틱 하나.</summary>
        public void Step()
        {
            Step(default(InputFrame));
        }

        /// <summary>틱 하나. 왕이 먼저 움직이고 친 다음, 적이 움직이고 친다.</summary>
        public void Step(InputFrame input)
        {
            Events.Clear();
            if (Cycle.Phase == Phase.Victory || Cycle.Phase == Phase.Fallen) return;    // 판이 끝났다
            King.Step(input, this, Dt);
            Siege.Step(Tick, Dt, King);
            if (Build != null) Build.Step(this, Dt);
            Cycle.Step(this, Dt);
            Tick++;
        }

        public void Emit(EventKind kind, int a, int b, int c)
        {
            Events.Add(new SimEvent { Kind = kind, A = a, B = b, C = c });
        }

        /// <summary>
        /// 습격을 시작한다. 적은 정해진 시간(스폰 창) 동안 고르게 나타나고, 레인은 난수로 고른다.
        /// 같은 레인에는 1.5초 안에 다시 나오지 않는다.
        /// </summary>
        public void StartAssault(IList<KeyValuePair<string, int>> units, float spawnWindow)
        {
            var order = new List<int>();
            for (int u = 0; u < units.Count; u++)
            {
                int def = Defs.EnemyIndex(units[u].Key);
                for (int c = 0; c < units[u].Value; c++) order.Add(def);
            }
            for (int i = order.Count - 1; i > 0; i--)       // 섞기
            {
                int j = Rng.Range(i + 1);
                int t = order[i];
                order[i] = order[j];
                order[j] = t;
            }
            int lanes = Defs.King.Lanes;
            var lastUse = new int[lanes];
            for (int i = 0; i < lanes; i++) lastUse[i] = -9999;
            int gap = (int)(1.5f * TickRate);
            for (int i = 0; i < order.Count; i++)
            {
                int tick = Tick + (int)(spawnWindow * TickRate * i / order.Count);
                int lane = Rng.Range(lanes);
                for (int tries = 0; tries < lanes && tick - lastUse[lane] < gap; tries++) lane = (lane + 1) % lanes;
                lastUse[lane] = tick;
                Siege.Queue.Add(new Spawn { Tick = tick, Def = order[i], Lane = lane });
            }
        }

        public byte[] Snapshot()
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(SnapshotVersion);
                w.Write(Tick);
                w.Write(Rng.State);
                Cycle.Write(w);
                Economy.Write(w);
                w.Write(Cape != null);
                if (Cape != null) Cape.Write(w);
                w.Write(Build != null);
                if (Build != null) Build.Write(w);
                King.Write(w);
                Siege.Write(w);
                w.Flush();
                return ms.ToArray();
            }
        }

        public void Restore(byte[] snapshot)
        {
            using (var ms = new MemoryStream(snapshot))
            using (var r = new BinaryReader(ms))
            {
                int version = r.ReadInt32();
                if (version != SnapshotVersion) throw new InvalidDataException("스냅숏 버전이 다르다: " + version);
                Tick = r.ReadInt32();
                Rng.State = r.ReadUInt64();
                Cycle.Read(r);
                Economy.Read(r);
                bool cape = r.ReadBoolean();
                if (cape != (Cape != null)) throw new InvalidDataException("스냅숏과 판의 곶이 맞지 않는다");
                if (cape) Cape.Read(r);
                bool build = r.ReadBoolean();
                if (build != (Build != null)) throw new InvalidDataException("스냅숏과 판의 구성이 맞지 않는다");
                if (build) Build.Read(r);
                King.Read(r);
                Siege.Read(r);
                if (build) Build.Apply(this, true);
            }
        }

        public ulong StateHash()
        {
            return Hash.Of(Snapshot());
        }
    }
}

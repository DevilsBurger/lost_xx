using System;
using System.Collections.Generic;
using System.IO;

namespace LostRegalia.Sim
{
    public enum EnemyState : byte
    {
        Climbing = 0,   // 레인을 따라 오르는 중
        Mounting = 1,   // 턱을 넘는 중. 붙잡기가 절반으로 계산된다
        OnWall = 2,     // 성벽 위. 성벽을 깎는다
        Perched = 3,    // 바위턱에 자리 잡음
        Grounded = 4,   // 떨어져서 살아남음. 잠시 뒤 다시 오른다
        Dead = 5
    }

    /// <summary>적 하나의 상태. 길 찾기가 없다. 레인 번호와 높이 값 하나로 움직인다.</summary>
    public struct Enemy
    {
        public int Def;
        public int Lane;
        public float Height;
        public float Hp;
        public float Grip;
        public float SinceKnock;
        public float Timer;
        public EnemyState State;
    }

    public struct Spawn
    {
        public int Tick;
        public int Def;
        public int Lane;
    }

    /// <summary>
    /// 성벽 한 면의 전투 상태. 등반, 올라서기, 성벽 공격, 붙잡기, 추락, 덮치기를 맡는다.
    /// 화면을 모른다. Unity 쪽은 이 상태를 읽어서 그리기만 한다.
    /// </summary>
    public sealed class Siege
    {
        readonly Defs defs;
        public float WallHp;
        // 고른 것(Build)에서 나오는 값. 상태가 아니다
        public float WallMax, FallMul = 1f, ThornDps, ThornSlow;
        public float CollideKnockMul = 1f;      // 검 파멸 가지의 '굴러떨어지는 것들'을 깨우면 1.5
        public readonly List<Enemy> Enemies = new List<Enemy>();
        public readonly List<Spawn> Queue = new List<Spawn>();
        public int Kills, FallKills, Drops;

        public Siege(Defs defs)
        {
            this.defs = defs;
            WallHp = WallMax = defs.King.WallHp;
        }

        public bool Active
        {
            get
            {
                if (Queue.Count > 0) return true;
                for (int i = 0; i < Enemies.Count; i++)
                    if (Enemies[i].State != EnemyState.Dead) return true;
                return false;
            }
        }

        /// <summary>틱 하나. king이 없으면(null) 성벽이 비어 있는 것으로 본다.</summary>
        /// <summary>성벽으로 오고 있거나 성벽 위에 있는 적이 남았는가. 바위턱에 앉은 적은 세지 않는다.</summary>
        public bool Threat
        {
            get
            {
                if (Queue.Count > 0) return true;
                for (int i = 0; i < Enemies.Count; i++)
                {
                    EnemyState s = Enemies[i].State;
                    if (s != EnemyState.Dead && s != EnemyState.Perched) return true;
                }
                return false;
            }
        }

        /// <summary>습격이 끝났다. 죽은 적을 치우고, 바위턱에 남은 적은 물러난다. 물러난 수를 돌려준다.</summary>
        public int EndAssault()
        {
            int retreated = 0;
            for (int i = 0; i < Enemies.Count; i++)
                if (Enemies[i].State != EnemyState.Dead) retreated++;
            Enemies.Clear();
            return retreated;
        }

        public void Step(int tick, float dt, King king = null)
        {
            KingDef k = defs.King;
            bool parried = false;
            for (int q = Queue.Count - 1; q >= 0; q--)
            {
                if (Queue[q].Tick > tick) continue;
                EnemyDef d = defs.Enemies[Queue[q].Def];
                Enemies.Add(new Enemy
                {
                    Def = Queue[q].Def, Lane = Queue[q].Lane, Hp = d.Hp, Grip = d.Grip,
                    SinceKnock = 999f, State = EnemyState.Climbing
                });
                Queue.RemoveAt(q);
            }

            for (int i = 0; i < Enemies.Count; i++)
            {
                Enemy e = Enemies[i];
                EnemyDef d = defs.Enemies[e.Def];
                switch (e.State)
                {
                    case EnemyState.Climbing:
                        RegenGrip(ref e, d, k, dt);
                        e.Height += d.Climb * dt * (ThornSlow > 0f && e.Height >= k.CliffHeight - k.ReachBelow ? 1f - ThornSlow : 1f);
                        if (d.Perches && e.Height >= k.LedgeHeight)
                        {
                            e.Height = k.LedgeHeight;
                            e.State = EnemyState.Perched;
                        }
                        else if (e.Height >= k.CliffHeight)
                        {
                            e.Height = k.CliffHeight;
                            e.State = EnemyState.Mounting;
                            e.Timer = k.MountTime;
                        }
                        break;
                    case EnemyState.Mounting:
                        RegenGrip(ref e, d, k, dt);
                        e.Timer -= dt;
                        if (e.Timer <= 0f)
                        {
                            e.State = EnemyState.OnWall;
                            e.Timer = 0f;
                        }
                        break;
                    case EnemyState.Perched:
                        RegenGrip(ref e, d, k, dt);
                        break;
                    case EnemyState.OnWall:
                        // 왕이 닿는 거리에 있으면 왕을 친다. 아니면 성벽을 깎는다
                        if (d.KingDamage > 0f && king != null && king.Targetable && Near(king, e.Lane, k))
                        {
                            e.Timer += dt;
                            if (e.Timer >= k.EnemyInterval)
                            {
                                e.Timer = 0f;
                                if (king.TakeHit(d.KingDamage)) parried = true;
                            }
                        }
                        else
                        {
                            e.Timer = 0f;
                            WallHp = Math.Max(0f, WallHp - d.WallDps * dt);
                        }
                        break;
                    case EnemyState.Grounded:
                        e.Timer -= dt;
                        if (e.Timer <= 0f)
                        {
                            e.State = EnemyState.Climbing;
                            e.Height = 0f;
                            e.Grip = d.Grip;
                            e.SinceKnock = 999f;
                        }
                        break;
                }
                Enemies[i] = e;
            }
            if (parried) king.CounterWave(this);
        }

        static bool Near(King king, int lane, KingDef k)
        {
            float dx = k.LaneX(lane) - king.X, dy = k.EnemyStand - king.Y;
            return dx * dx + dy * dy <= k.EnemyReach * k.EnemyReach;
        }

        static void RegenGrip(ref Enemy e, EnemyDef d, KingDef k, float dt)
        {
            e.SinceKnock += dt;
            if (e.SinceKnock > k.GripRegenDelay) e.Grip = Math.Min(d.Grip, e.Grip + k.GripRegen * dt);
        }

        /// <summary>적을 밀어낸다. 붙잡기가 다하면 떨어진다.</summary>
        public void Knock(int i, float amount)
        {
            Enemy e = Enemies[i];
            if (e.State == EnemyState.OnWall)
            {
                // 성벽 위의 적은 붙잡기가 깎이지 않는다. 한 번의 밀어냄이 붙잡기를 넘으면 벼랑 밖으로 떨어진다
                if (amount >= defs.Enemies[e.Def].Grip) Drop(i);
                return;
            }
            if (e.State != EnemyState.Climbing && e.State != EnemyState.Mounting && e.State != EnemyState.Perched) return;
            e.Grip -= e.State == EnemyState.Mounting ? amount * 2f : amount;
            e.SinceKnock = 0f;
            Enemies[i] = e;
            if (e.Grip <= 0f) Drop(i);
        }

        /// <summary>피해를 준다. 절벽에 매달린 채 죽은 적은 떨어지며 아래의 적을 덮친다.</summary>
        public void Damage(int i, float amount)
        {
            Enemy e = Enemies[i];
            if (e.State == EnemyState.Dead) return;
            e.Hp -= amount;
            if (e.Hp > 0f)
            {
                Enemies[i] = e;
                return;
            }
            bool onCliff = e.State == EnemyState.Climbing || e.State == EnemyState.Mounting || e.State == EnemyState.Perched;
            e.State = EnemyState.Dead;
            Kills++;
            Enemies[i] = e;
            if (onCliff) HitBelow(i, e.Lane, e.Height, defs.Enemies[e.Def].Size);
        }

        /// <summary>
        /// 떨어진다. 높이의 제곱에 비례한 피해를 받고, 같은 레인에서 바로 아래에 있는 적 하나를 덮친다.
        /// 덮친 적도 떨어지면 연쇄가 이어진다.
        /// </summary>
        void Drop(int i)
        {
            KingDef k = defs.King;
            Enemy e = Enemies[i];
            float h = e.Height;
            Drops++;
            e.Hp -= FallRules.FallDamage(k, h) * FallMul;
            if (e.Hp <= 0f)
            {
                e.State = EnemyState.Dead;
                Kills++;
                FallKills++;
            }
            else
            {
                e.State = EnemyState.Grounded;
                e.Timer = k.ReclimbDelay;
                e.Height = 0f;
            }
            Enemies[i] = e;
            HitBelow(i, e.Lane, h, defs.Enemies[e.Def].Size);
        }

        /// <summary>같은 레인에서 바로 아래에 있는 적 하나를 덮친다.</summary>
        void HitBelow(int self, int lane, float height, Size size)
        {
            KingDef k = defs.King;
            int below = -1;
            float best = -1f;
            for (int j = 0; j < Enemies.Count; j++)
            {
                if (j == self) continue;
                Enemy o = Enemies[j];
                if (o.Lane != lane || o.Height >= height || o.Height <= best) continue;
                if (o.State != EnemyState.Climbing && o.State != EnemyState.Perched) continue;
                below = j;
                best = o.Height;
            }
            if (below < 0) return;
            Damage(below, k.CollideDamage[(int)size]);
            if (Enemies[below].State != EnemyState.Dead)
                Knock(below, k.CollideKnock[(int)size] * CollideKnockMul);
        }

        public void Write(BinaryWriter w)
        {
            w.Write(WallHp);
            w.Write(CollideKnockMul);
            w.Write(Kills);
            w.Write(FallKills);
            w.Write(Drops);
            w.Write(Enemies.Count);
            for (int i = 0; i < Enemies.Count; i++)
            {
                Enemy e = Enemies[i];
                w.Write(e.Def);
                w.Write(e.Lane);
                w.Write(e.Height);
                w.Write(e.Hp);
                w.Write(e.Grip);
                w.Write(e.SinceKnock);
                w.Write(e.Timer);
                w.Write((byte)e.State);
            }
            w.Write(Queue.Count);
            for (int i = 0; i < Queue.Count; i++)
            {
                w.Write(Queue[i].Tick);
                w.Write(Queue[i].Def);
                w.Write(Queue[i].Lane);
            }
        }

        public void Read(BinaryReader r)
        {
            WallHp = r.ReadSingle();
            CollideKnockMul = r.ReadSingle();
            Kills = r.ReadInt32();
            FallKills = r.ReadInt32();
            Drops = r.ReadInt32();
            Enemies.Clear();
            int n = r.ReadInt32();
            for (int i = 0; i < n; i++)
            {
                var e = new Enemy();
                e.Def = r.ReadInt32();
                e.Lane = r.ReadInt32();
                e.Height = r.ReadSingle();
                e.Hp = r.ReadSingle();
                e.Grip = r.ReadSingle();
                e.SinceKnock = r.ReadSingle();
                e.Timer = r.ReadSingle();
                e.State = (EnemyState)r.ReadByte();
                Enemies.Add(e);
            }
            Queue.Clear();
            n = r.ReadInt32();
            for (int i = 0; i < n; i++)
                Queue.Add(new Spawn { Tick = r.ReadInt32(), Def = r.ReadInt32(), Lane = r.ReadInt32() });
        }
    }
}

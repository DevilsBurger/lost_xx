using System;
using System.IO;

namespace LostRegalia.Sim
{
    public enum KingMode : byte
    {
        Normal = 0,
        Dashing = 1,
        Downed = 2      // 쓰러짐. 왕좌에서 다시 일어날 때까지 조작이 없다
    }

    /// <summary>
    /// 왕. 성벽 위를 움직이고, 베고, 모아 치고, 받아치고, 대시하고, 맞고, 쓰러졌다 일어난다.
    /// 입력(InputFrame)과 틱으로만 바뀐다. 곶과 기술은 아직 없다.
    /// </summary>
    public sealed class King
    {
        readonly Defs defs;

        public float X, Y;                  // 성벽 위의 자리. y = 0이 벼랑 턱
        public float AimX, AimY = -1f;      // 조준. 처음에는 벼랑 쪽
        public float Hp;
        public KingMode Mode;
        public bool OnWall = true;          // 거짓이면 성벽을 비운 것이다. 적이 왕을 노리지 않는다

        public float Busy;                  // 공격 동작이 끝날 때까지 남은 시간
        public float Charge;                // 공격 버튼을 누르고 있던 시간
        public bool Queued;                 // 동작 중에 눌렀다 뗀 공격 한 번
        public int ComboStep;               // 다음에 나갈 연타의 번호(0~2)
        public float ComboTimer;            // 이 안에 다시 치면 연타가 이어진다
        public float DashTime, DashCd, DashDX, DashDY;
        public float IFrame;                // 남은 무적 시간
        public float ParryWindow, ParryCd;
        public float DownTimer;
        public Buttons Prev;

        public int Swings, Parries, Downs;  // 계측용
        public float DamageTaken;

        public King(Defs defs)
        {
            this.defs = defs;
            Hp = defs.King.Hp;
            Respawn();
        }

        /// <summary>적이 왕을 칠 수 있는 상태인가.</summary>
        public bool Targetable { get { return OnWall && Mode != KingMode.Downed; } }

        void Respawn()
        {
            X = defs.King.WallLength * 0.5f;
            Y = defs.King.WallWidth * 0.5f;
        }

        public void Step(InputFrame input, Siege siege, float dt)
        {
            KingDef k = defs.King;
            Buttons pressed = input.Held & ~Prev;
            Buttons released = Prev & ~input.Held;
            Prev = input.Held;
            if (!OnWall) return;

            if (Mode == KingMode.Downed)
            {
                DownTimer -= dt;
                if (DownTimer <= 0f)
                {
                    Mode = KingMode.Normal;
                    Hp = k.Hp;
                    Respawn();
                }
                return;
            }

            Busy = Math.Max(0f, Busy - dt);
            ComboTimer = Math.Max(0f, ComboTimer - dt);
            DashCd = Math.Max(0f, DashCd - dt);
            IFrame = Math.Max(0f, IFrame - dt);
            ParryWindow = Math.Max(0f, ParryWindow - dt);
            ParryCd = Math.Max(0f, ParryCd - dt);
            Hp = Math.Min(k.Hp, Hp + k.RegenOnWall * dt);

            float aim = (float)Math.Sqrt(input.AimX * input.AimX + input.AimY * input.AimY);
            if (aim > 0.01f)
            {
                AimX = input.AimX / aim;
                AimY = input.AimY / aim;
            }

            float mx = input.MoveX, my = input.MoveY;
            float ml = (float)Math.Sqrt(mx * mx + my * my);
            if (ml > 1f)
            {
                mx /= ml;
                my /= ml;
                ml = 1f;
            }

            if (Mode != KingMode.Dashing && (pressed & Buttons.Dash) != 0 && DashCd <= 0f)
            {
                // 움직이는 쪽으로. 서 있으면 조준한 쪽으로
                if (ml > 0.01f)
                {
                    DashDX = mx / ml;
                    DashDY = my / ml;
                }
                else
                {
                    DashDX = AimX;
                    DashDY = AimY;
                }
                Mode = KingMode.Dashing;
                DashTime = k.DashTime;
                DashCd = k.DashCooldown;
                IFrame = Math.Max(IFrame, k.DashIFrame);
                Charge = 0f;
                Queued = false;
            }
            bool dashing = Mode == KingMode.Dashing;
            if (dashing)
            {
                float t = Math.Min(dt, DashTime);
                float v = k.DashDistance / k.DashTime;
                X += DashDX * v * t;
                Y += DashDY * v * t;
                DashTime -= dt;
                if (DashTime <= 0f) Mode = KingMode.Normal;
            }
            else
            {
                X += mx * k.Speed * dt;
                Y += my * k.Speed * dt;
            }
            X = Math.Min(k.WallLength, Math.Max(0f, X));
            Y = Math.Min(k.WallWidth, Math.Max(0f, Y));
            if (dashing) return;

            if ((pressed & Buttons.Guard) != 0 && ParryCd <= 0f && Busy <= 0f)
            {
                ParryWindow = k.ParryWindow;
                ParryCd = k.ParryCooldown;
                Charge = 0f;
            }

            // 공격: 누르고 있는 동안 모으고, 뗄 때 나간다. 충분히 모았으면 강공격, 아니면 연타 한 번
            bool held = (input.Held & Buttons.Attack) != 0;
            if (held && Busy <= 0f) Charge += dt;
            if ((released & Buttons.Attack) != 0)
            {
                if (Busy > 0f) Queued = true;
                else if (Charge >= k.ChargeTime) Charged(siege);
                else Basic(siege);
                Charge = 0f;
            }
            else if (Queued && Busy <= 0f && !held)
            {
                Queued = false;
                Basic(siege);
            }
        }

        void Basic(Siege siege)
        {
            KingDef k = defs.King;
            int step = ComboTimer > 0f ? ComboStep : 0;
            Swings++;
            Strike(siege, k.BasicRadius, k.BasicArc, k.BasicDamage[step], k.BasicKnock[step]);
            Busy = k.BasicTimes[step];
            ComboStep = (step + 1) % k.BasicTimes.Length;
            ComboTimer = k.BasicTimes[step] + k.ComboGrace;
        }

        void Charged(Siege siege)
        {
            KingDef k = defs.King;
            Swings++;
            Strike(siege, k.ChargeRadius, 360f, k.ChargeDamage, k.ChargeKnock);
            Busy = k.ChargeRecover;
            ComboStep = 0;
            ComboTimer = 0f;
        }

        /// <summary>
        /// 근접 판정. 성벽 위의 적과 턱밑 띠의 적에게 닿는다. 밀어냄이 먼저, 피해가 다음이다.
        /// 턱밑 띠의 적은 그 레인의 벼랑 턱 자리에 있는 것으로 잰다.
        /// </summary>
        void Strike(Siege siege, float radius, float arcDegrees, float damage, float knock)
        {
            KingDef k = defs.King;
            float cos = (float)Math.Cos(arcDegrees * 0.5 * Math.PI / 180.0);
            var es = siege.Enemies;
            for (int i = 0; i < es.Count; i++)
            {
                EnemyState s = es[i].State;
                float ey;
                if (s == EnemyState.OnWall) ey = k.EnemyStand;
                else if (s == EnemyState.Mounting || (s == EnemyState.Climbing && es[i].Height >= k.CliffHeight - k.ReachBelow)) ey = 0f;
                else continue;
                float dx = k.LaneX(es[i].Lane) - X, dy = ey - Y;
                float dist = (float)Math.Sqrt(dx * dx + dy * dy);
                if (dist > radius) continue;
                if (arcDegrees < 360f && dist > 0.001f && (dx * AimX + dy * AimY) / dist < cos) continue;
                if (knock > 0f) siege.Knock(i, knock);
                s = es[i].State;
                if (s != EnemyState.Dead && s != EnemyState.Grounded) siege.Damage(i, damage);
            }
        }

        /// <summary>적의 일격. 받아쳤으면 참을 돌려준다. 그때 피해는 없고 반격파가 나간다.</summary>
        public bool TakeHit(float damage)
        {
            if (!Targetable) return false;
            if (ParryWindow > 0f)
            {
                ParryWindow = 0f;
                Parries++;
                return true;
            }
            if (IFrame > 0f) return false;
            Hp -= damage;
            DamageTaken += damage;
            IFrame = defs.King.HitIFrame;
            if (Hp <= 0f)
            {
                Hp = 0f;
                Mode = KingMode.Downed;
                DownTimer = defs.King.Revive;
                Downs++;
                Busy = Charge = ComboTimer = ParryWindow = DashTime = 0f;
                Queued = false;
            }
            return false;
        }

        /// <summary>받아치기에 성공했을 때의 반격파.</summary>
        public void CounterWave(Siege siege)
        {
            KingDef k = defs.King;
            Strike(siege, k.ParryRadius, 360f, k.ParryDamage, k.ParryKnock);
        }

        public void Write(BinaryWriter w)
        {
            w.Write(X); w.Write(Y); w.Write(AimX); w.Write(AimY);
            w.Write(Hp); w.Write((byte)Mode); w.Write(OnWall);
            w.Write(Busy); w.Write(Charge); w.Write(Queued); w.Write(ComboStep); w.Write(ComboTimer);
            w.Write(DashTime); w.Write(DashCd); w.Write(DashDX); w.Write(DashDY);
            w.Write(IFrame); w.Write(ParryWindow); w.Write(ParryCd); w.Write(DownTimer);
            w.Write((ushort)Prev);
            w.Write(Swings); w.Write(Parries); w.Write(Downs); w.Write(DamageTaken);
        }

        public void Read(BinaryReader r)
        {
            X = r.ReadSingle(); Y = r.ReadSingle(); AimX = r.ReadSingle(); AimY = r.ReadSingle();
            Hp = r.ReadSingle(); Mode = (KingMode)r.ReadByte(); OnWall = r.ReadBoolean();
            Busy = r.ReadSingle(); Charge = r.ReadSingle(); Queued = r.ReadBoolean(); ComboStep = r.ReadInt32(); ComboTimer = r.ReadSingle();
            DashTime = r.ReadSingle(); DashCd = r.ReadSingle(); DashDX = r.ReadSingle(); DashDY = r.ReadSingle();
            IFrame = r.ReadSingle(); ParryWindow = r.ReadSingle(); ParryCd = r.ReadSingle(); DownTimer = r.ReadSingle();
            Prev = (Buttons)r.ReadUInt16();
            Swings = r.ReadInt32(); Parries = r.ReadInt32(); Downs = r.ReadInt32(); DamageTaken = r.ReadSingle();
        }
    }
}

namespace LostRegalia.Sim
{
    /// <summary>
    /// 시뮬레이션의 난수. 상태가 값 하나(State)라서 스냅숏에 그대로 담긴다.
    /// UnityEngine.Random이나 System.Random을 쓰지 않는다. 같은 시드는 언제나 같은 결과를 낸다.
    /// </summary>
    public struct Rng
    {
        public ulong State;

        public Rng(ulong seed)
        {
            State = seed == 0 ? 0x9E3779B97F4A7C15UL : seed;
            Next();
        }

        public ulong Next()
        {
            ulong x = State;
            x ^= x >> 12;
            x ^= x << 25;
            x ^= x >> 27;
            State = x;
            return x * 2685821657736338717UL;
        }

        /// <summary>0 이상 n 미만의 정수.</summary>
        public int Range(int n)
        {
            return (int)(Next() % (ulong)n);
        }

        /// <summary>0 이상 1 미만의 실수.</summary>
        public float Float01()
        {
            return (Next() >> 40) * (1.0f / 16777216f);
        }
    }
}
